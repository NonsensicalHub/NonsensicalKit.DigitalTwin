using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine;
using Random = UnityEngine.Random;

public class WarehouseInventoryReview : NonsensicalMono
{
    [InfoBox("需要将主货架的货物状态注册到IOCC,用于颜色筛选")]
    [SerializeField,Label("主货架注册名称")] private string CargoStatusKey = "cargoStatus";

    #region 反射

    private static readonly FieldInfo s_autoInitField =
        typeof(WarehouseManager).GetField("m_autoInit", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo s_cargoPrefabsField =
        typeof(WarehouseManager).GetField("m_cargoPrefabs", BindingFlags.Instance | BindingFlags.NonPublic);

    #endregion

    #region 序列化字段

    [SerializeField, Label("货位数据名称"), Tooltip("此处用于异色货架读取货架地图")]
    private string m_wareHouseName;

    [SerializeField, Tooltip("主货架")] private WarehouseManager m_stackerWarehouseManager;

    [SerializeField, Tooltip("为 true 时异色层忽略货架切片列隐藏，始终显示全部高亮列；为 false 时更新格位跟随主货架当前可见列，隐藏列不打异色。")]
    private bool m_alwaysShowAllColumn;

    [SerializeField, Range(1, 6), Tooltip("异色层列位移动画的刷新帧间隔。值越大越省性能，最终位置始终会同步。")]
    private int m_columnOffsetUpdateIntervalFrames = 2;

    [Header("动态盘点层")]
    [SerializeField, Tooltip("盘点库模板。建议勾掉 Auto Init，由本脚本换材质后再 HandleInit。")]
    private WarehouseManager m_warehousePrefab;

    [SerializeField, Tooltip("材质模板。仅替换货柜上同名材质；每层会 new Material(模板)，异色不能共用同一实例。")]
    private Material[] m_materialTemplate;

    [SerializeField, Tooltip("动态层挂载点，为空则挂到本物体下")]
    private Transform m_inventoryRoot;

    #endregion

    #region 运行时状态

    private readonly List<WarehouseInventory> _inventories = new();
    private readonly List<Int4> _stackerLocs = new(256);
    private readonly List<bool> _stackerShows = new(256);
    private readonly List<Int4> _overlayLocs = new(256);
    private readonly List<bool> _overlayShows = new(256);
    private readonly List<Int4> _restoreLocs = new(256);
    private readonly HashSet<Int4> _claimed = new();
    private readonly HashSet<int> _affectedColumns = new();
    private readonly List<WarehouseManager> _pendingInits = new();

    private Int4[] _stackerLocBuffer;
    private bool[] _stackerShowBuffer;
    private Int4[] _overlayLocBuffer;
    private bool[] _overlayShowBuffer;
    private bool[] _falseStateBuffer;

    private bool _isUpdating;
    private bool _overlaySilenced;
    private bool _warehouseReady;
    private Coroutine _waitReadyRoutine;
    private Coroutine _updateRoutine;
    private Coroutine _offsetSyncRoutine;
    private Coroutine _columnRefreshRoutine;

    /// <summary>当前会话的完整高亮快照；列显隐变化时只读取对应列。</summary>
    private WarehouseInventoryHighlightBatch[] _cachedHighlightBatches;

    private readonly Dictionary<int, List<ColumnHighlightEntry>[]> _cachedHighlightsByColumn = new();
    private readonly Dictionary<Int4, bool> _cargoLookup = new();
    private readonly Dictionary<Int4, bool> _overlayStateScratch = new();
    private readonly Dictionary<Int4, bool> _stackerStateScratch = new();
    private bool _cargoLookupReady;

    /// <summary>货架切片当前列偏移（相对原点），新建/刷新层后需重放。</summary>
    private readonly Dictionary<int, Vector3> _shelfColumnOffsets = new();

    /// <summary>货架切片当前列显隐，新建/刷新层后需重放。</summary>
    private readonly Dictionary<int, bool> _shelfColumnStates = new();

    private readonly Dictionary<int, Vector3> _pendingColumnOffsets = new();
    private readonly HashSet<int> _pendingColumnRefreshes = new();
    private readonly List<int> _columnScratch = new();

    private readonly struct ColumnHighlightEntry
    {
        public readonly Int4 Location;
        public readonly bool Highlight;

        public ColumnHighlightEntry(Int4 location, bool highlight)
        {
            Location = location;
            Highlight = highlight;
        }
    }

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        if (m_stackerWarehouseManager != null)
        {
            m_stackerWarehouseManager.UseSharedGpuPicker = true;
            m_stackerWarehouseManager.IgnoreGpuPickShowCargo = true;
        }

        SetAllOverlaysEnabled(false);
        if (isActiveAndEnabled)
        {
            _waitReadyRoutine = StartCoroutine(WaitReady());
        }

        Subscribe<int, Vector3>("ShelfSliceOffsetChanged", OnShelfOffsetChange);
        Subscribe<int, bool>("ShelfSliceStateChanged", OnShelfStateChange);
    }

    private void OnEnable()
    {
        if (_waitReadyRoutine == null && !_warehouseReady)
        {
            _waitReadyRoutine = StartCoroutine(WaitReady());
        }

        TryStartPendingColumnOffsetFlush();
        TryStartPendingColumnRefreshFlush();
    }

    private void OnDisable()
    {
        if (_waitReadyRoutine != null)
        {
            StopCoroutine(_waitReadyRoutine);
            _waitReadyRoutine = null;
        }

        StopUpdateRoutine();
        if (_offsetSyncRoutine != null)
        {
            StopCoroutine(_offsetSyncRoutine);
            _offsetSyncRoutine = null;
        }

        if (_columnRefreshRoutine != null)
        {
            StopCoroutine(_columnRefreshRoutine);
            _columnRefreshRoutine = null;
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        // 未激活时销毁不会再走 OnDisable，这里幂等停协程。
        OnDisable();
        for (int i = 0; i < _inventories.Count; i++)
        {
            _inventories[i]?.DisposeRuntime();
        }

        _inventories.Clear();
    }

    #endregion

    #region 公开接口

    /// <summary>是否正在执行高亮/还原/退出等更新协程。</summary>
    public bool IsUpdating => _isUpdating;

    /// <summary>
    /// 增量更新高亮：在现有高亮之上叠加本次传入的格位，不会先清空旧高亮。
    /// 同一格被多色命中时，以快照中先处理到的批次为准。
    /// 适用：局部刷新、逐条追加结果。
    /// 全量替换请用 <see cref="ReplaceHighlight(WarehouseInventoryHighlightBatch[])"/>。
    /// </summary>
    public void ApplyIncrementalUpdate(Int4[] locations, bool[] highlightStates, Color[] colors)
    {
        if (!TryBeginUpdate(locations, highlightStates, colors))
        {
            return;
        }

        StartUpdateRoutine(RunReplace(BuildBatchesByColor(locations, highlightStates, colors), clearFirst: false));
    }

    /// <summary>
    /// 全量替换高亮：先 <see cref="RestoreAllHighlights"/> 清掉当前所有异色/堆垛机遮罩，
    /// 再按 batches 重新应用。batches[i] 对应动态盘点层 i（一层一色）。
    /// 适用：一次检索出完整多批结果、Demo/正式入口的主路径。
    /// 若只需在旧结果上打补丁，用 <see cref="ApplyIncrementalUpdate(Int4[], bool[], Color[])"/>。
    /// </summary>
    public void ReplaceHighlight(WarehouseInventoryHighlightBatch[] batches)
    {
        if (_isUpdating)
        {
            return;
        }

        if (batches == null || batches.Length == 0)
        {
            Debug.LogWarning($"{nameof(WarehouseInventoryReview)} 批次为空，已忽略。");
            return;
        }

        if (!CanCreateOrUseInventory())
        {
            Debug.LogWarning($"{nameof(WarehouseInventoryReview)} 未配置盘点库预制体，且无可用层。");
            return;
        }

        StartUpdateRoutine(RunReplace(batches, clearFirst: true));
    }

    /// <summary>
    /// 按颜色分组后全量替换，等价于先 <c>BuildBatchesByColor</c> 再
    /// <see cref="ReplaceHighlight(WarehouseInventoryHighlightBatch[])"/>。
    /// </summary>
    public void ReplaceHighlight(Int4[] locations, bool[] highlightStates, Color[] colors)
    {
        if (!TryBeginUpdate(locations, highlightStates, colors))
        {
            return;
        }

        ReplaceHighlight(BuildBatchesByColor(locations, highlightStates, colors));
    }

    /// <summary>
    /// 增量更新（单色回退）：未传 colors 时全部使用当前层回退色。
    /// </summary>
    public void ApplyIncrementalUpdate(Int4[] locations, bool[] highlightStates)
    {
        if (locations == null || highlightStates == null)
        {
            ApplyIncrementalUpdate(null, null, null);
            return;
        }

        Color fallback = GetFallbackColor();
        var colors = new Color[locations.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = fallback;
        }

        ApplyIncrementalUpdate(locations, highlightStates, colors);
    }

    /// <summary>
    /// 仅还原当前高亮视觉（堆垛机货位 + 异色层），不销毁盘点层。
    /// </summary>
    [Button]
    public void RestoreNormal()
    {
        if (_isUpdating)
        {
            return;
        }

        if (!HasAnyHighlights())
        {
            ClearHighlightCache();
            return;
        }

        StartUpdateRoutine(RunUpdate(() =>
        {
            RestoreAllHighlights();
            ClearHighlightCache();
        }));
    }

    /// <summary>
    /// 退出盘点会话：还原堆垛机、全量静默并关闭异色层、清会话临时数据。
    /// 保留已创建的盘点层与运行时材质，便于二次进入快速高亮。
    /// 可打断进行中的高亮/还原更新。
    /// </summary>
    [Button("退出盘点")]
    public void ExitReview()
    {
        StopUpdateRoutine();
        StartUpdateRoutine(RunUpdate(() =>
        {
            RestoreStackerFromCargoStatus();
            SilenceAllOverlaysFully();
            ClearSessionWorkState();
        }));
    }

    #endregion

    #region 编辑器按钮Test
    
    private void SetHighlight()
    {
        if (_isUpdating || !TryGetCargoStatus(out Int4[] locations, out bool[] cargoStates))
        {
            return;
        }

        BuildHighlightDelta(locations, cargoStates, out Int4[] deltaLocations, out bool[] highlightStates);
        if (deltaLocations.Length == 0)
        {
            return;
        }

        Color fallback = GetFallbackColor();
        var colors = new Color[deltaLocations.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = fallback;
        }

        ReplaceHighlight(deltaLocations, highlightStates, colors);
    }

    #endregion

    #region 事件处理

    private void OnShelfOffsetChange(int column, Vector3 offset)
    {
        _shelfColumnOffsets[column] = offset;
        _pendingColumnOffsets[column] = offset;
        TryStartPendingColumnOffsetFlush();
    }

    private void OnShelfStateChange(int column, bool visible)
    {
        _shelfColumnStates[column] = visible;
        // AlwaysShowAllColumn 时异色层保持列可见；否则跟随切片。
        ApplyColumnStateToActiveInventories(column, m_alwaysShowAllColumn || visible);

        // 跟随列显隐时，只刷新发生变化的列，不再整库 Restore + Apply。
        if (!m_alwaysShowAllColumn && _cachedHighlightBatches != null)
        {
            _pendingColumnRefreshes.Add(column);
            TryStartPendingColumnRefreshFlush();
        }
    }

    private void TryStartPendingColumnOffsetFlush()
    {
        if (!isActiveAndEnabled || _offsetSyncRoutine != null || _pendingColumnOffsets.Count == 0)
        {
            return;
        }

        _offsetSyncRoutine = StartCoroutine(FlushPendingColumnOffsets());
    }

    private void TryStartPendingColumnRefreshFlush()
    {
        if (!isActiveAndEnabled || _columnRefreshRoutine != null || _pendingColumnRefreshes.Count == 0)
        {
            return;
        }

        _columnRefreshRoutine = StartCoroutine(FlushPendingColumnRefreshes());
    }

    private IEnumerator FlushPendingColumnRefreshes()
    {
        yield return null;
        while (_isUpdating)
        {
            yield return null;
        }

        if (m_alwaysShowAllColumn || _cachedHighlightBatches == null)
        {
            _pendingColumnRefreshes.Clear();
            _columnRefreshRoutine = null;
            yield break;
        }

        _columnScratch.Clear();
        _columnScratch.AddRange(_pendingColumnRefreshes);
        _pendingColumnRefreshes.Clear();
        for (int i = 0; i < _columnScratch.Count; i++)
        {
            int column = _columnScratch[i];
            ApplyCachedColumnHighlight(column, IsStackerColumnVisible(column));
        }

        _columnRefreshRoutine = null;
    }

    private IEnumerator FlushPendingColumnOffsets()
    {
        int interval = Mathf.Max(1, m_columnOffsetUpdateIntervalFrames);
        while (_pendingColumnOffsets.Count > 0)
        {
            for (int i = 0; i < interval; i++)
            {
                yield return null;
            }

            _columnScratch.Clear();
            foreach (int column in _pendingColumnOffsets.Keys)
            {
                _columnScratch.Add(column);
            }

            for (int i = 0; i < _columnScratch.Count; i++)
            {
                int column = _columnScratch[i];
                if (_pendingColumnOffsets.TryGetValue(column, out Vector3 offset))
                {
                    ApplyColumnOffsetToActiveInventories(column, offset);
                    _pendingColumnOffsets.Remove(column);
                }
            }
        }

        _offsetSyncRoutine = null;
    }

    #endregion

    #region 协程

    private void StartUpdateRoutine(IEnumerator routine)
    {
        StopUpdateRoutine();
        if (!isActiveAndEnabled)
        {
            Debug.LogWarning($"{nameof(WarehouseInventoryReview)} 所在物体未激活，已忽略更新协程。");
            return;
        }

        _updateRoutine = StartCoroutine(routine);
    }

    private void StopUpdateRoutine()
    {
        if (_updateRoutine == null)
        {
            _isUpdating = false;
            return;
        }

        StopCoroutine(_updateRoutine);
        _updateRoutine = null;
        _isUpdating = false;
    }

    private IEnumerator RunReplace(WarehouseInventoryHighlightBatch[] batches, bool clearFirst)
    {
        _isUpdating = true;
        try
        {
            yield return WaitReady();
            if (clearFirst)
            {
                CacheHighlightBatches(batches);
            }
            else
            {
                MergeCachedHighlightBatches(batches);
            }

            WarehouseInventoryHighlightBatch[] targetBatches = _cachedHighlightBatches ?? batches;
            yield return EnsureInventories(targetBatches.Length);

            // 单趟合并旧高亮清理与新快照应用，每层仅提交一次 SetCargoState。
            ApplyHighlightSnapshot(targetBatches);
        }
        finally
        {
            _isUpdating = false;
            _updateRoutine = null;
        }
    }

    private IEnumerator RunUpdate(Action action)
    {
        _isUpdating = true;
        try
        {
            yield return WaitReady();
            action();
        }
        finally
        {
            _isUpdating = false;
            _updateRoutine = null;
        }
    }

    private IEnumerator WaitReady()
    {
        while (m_stackerWarehouseManager != null && !m_stackerWarehouseManager.Inited)
        {
            yield return null;
        }

        foreach (var inventory in _inventories)
        {
            while (inventory is { IsValid: true, Inited: false })
            {
                yield return null;
            }
        }

        while (!_warehouseReady && !TryGetCargoStatus(out _, out _))
        {
            yield return null;
        }

        if (!_overlaySilenced)
        {
            SilenceOverlayOnce();
        }

        _warehouseReady = true;
        _waitReadyRoutine = null;
    }

    /// <summary>
    /// 先同步创建缺失层并启动 Init，再协程等待全部 Inited（避免一层层串行空等）。
    /// </summary>
    private IEnumerator EnsureInventories(int count)
    {
        int need = count - _inventories.Count;
        if (need <= 0)
        {
            yield break;
        }

        if (m_warehousePrefab == null)
        {
            Debug.LogWarning(
                $"{nameof(WarehouseInventoryReview)} 需要 {count} 层，当前 {_inventories.Count}，未配置 m_warehousePrefab。");
            yield break;
        }

        _pendingInits.Clear();
        for (int n = 0; n < need; n++)
        {
            if (!TryCreateInventoryLayer(_inventories.Count, out WarehouseManager pending))
            {
                yield break;
            }

            _pendingInits.Add(pending);
        }

        while (true)
        {
            bool allReady = true;
            for (int i = 0; i < _pendingInits.Count; i++)
            {
                WarehouseManager manager = _pendingInits[i];
                if (manager != null && !manager.Inited)
                {
                    allReady = false;
                    break;
                }
            }

            if (allReady)
            {
                break;
            }

            yield return null;
        }

        if (!TryGetCargoStatus(out Int4[] locations, out _))
        {
            _pendingInits.Clear();
            yield break;
        }

        bool[] silent = GetFalseStates(locations.Length);
        for (int i = 0; i < _pendingInits.Count; i++)
        {
            WarehouseManager manager = _pendingInits[i];
            if (manager == null)
            {
                continue;
            }

            manager.SetCargoState(locations, silent, true);
            manager.enabled = false;
        }

        _pendingInits.Clear();
        SyncShelfSliceToInventories();
    }

    #endregion

    #region 动态层创建

    private bool TryCreateInventoryLayer(int index, out WarehouseManager instance)
    {
        instance = null;
        Transform parent = m_inventoryRoot != null ? m_inventoryRoot : transform;

        var holder = new GameObject($"InventoryCreate_{index}");
        holder.transform.SetParent(parent, false);
        holder.SetActive(false);

        instance = Instantiate(m_warehousePrefab, holder.transform);
        instance.WarehouseName(m_wareHouseName);
        instance.gameObject.name = $"{m_warehousePrefab.name}_Batch{index}";
        s_autoInitField?.SetValue(instance, false);
        instance.UseSharedGpuPicker = true;

        if (!TryBuildRuntimeCargo(instance, out Material[] runtimeMaterials, out GameObject[] runtimeCargoPrefabs))
        {
            Destroy(holder);
            instance = null;
            return false;
        }

        holder.SetActive(true);
        instance.transform.SetParent(parent, true);
        Destroy(holder);

        instance.HandleInit();

        Color seedColor = runtimeMaterials != null && runtimeMaterials.Length > 0
            ? runtimeMaterials[0].color
            : Color.cyan;

        var inventory = new WarehouseInventory();
        inventory.SetupRuntime(instance, runtimeMaterials, runtimeCargoPrefabs, seedColor);
        _inventories.Add(inventory);
        return true;
    }

    private bool TryBuildRuntimeCargo(
        WarehouseManager manager,
        out Material[] runtimeMaterials,
        out GameObject[] runtimeCargoPrefabs)
    {
        runtimeMaterials = null;
        runtimeCargoPrefabs = null;

        if (s_cargoPrefabsField == null)
        {
            Debug.LogError($"{nameof(WarehouseInventoryReview)} 无法反射 WarehouseManager.m_cargoPrefabs。");
            return false;
        }

        var sourcePrefabs = s_cargoPrefabsField.GetValue(manager) as GameObject[];
        if (sourcePrefabs == null || sourcePrefabs.Length == 0)
        {
            Debug.LogError($"{nameof(WarehouseInventoryReview)} 盘点库预制体未配置 cargoPrefabs。");
            return false;
        }

        var templateByName = new Dictionary<string, Material>();
        if (m_materialTemplate != null)
        {
            for (int t = 0; t < m_materialTemplate.Length; t++)
            {
                Material template = m_materialTemplate[t];
                if (template != null && !string.IsNullOrEmpty(template.name))
                {
                    templateByName[template.name] = template;
                }
            }
        }

        if (templateByName.Count == 0)
        {
            Debug.LogError($"{nameof(WarehouseInventoryReview)} 未配置可用的材质模板。");
            return false;
        }

        var materialList = new List<Material>(4);
        runtimeCargoPrefabs = new GameObject[sourcePrefabs.Length];

        for (int i = 0; i < sourcePrefabs.Length; i++)
        {
            GameObject source = sourcePrefabs[i];
            if (source == null)
            {
                continue;
            }

            GameObject cargoClone = Instantiate(source, manager.transform);
            cargoClone.name = $"{source.name}_Mat{i}";
            cargoClone.SetActive(false);

            MeshRenderer[] renderers = cargoClone.GetComponentsInChildren<MeshRenderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                MeshRenderer renderer = renderers[r];
                Material[] shared = renderer.sharedMaterials;
                var next = new Material[shared.Length];
                bool replacedAny = false;
                for (int m = 0; m < shared.Length; m++)
                {
                    Material sourceMat = shared[m];
                    if (sourceMat != null
                        && templateByName.TryGetValue(sourceMat.name, out Material template)
                        && template != null)
                    {
                        var runtimeMat = new Material(template)
                        {
                            name = $"{template.name}_Runtime_{i}_{m}",
                            hideFlags = HideFlags.HideAndDontSave,
                            enableInstancing = true
                        };
                        next[m] = runtimeMat;
                        materialList.Add(runtimeMat);
                        replacedAny = true;
                    }
                    else
                    {
                        next[m] = sourceMat;
                    }
                }

                if (replacedAny)
                {
                    renderer.sharedMaterials = next;
                }
            }

            runtimeCargoPrefabs[i] = cargoClone;
        }

        s_cargoPrefabsField.SetValue(manager, runtimeCargoPrefabs);
        runtimeMaterials = materialList.ToArray();
        return runtimeMaterials.Length > 0;
    }

    #endregion

    #region 高亮应用与还原

    private void CacheHighlightBatches(WarehouseInventoryHighlightBatch[] batches)
    {
        if (batches == null)
        {
            ClearHighlightCache();
            return;
        }

        _cachedHighlightBatches = new WarehouseInventoryHighlightBatch[batches.Length];
        for (int i = 0; i < batches.Length; i++)
        {
            WarehouseInventoryHighlightBatch batch = batches[i];
            if (batch == null)
            {
                continue;
            }

            _cachedHighlightBatches[i] = CloneNormalizedBatch(batch);
        }

        RebuildColumnHighlightIndex();
        InvalidateCargoLookup();
    }

    /// <summary>同一批内相同格位按最后一次状态归一，避免集合状态与最终视觉不一致。</summary>
    private static WarehouseInventoryHighlightBatch CloneNormalizedBatch(
        WarehouseInventoryHighlightBatch batch)
    {
        if (batch == null || !batch.IsValid)
        {
            return new WarehouseInventoryHighlightBatch(
                batch?.Locations != null ? (Int4[])batch.Locations.Clone() : Array.Empty<Int4>(),
                batch?.HighlightStates != null ? (bool[])batch.HighlightStates.Clone() : Array.Empty<bool>(),
                batch?.Color ?? Color.clear);
        }

        var stateByLocation = new Dictionary<Int4, bool>(batch.Locations.Length);
        for (int i = 0; i < batch.Locations.Length; i++)
        {
            stateByLocation[batch.Locations[i]] = batch.HighlightStates[i];
        }

        var locations = new Int4[stateByLocation.Count];
        var states = new bool[stateByLocation.Count];
        int cursor = 0;
        foreach (KeyValuePair<Int4, bool> pair in stateByLocation)
        {
            locations[cursor] = pair.Key;
            states[cursor] = pair.Value;
            cursor++;
        }

        return new WarehouseInventoryHighlightBatch(locations, states, batch.Color);
    }

    private void MergeCachedHighlightBatches(WarehouseInventoryHighlightBatch[] updates)
    {
        if (_cachedHighlightBatches == null || _cachedHighlightBatches.Length == 0)
        {
            CacheHighlightBatches(updates);
            return;
        }

        var merged = new List<WarehouseInventoryHighlightBatch>(_cachedHighlightBatches);
        for (int i = 0; i < updates.Length; i++)
        {
            WarehouseInventoryHighlightBatch update = updates[i];
            if (update == null || !update.IsValid)
            {
                continue;
            }

            int targetIndex = -1;
            for (int b = 0; b < merged.Count; b++)
            {
                if (merged[b] != null && WarehouseInventory.ColorsEqual(merged[b].Color, update.Color))
                {
                    targetIndex = b;
                    break;
                }
            }

            if (targetIndex < 0)
            {
                merged.Add(new WarehouseInventoryHighlightBatch(
                    (Int4[])update.Locations.Clone(),
                    (bool[])update.HighlightStates.Clone(),
                    update.Color));
                continue;
            }

            WarehouseInventoryHighlightBatch current = merged[targetIndex];
            var stateByLocation = new Dictionary<Int4, bool>(current.Locations.Length + update.Locations.Length);
            for (int n = 0; n < current.Locations.Length; n++)
            {
                stateByLocation[current.Locations[n]] = current.HighlightStates[n];
            }

            for (int n = 0; n < update.Locations.Length; n++)
            {
                stateByLocation[update.Locations[n]] = update.HighlightStates[n];
            }

            var locations = new Int4[stateByLocation.Count];
            var states = new bool[stateByLocation.Count];
            int cursor = 0;
            foreach (KeyValuePair<Int4, bool> pair in stateByLocation)
            {
                locations[cursor] = pair.Key;
                states[cursor] = pair.Value;
                cursor++;
            }

            merged[targetIndex] = new WarehouseInventoryHighlightBatch(locations, states, current.Color);
        }

        _cachedHighlightBatches = merged.ToArray();
        RebuildColumnHighlightIndex();
        InvalidateCargoLookup();
    }

    private void RebuildColumnHighlightIndex()
    {
        _cachedHighlightsByColumn.Clear();
        if (_cachedHighlightBatches == null)
        {
            return;
        }

        int batchCount = _cachedHighlightBatches.Length;
        for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
        {
            WarehouseInventoryHighlightBatch batch = _cachedHighlightBatches[batchIndex];
            if (batch == null || !batch.IsValid)
            {
                continue;
            }

            for (int i = 0; i < batch.Locations.Length; i++)
            {
                Int4 location = batch.Locations[i];
                if (!_cachedHighlightsByColumn.TryGetValue(
                        location.Y,
                        out List<ColumnHighlightEntry>[] entriesByBatch))
                {
                    entriesByBatch = new List<ColumnHighlightEntry>[batchCount];
                    _cachedHighlightsByColumn.Add(location.Y, entriesByBatch);
                }

                entriesByBatch[batchIndex] ??= new List<ColumnHighlightEntry>();
                entriesByBatch[batchIndex].Add(
                    new ColumnHighlightEntry(location, batch.HighlightStates[i]));
            }
        }
    }

    private void ClearHighlightCache()
    {
        _cachedHighlightBatches = null;
        _cachedHighlightsByColumn.Clear();
        _pendingColumnRefreshes.Clear();
    }

    private void ApplyCachedColumnHighlight(int column, bool visible)
    {
        if (!TryGetCargoLookup(out Dictionary<Int4, bool> lookup))
        {
            return;
        }

        _cachedHighlightsByColumn.TryGetValue(
            column,
            out List<ColumnHighlightEntry>[] entriesByBatch);
        _claimed.Clear();
        _stackerStateScratch.Clear();

        for (int batchIndex = 0; batchIndex < _inventories.Count; batchIndex++)
        {
            WarehouseInventory inventory = _inventories[batchIndex];
            if (inventory == null || !inventory.IsValid)
            {
                continue;
            }

            _overlayLocs.Clear();
            _overlayShows.Clear();
            _restoreLocs.Clear();
            inventory.CopyHighlightsInColumn(column, _restoreLocs);
            for (int i = 0; i < _restoreLocs.Count; i++)
            {
                Int4 location = _restoreLocs[i];
                _overlayLocs.Add(location);
                _overlayShows.Add(false);
                inventory.RemoveHighlight(location);
                if (!_claimed.Contains(location))
                {
                    _stackerStateScratch[location] = HasCargo(location, lookup);
                }
            }

            if (visible
                && entriesByBatch != null
                && batchIndex < entriesByBatch.Length
                && entriesByBatch[batchIndex] != null)
            {
                WarehouseInventoryHighlightBatch cachedBatch = _cachedHighlightBatches[batchIndex];
                inventory.ChangeColor(cachedBatch.Color);
                List<ColumnHighlightEntry> entries = entriesByBatch[batchIndex];
                for (int i = 0; i < entries.Count; i++)
                {
                    ColumnHighlightEntry entry = entries[i];
                    if (!entry.Highlight)
                    {
                        _overlayLocs.Add(entry.Location);
                        _overlayShows.Add(false);
                        inventory.RemoveHighlight(entry.Location);
                        if (!_claimed.Contains(entry.Location))
                        {
                            _stackerStateScratch[entry.Location] = HasCargo(entry.Location, lookup);
                        }

                        continue;
                    }

                    if (!HasCargo(entry.Location, lookup) || !_claimed.Add(entry.Location))
                    {
                        continue;
                    }

                    _overlayLocs.Add(entry.Location);
                    _overlayShows.Add(true);
                    inventory.AddHighlight(entry.Location);
                    _stackerStateScratch[entry.Location] = false;
                }
            }

            if (_overlayLocs.Count > 0)
            {
                FlushCargoState(
                    inventory.WarehouseManager,
                    _overlayLocs,
                    _overlayShows,
                    ref _overlayLocBuffer,
                    ref _overlayShowBuffer);
                SyncShelfColumnToInventory(inventory, column);
            }
        }

        _stackerLocs.Clear();
        _stackerShows.Clear();
        foreach (KeyValuePair<Int4, bool> pair in _stackerStateScratch)
        {
            _stackerLocs.Add(pair.Key);
            _stackerShows.Add(pair.Value);
        }

        if (_stackerLocs.Count > 0)
        {
            FlushCargoState(
                m_stackerWarehouseManager,
                _stackerLocs,
                _stackerShows,
                ref _stackerLocBuffer,
                ref _stackerShowBuffer);
            SyncShelfColumnToStacker(column);
        }

        RefreshOverlayEnabled();
    }

    private void SilenceOverlayOnce()
    {
        if (!TryGetCargoStatus(out Int4[] locations, out _))
        {
            return;
        }

        bool[] silent = GetFalseStates(locations.Length);
        for (int i = 0; i < _inventories.Count; i++)
        {
            WarehouseInventory inventory = _inventories[i];
            if (inventory == null || !inventory.IsValid)
            {
                continue;
            }

            inventory.WarehouseManager.SetCargoState(locations, silent, true);
            inventory.SetEnabled(false);
        }

        _overlaySilenced = true;
        SetAllOverlaysEnabled(false);
    }

    /// <summary>
    /// 合并清理旧高亮与应用新快照：每个异色层、主货架各提交一次批量状态。
    /// </summary>
    private void ApplyHighlightSnapshot(WarehouseInventoryHighlightBatch[] batches)
    {
        if (batches == null || batches.Length == 0
                            || !TryGetCargoLookup(out Dictionary<Int4, bool> lookup))
        {
            return;
        }

        if (batches.Length > _inventories.Count)
        {
            Debug.LogWarning(
                $"{nameof(WarehouseInventoryReview)} 收到 {batches.Length} 批，当前仅有 {_inventories.Count} 层，多余批次已忽略。");
        }

        _affectedColumns.Clear();
        _claimed.Clear();
        _stackerStateScratch.Clear();

        for (int batchIndex = 0; batchIndex < _inventories.Count; batchIndex++)
        {
            WarehouseInventory inventory = _inventories[batchIndex];
            if (inventory == null || !inventory.IsValid)
            {
                continue;
            }

            _overlayStateScratch.Clear();
            _restoreLocs.Clear();
            _restoreLocs.AddRange(inventory.HighlightedLocations);
            for (int i = 0; i < _restoreLocs.Count; i++)
            {
                Int4 location = _restoreLocs[i];
                _overlayStateScratch[location] = false;
                _affectedColumns.Add(location.Y);
                if (!_claimed.Contains(location))
                {
                    _stackerStateScratch[location] = HasCargo(location, lookup);
                }
            }

            inventory.ClearHighlights();
            WarehouseInventoryHighlightBatch batch =
                batchIndex < batches.Length ? batches[batchIndex] : null;
            if (batch != null && batch.IsValid)
            {
                inventory.ChangeColor(batch.Color);

                Int4[] locations = batch.Locations;
                bool[] highlightStates = batch.HighlightStates;
                for (int i = 0; i < locations.Length; i++)
                {
                    Int4 location = locations[i];
                    bool highlight = highlightStates[i];
                    bool stackerColumnVisible = IsStackerColumnVisible(location.Y);

                    if (highlight && !m_alwaysShowAllColumn && !stackerColumnVisible)
                    {
                        continue;
                    }

                    if (highlight && !HasCargo(location, lookup))
                    {
                        continue;
                    }

                    if (highlight && !_claimed.Add(location))
                    {
                        continue;
                    }

                    _overlayStateScratch[location] = highlight;
                    _affectedColumns.Add(location.Y);

                    if (highlight)
                    {
                        inventory.AddHighlight(location);
                        // 即使主货架列当前隐藏，也要保存 false，避免列重新显示时原色与异色重叠。
                        _stackerStateScratch[location] = false;
                    }
                    else if (!_claimed.Contains(location))
                    {
                        inventory.RemoveHighlight(location);
                        _stackerStateScratch[location] = HasCargo(location, lookup);
                    }
                }
            }

            _overlayLocs.Clear();
            _overlayShows.Clear();
            foreach (KeyValuePair<Int4, bool> pair in _overlayStateScratch)
            {
                _overlayLocs.Add(pair.Key);
                _overlayShows.Add(pair.Value);
            }

            FlushCargoState(
                inventory.WarehouseManager,
                _overlayLocs,
                _overlayShows,
                ref _overlayLocBuffer,
                ref _overlayShowBuffer);
        }

        _stackerLocs.Clear();
        _stackerShows.Clear();
        foreach (KeyValuePair<Int4, bool> pair in _stackerStateScratch)
        {
            _stackerLocs.Add(pair.Key);
            _stackerShows.Add(pair.Value);
        }

        FlushCargoState(
            m_stackerWarehouseManager,
            _stackerLocs,
            _stackerShows,
            ref _stackerLocBuffer,
            ref _stackerShowBuffer);
        RefreshOverlayEnabled();
        SyncAffectedShelfColumns();
    }

    private void RestoreAllHighlights()
    {
        if (!HasAnyHighlights() || !TryGetCargoLookup(out Dictionary<Int4, bool> lookup))
        {
            return;
        }

        _affectedColumns.Clear();
        _stackerLocs.Clear();
        _stackerShows.Clear();

        for (int i = 0; i < _inventories.Count; i++)
        {
            WarehouseInventory inventory = _inventories[i];
            if (inventory == null || !inventory.HasHighlights)
            {
                continue;
            }

            _overlayLocs.Clear();
            _overlayShows.Clear();
            _restoreLocs.Clear();
            _restoreLocs.AddRange(inventory.HighlightedLocations);

            for (int n = 0; n < _restoreLocs.Count; n++)
            {
                Int4 loc = _restoreLocs[n];
                _overlayLocs.Add(loc);
                _overlayShows.Add(false);
                _affectedColumns.Add(loc.Y);
                _stackerLocs.Add(loc);
                _stackerShows.Add(HasCargo(loc, lookup));
            }

            FlushCargoState(inventory.WarehouseManager, _overlayLocs, _overlayShows, ref _overlayLocBuffer,
                ref _overlayShowBuffer);
            inventory.ClearHighlights();
        }

        FlushCargoState(m_stackerWarehouseManager, _stackerLocs, _stackerShows, ref _stackerLocBuffer,
            ref _stackerShowBuffer);
        SetAllOverlaysEnabled(false);
        SyncAffectedShelfColumns();
    }

    private static WarehouseInventoryHighlightBatch[] BuildBatchesByColor(
        Int4[] locations,
        bool[] highlightStates,
        Color[] colors)
    {
        var order = new List<Color>();
        var locMap = new Dictionary<Color, List<Int4>>(new ColorEqualityComparer());
        var stateMap = new Dictionary<Color, List<bool>>(new ColorEqualityComparer());

        for (int i = 0; i < locations.Length; i++)
        {
            Color color = colors[i];
            if (!locMap.TryGetValue(color, out List<Int4> locs))
            {
                order.Add(color);
                locs = new List<Int4>();
                locMap[color] = locs;
                stateMap[color] = new List<bool>();
            }

            locs.Add(locations[i]);
            stateMap[color].Add(highlightStates[i]);
        }

        var batches = new WarehouseInventoryHighlightBatch[order.Count];
        for (int i = 0; i < order.Count; i++)
        {
            Color color = order[i];
            batches[i] = new WarehouseInventoryHighlightBatch(
                locMap[color].ToArray(),
                stateMap[color].ToArray(),
                color);
        }

        return batches;
    }

    private sealed class ColorEqualityComparer : IEqualityComparer<Color>
    {
        public bool Equals(Color x, Color y) => WarehouseInventory.ColorsEqual(x, y);

        // 近似相等不具备可安全量化的传递性，常量哈希保证 Equals 相等时哈希必相等。
        // 颜色批次数很少，线性比较成本可忽略。
        public int GetHashCode(Color c) => 0;
    }

    #endregion

    #region 货位状态缓冲

    private static void FlushCargoState(
        WarehouseManager manager,
        List<Int4> locs,
        List<bool> shows,
        ref Int4[] locBuffer,
        ref bool[] showBuffer)
    {
        if (manager == null || locs.Count == 0)
        {
            return;
        }

        int count = locs.Count;
        locBuffer = EnsureExactArray(locBuffer, count);
        showBuffer = EnsureExactArray(showBuffer, count);
        locs.CopyTo(locBuffer);
        shows.CopyTo(showBuffer);
        manager.SetCargoState(locBuffer, showBuffer, true);
    }

    private bool[] GetFalseStates(int length)
    {
        if (_falseStateBuffer == null || _falseStateBuffer.Length != length)
        {
            _falseStateBuffer = new bool[length];
        }

        return _falseStateBuffer;
    }

    private static T[] EnsureExactArray<T>(T[] buffer, int count)
    {
        if (buffer == null || buffer.Length != count)
        {
            return new T[count];
        }

        return buffer;
    }

    #endregion

    #region 库存辅助

    private void ApplyColumnOffsetToActiveInventories(int column, Vector3 offset)
    {
        for (int i = 0; i < _inventories.Count; i++)
        {
            WarehouseInventory inventory = _inventories[i];
            if (inventory != null && inventory.HasHighlightsInColumn(column))
            {
                inventory.SetColumnOffset(column, offset);
            }
        }
    }

    private void ApplyColumnStateToActiveInventories(int column, bool visible)
    {
        for (int i = 0; i < _inventories.Count; i++)
        {
            WarehouseInventory inventory = _inventories[i];
            if (inventory != null && inventory.HasHighlightsInColumn(column))
            {
                inventory.SetColumnState(column, visible);
            }
        }
    }

    private void SyncShelfColumnToInventory(WarehouseInventory inventory, int column)
    {
        if (inventory == null || !inventory.IsValid)
        {
            return;
        }

        if (_shelfColumnOffsets.TryGetValue(column, out Vector3 offset))
        {
            inventory.SetColumnOffset(column, offset);
        }

        if (_shelfColumnStates.TryGetValue(column, out bool visible))
        {
            inventory.SetColumnState(column, m_alwaysShowAllColumn || visible);
        }
    }

    private void SyncShelfColumnToStacker(int column)
    {
        if (m_stackerWarehouseManager == null || !m_stackerWarehouseManager.Inited)
        {
            return;
        }

        if (_shelfColumnOffsets.TryGetValue(column, out Vector3 offset))
        {
            m_stackerWarehouseManager.SetColumnOffset(column, offset);
        }

        if (_shelfColumnStates.TryGetValue(column, out bool visible))
        {
            m_stackerWarehouseManager.SetColumnState(column, visible);
        }
    }

    private void SyncAffectedShelfColumns()
    {
        foreach (int column in _affectedColumns)
        {
            for (int i = 0; i < _inventories.Count; i++)
            {
                WarehouseInventory inventory = _inventories[i];
                if (inventory != null && inventory.HasHighlightsInColumn(column))
                {
                    SyncShelfColumnToInventory(inventory, column);
                }
            }

            SyncShelfColumnToStacker(column);
        }
    }

    /// <summary>
    /// 把货架切片的列显隐/偏移重放到所有异色层。
    /// 顺序：先 Offset 再 State（Offset 会按 ShowCargo 写显示，必须放在 State 前，否则会冲掉列隐藏）。
    /// m_AlwaysShowAllColumn 为 true 时异色层列一律可见，仅同步偏移。
    /// </summary>
    private void SyncShelfSliceToInventories()
    {
        if (_inventories.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<int, Vector3> pair in _shelfColumnOffsets)
        {
            for (int i = 0; i < _inventories.Count; i++)
            {
                _inventories[i]?.SetColumnOffset(pair.Key, pair.Value);
            }
        }

        foreach (KeyValuePair<int, bool> pair in _shelfColumnStates)
        {
            bool visible = m_alwaysShowAllColumn || pair.Value;
            for (int i = 0; i < _inventories.Count; i++)
            {
                _inventories[i]?.SetColumnState(pair.Key, visible);
            }
        }
    }

    /// <summary>
    /// 主货架该列当前是否显示（只看切片缓存，不受 AlwaysShowAllColumn 影响）。
    /// 尚未收到该列事件时默认视为可见。
    /// </summary>
    private bool IsStackerColumnVisible(int column)
    {
        return !_shelfColumnStates.TryGetValue(column, out bool visible) || visible;
    }

    /// <summary>
    /// SetCargoState 会冲掉列显隐矩阵，需把当前切片状态重打到堆垛机上。
    /// 顺序：先 Offset 再 State，保证隐藏列最终不显示。
    /// </summary>
    private void SyncShelfSliceToStacker()
    {
        if (m_stackerWarehouseManager == null || !m_stackerWarehouseManager.Inited)
        {
            return;
        }

        foreach (KeyValuePair<int, Vector3> pair in _shelfColumnOffsets)
        {
            m_stackerWarehouseManager.SetColumnOffset(pair.Key, pair.Value);
        }

        foreach (KeyValuePair<int, bool> pair in _shelfColumnStates)
        {
            m_stackerWarehouseManager.SetColumnState(pair.Key, pair.Value);
        }
    }

    private void RefreshOverlayEnabled()
    {
        for (int i = 0; i < _inventories.Count; i++)
        {
            WarehouseInventory inventory = _inventories[i];
            if (inventory == null || !inventory.IsValid)
            {
                continue;
            }

            inventory.SetEnabled(inventory.HasHighlights);
        }
    }

    private void SetAllOverlaysEnabled(bool enabled)
    {
        for (int i = 0; i < _inventories.Count; i++)
        {
            _inventories[i]?.SetEnabled(enabled);
        }
    }

    /// <summary>
    /// 按 IOCC 还原堆垛机 ShowCargo，再按当前切片列显隐/偏移重放，
    /// 避免单列显示时全量 SetCargoState 把隐藏列货物一并刷出来。
    /// </summary>
    private void RestoreStackerFromCargoStatus()
    {
        if (m_stackerWarehouseManager == null || !m_stackerWarehouseManager.Inited)
        {
            return;
        }

        if (!TryGetCargoStatus(out Int4[] locations, out bool[] cargoStates))
        {
            return;
        }

        m_stackerWarehouseManager.SetCargoState(locations, cargoStates, true);
        SyncShelfSliceToStacker();
    }

    /// <summary>异色层全量静默、清高亮、关闭 Update；不销毁层实例。</summary>
    private void SilenceAllOverlaysFully()
    {
        if (_inventories.Count == 0)
        {
            return;
        }

        if (TryGetCargoStatus(out Int4[] locations, out _))
        {
            bool[] silent = GetFalseStates(locations.Length);
            for (int i = 0; i < _inventories.Count; i++)
            {
                WarehouseInventory inventory = _inventories[i];
                if (inventory == null || !inventory.IsValid || !inventory.Inited)
                {
                    continue;
                }

                inventory.WarehouseManager.SetCargoState(locations, silent, true);
                inventory.ClearHighlights();
                inventory.SetEnabled(false);
            }
        }
        else
        {
            for (int i = 0; i < _inventories.Count; i++)
            {
                WarehouseInventory inventory = _inventories[i];
                if (inventory == null)
                {
                    continue;
                }

                inventory.ClearHighlights();
                inventory.SetEnabled(false);
            }
        }

        SyncShelfSliceToInventories();
        _overlaySilenced = true;
    }

    /// <summary>清会话临时缓冲内容；保留列表容量、盘点层、切片缓存、就绪标记。</summary>
    private void ClearSessionWorkState()
    {
        _stackerLocs.Clear();
        _stackerShows.Clear();
        _overlayLocs.Clear();
        _overlayShows.Clear();
        _restoreLocs.Clear();
        _claimed.Clear();
        _pendingInits.Clear();
        ClearHighlightCache();
        InvalidateCargoLookup();
    }

    private bool CanCreateOrUseInventory()
    {
        if (m_warehousePrefab != null)
        {
            return true;
        }

        for (int i = 0; i < _inventories.Count; i++)
        {
            if (_inventories[i] != null && _inventories[i].IsValid)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasAnyHighlights()
    {
        for (int i = 0; i < _inventories.Count; i++)
        {
            if (_inventories[i] != null && _inventories[i].HasHighlights)
            {
                return true;
            }
        }

        return false;
    }

    private Color GetFallbackColor()
    {
        for (int i = 0; i < _inventories.Count; i++)
        {
            WarehouseInventory inventory = _inventories[i];
            if (inventory != null && inventory.IsValid)
            {
                return inventory.Color;
            }
        }

        return Color.cyan;
    }

    #endregion

    #region 校验

    private bool TryBeginUpdate(Int4[] locations, bool[] highlightStates, Color[] colors)
    {
        if (_isUpdating)
        {
            return false;
        }

        if (locations == null || highlightStates == null || colors == null || locations.Length == 0)
        {
            Debug.LogWarning($"{nameof(WarehouseInventoryReview)} 增量更新参数为空，已忽略。");
            return false;
        }

        if (locations.Length != highlightStates.Length || locations.Length != colors.Length)
        {
            Debug.LogWarning($"{nameof(WarehouseInventoryReview)} 增量更新参数长度不一致，已忽略。");
            return false;
        }

        if (!CanCreateOrUseInventory())
        {
            Debug.LogWarning($"{nameof(WarehouseInventoryReview)} 未配置盘点库预制体，且无可用层。");
            return false;
        }

        return true;
    }

    #endregion

    #region 货位状态辅助

    public bool TryGetCargoStatus(out Int4[] locations, out bool[] cargoStates)
    {
        locations = null;
        cargoStates = null;

        if (!IOCC.TryGet(CargoStatusKey, out (Int4[] locs, bool[] states) status))
        {
            return false;
        }

        locations = status.locs;
        cargoStates = status.states;
        return locations != null && cargoStates != null && locations.Length == cargoStates.Length;
    }

    private bool TryGetCargoLookup(out Dictionary<Int4, bool> lookup)
    {
        lookup = _cargoLookup;
        if (_cargoLookupReady)
        {
            return true;
        }

        if (!TryGetCargoStatus(out Int4[] locations, out bool[] cargoStates))
        {
            Debug.LogWarning($"{nameof(WarehouseInventoryReview)} 未找到 IOCC 键: {CargoStatusKey}");
            return false;
        }

        _cargoLookup.Clear();
        _cargoLookup.EnsureCapacity(locations.Length);
        for (int i = 0; i < locations.Length; i++)
        {
            _cargoLookup[locations[i]] = cargoStates[i];
        }

        _cargoLookupReady = true;
        return true;
    }

    private void InvalidateCargoLookup()
    {
        _cargoLookupReady = false;
        _cargoLookup.Clear();
    }

    private static bool HasCargo(Int4 loc, Dictionary<Int4, bool> lookup) =>
        lookup.TryGetValue(loc, out bool hasCargo) && hasCargo;

    #endregion

    #region 调试辅助

    private static void BuildHighlightDelta(
        Int4[] locations,
        bool[] cargoStates,
        out Int4[] deltaLocations,
        out bool[] highlightStates)
    {
        var locList = new List<Int4>();
        var stateList = new List<bool>();

        for (int i = 0; i < cargoStates.Length; i++)
        {
            if (cargoStates[i] && Random.Range(0, 2) == 1)
            {
                locList.Add(locations[i]);
                stateList.Add(true);
            }
        }

        deltaLocations = locList.ToArray();
        highlightStates = stateList.ToArray();
    }

    #endregion
}
