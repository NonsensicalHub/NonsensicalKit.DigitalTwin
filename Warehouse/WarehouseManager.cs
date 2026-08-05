using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NaughtyAttributes;
using NonsensicalKit.Core;
using UnityEngine;
using UnityEngine.Events;

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    /// <summary>
    /// 货位索引规则：x 为 layer，y 为 column，z 为 row，w 为 depth。
    /// 维度顺序为“层、列、排、深”。
    /// </summary>
    public sealed class WarehouseManager : NonsensicalMono
    {
        [SerializeField] private string m_warehouseName;
        [SerializeField] private bool m_autoInit = true;
        [SerializeField, Label("默认显示所有货物")] private bool m_defaultShowAllCargo = true;
        [SerializeField, Label("非法货位索引报警"), Tooltip("关闭后，非法货位索引仅静默丢弃（适合对接假数据），不再打 Warning 日志。")]
        private bool m_logInvalidSlotIndex = true;
        [SerializeField] private bool m_enableHighlightCargo = true;

        [SerializeField] private GameObject[] m_cargoPrefabs;
        [SerializeField] private GameObject m_highlightCargo;
        [SerializeField] private GameObject m_highlightIndicator;
        [SerializeField] private Camera m_renderCamera;
        [SerializeField] private WarehouseChunkLevel m_chunkLevel = WarehouseChunkLevel.Medium;
        [SerializeField, Min(0f)] private float m_chunkCullDistance = 0f;
        [SerializeField, Range(1, 6)] private int m_updateIntervalFrames = 2;
        [SerializeField] private bool m_logWarehousePerf;
        [SerializeField] private bool m_enableGpuPicking;
        [SerializeField] private bool m_debugGpuPicking;
        [SerializeField, Label("共享 GPU Picking")] private bool m_useSharedGpuPicker;
        [SerializeField, Label("Picking 忽略 ShowCargo 校验")] private bool m_ignoreGpuPickShowCargo;

        public bool Inited => _inited;

        /// <summary>
        /// 当前已加载仓位尺寸（层 X、列 Y、排 Z、深 W）。未就绪时为 0。
        /// </summary>
        public Int4 RuntimeBinSize => _binDataStore.IsReady ? _binDataStore.Size : new Int4(0, 0, 0, 0);

        /// <summary>最近一次 GPU Picking 的调试 RT 预览（需开启 <see cref="EnableGpuPicking"/> 和 <see cref="DebugGpuPicking"/>）。</summary>
        public Texture2D LastGpuPickPreview => _gpuPicker?.LastPickPreview;

        public bool FlipGpuPickPreviewX => _gpuPicker is { FlipPreviewX: true };
        public bool FlipGpuPickPreviewY => _gpuPicker is { FlipPreviewY: true };

        /// <summary>最近一次 GPU Picking 的调试信息。</summary>
        public WarehouseGpuPickDebugInfo LastGpuPickDebugInfo => _gpuPicker?.LastDebugInfo ?? WarehouseGpuPickDebugInfo.Empty("gpu picking disabled");

        /// <summary>
        /// 全仓库货物实例的全局显隐乘数 [0,1]（材质 <c>_DitherVisibility</c>）。
        /// </summary>
        public float GlobalCargoVisibility => _globalCargoVisibility;

        private bool _inited;
        private float _globalCargoVisibility = 1f;
        private CargoConfig[] _cargoConfigs;
        private Matrix4x4 _ltwMatrix;
        private readonly Plane[] _frameFrustumPlanes = new Plane[6];
        private readonly WarehouseBinDataStore _binDataStore = new WarehouseBinDataStore();
        private readonly WarehouseUpdateScheduler _updateScheduler = new WarehouseUpdateScheduler();
        private WarehouseGpuPicker _gpuPicker;
        private WarehouseHighlightController _highlightController;
        private bool _destroying;
        private CancellationTokenSource _initCts;

        private static WarehouseGpuPicker s_sharedGpuPicker;
        private static readonly HashSet<WarehouseManager> s_sharedGpuManagers = new HashSet<WarehouseManager>();
        private static readonly List<CargoConfig> s_sharedGpuPickConfigs = new List<CargoConfig>();

        private float _nextPerfLogTime;

        public UnityEvent<float> ChangeGlobalCargoVisibility;

        public void WarehouseName(string a)
        {
           m_warehouseName = a;
           
        }

        public void Change(float value)
        {
            SetGlobalCargoVisibility(value);
        }

        private void OnValidate()
        {
            if (m_chunkCullDistance < 0f) m_chunkCullDistance = 0f;
            _binDataStore?.SetLogInvalidSlotIndex(m_logInvalidSlotIndex);
        }

        private void Awake()
        {
            ChangeGlobalCargoVisibility.AddListener(Change);
            _binDataStore.SetDefaultShowCargo(m_defaultShowAllCargo);
            _binDataStore.SetLogInvalidSlotIndex(m_logInvalidSlotIndex);

            _highlightController = new WarehouseHighlightController(m_highlightCargo, m_highlightIndicator);

            if (m_enableGpuPicking || m_useSharedGpuPicker)
            {
                EnsureGpuPicker();
            }

            if (m_autoInit)
            {
                Init(BeginInitToken()).Forget();
            }
        }

        private void Update()
        {
            if (_destroying || !HasCargoConfigs()) return;

            Camera renderCamera = ResolveRenderCamera();
            bool transformDirty = false;
            if (transform.localToWorldMatrix != _ltwMatrix)
            {
                _ltwMatrix = transform.localToWorldMatrix;
                transformDirty = true;
            }

            if (transformDirty) RequestConfigUpdate();

            Plane[] frustumPlanes = null;
            if (renderCamera != null)
            {
                GeometryUtility.CalculateFrustumPlanes(renderCamera, _frameFrustumPlanes);
                frustumPlanes = _frameFrustumPlanes;
            }

            // WebGL：块包围盒 + 视锥在部分项目/浏览器组合下易误判，导致整块不画；实例绘制本身已有超大 worldBounds。
            bool skipChunkCull = Application.platform == RuntimePlatform.WebGLPlayer;
            Camera effectiveRenderCamera = skipChunkCull ? null : renderCamera;
            Plane[] effectiveFrustumPlanes = skipChunkCull ? null : frustumPlanes;

            foreach (var config in _cargoConfigs)
            {
                if (config == null) continue;
                if (transformDirty) config.UpdateWarehouseTransform(_ltwMatrix, false);
                config.RenderLoads(effectiveRenderCamera, effectiveFrustumPlanes);
            }

            TryExecuteScheduledConfigUpdate();
            LogPerfIfNeeded();

            if (m_enableGpuPicking && m_debugGpuPicking && _gpuPicker != null && _inited)
            {
                Camera previewCamera = ResolveRenderCamera();
                if (previewCamera != null)
                {
                    _gpuPicker.SetContinuousPreview(
                        previewCamera,
                        GetGpuPickConfigs(),
                        _globalCargoVisibility);
                }
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _destroying = true;
            CancelInit();
            _updateScheduler.ClearPending();
            ReleaseGpuPicker();
            ReleaseCargoConfigs();
        }

        public void HandleInit()
        {
            if (_destroying)
            {
                return;
            }

            Init(BeginInitToken()).Forget();
        }

        /// <summary>
        /// 获取坐标映射
        /// </summary>
        /// <param name="cellLocation">货物位置坐标</param>
        /// <returns></returns>
        public RuntimeBinData GetRuntimeBinData(Int4 cellLocation)
        {
            return _binDataStore.TryGet(cellLocation, out var binData) ? binData : null;
        }


        #region Enable GPU Picking

        public bool EnableGpuPicking
        {
            get => m_enableGpuPicking;
            set
            {
                if (m_enableGpuPicking == value) return;
                m_enableGpuPicking = value;
                if (m_enableGpuPicking)
                {
                    EnsureGpuPicker();
                }
                else if (!m_useSharedGpuPicker)
                {
                    ReleaseGpuPicker();
                }
            }
        }

        public bool UseSharedGpuPicker
        {
            get => m_useSharedGpuPicker;
            set
            {
                if (m_useSharedGpuPicker == value)
                {
                    return;
                }

                ReleaseGpuPicker();
                m_useSharedGpuPicker = value;
                if (m_enableGpuPicking || m_useSharedGpuPicker)
                {
                    EnsureGpuPicker();
                }
            }
        }

        public bool IgnoreGpuPickShowCargo
        {
            get => m_ignoreGpuPickShowCargo;
            set => m_ignoreGpuPickShowCargo = value;
        }

        public bool DebugGpuPicking
        {
            get => m_debugGpuPicking;
            set
            {
                m_debugGpuPicking = value;
                if (_gpuPicker != null)
                {
                    _gpuPicker.DebugEnabled = value;
                }
            }
        }

        #endregion

        #region Cargo Status Setting

        public void SetColumnOffset(int columnIndex, Vector3 offset)
        {
            if (!CanProcessColumn(columnIndex)) return;
            Quaternion rotation = transform.rotation;
            _binDataStore.ApplyToColumn(columnIndex, (cellLocation, binData) =>
            {
                Matrix4x4 matrix = Matrix4x4.TRS(binData.Pos + offset, rotation, Vector3.one);
                ApplyToConfigs(cellLocation, matrix, binData.ShowCargo);
            });

            RequestConfigUpdate();
        }

        public void SetColumnState(int columnIndex, bool state)
        {
            if (!CanProcessColumn(columnIndex)) return;
            Quaternion rotation = transform.rotation;
            _binDataStore.ApplyToColumn(columnIndex, (cellLocation, binData) =>
            {
                Matrix4x4 matrix = Matrix4x4.TRS(binData.Pos, rotation, Vector3.one);
                ApplyToConfigs(cellLocation, matrix, state && binData.ShowCargo);
            });

            RequestConfigUpdate();
        }

        public void SetCargoState(Int4[] cellsLocation, Vector3[] cellsPos, bool autoUpdate = false)
        {
            ApplyBatchCargoState(
                cellsLocation,
                cellsPos,
                "[Warehouse] 批量位置更新参数无效，已忽略。",
                (location, pos) => SetCargoState(location, pos, false),
                autoUpdate);
        }

        public void SetCargoState(Int4 cellLocation, Vector3 cellPos, bool autoUpdate = false)
        {
            if (!HasCargoConfigs() || !_binDataStore.TryGet(cellLocation, out RuntimeBinData binData)) return;
            Matrix4x4 matrix = Matrix4x4.TRS(cellPos, transform.rotation, Vector3.one);
            binData.Pos = cellPos;
            binData.CachedMatrix = matrix;
            binData.HasCachedMatrix = true;
            ApplyToConfigs(cellLocation, matrix, binData.ShowCargo);

            if (autoUpdate) RequestConfigUpdate();
        }

        public void SetCargoState(Int4[] cellsLocation, bool[] show, bool autoUpdate = false)
        {
            ApplyBatchCargoState(
                cellsLocation,
                show,
                "[Warehouse] 批量显示更新参数无效，已忽略。",
                (location, visible) => SetCargoState(location, visible, false),
                autoUpdate);
        }

        /// <summary>
        /// 批量写入结构启用状态与显示状态（布局编辑器 / 规则热更新用）。
        /// </summary>
        public void ApplyStructuralAndShowStates(
            Int4[] cellsLocation,
            bool[] slotEnabled,
            bool[] show,
            bool autoUpdate = true)
        {
            if (!HasCargoConfigs())
            {
                return;
            }

            if (cellsLocation == null || slotEnabled == null || show == null ||
                cellsLocation.Length == 0 ||
                cellsLocation.Length != slotEnabled.Length ||
                cellsLocation.Length != show.Length)
            {
                Debug.LogWarning("[Warehouse] 结构/显示批量更新参数无效，已忽略。");
                return;
            }

            for (int i = 0; i < cellsLocation.Length; i++)
            {
                if (!_binDataStore.TryGet(cellsLocation[i], out RuntimeBinData binData))
                {
                    continue;
                }

                binData.SlotEnabled = slotEnabled[i];
                bool effectiveShow = slotEnabled[i] && show[i];
                if (binData.ShowCargo == effectiveShow)
                {
                    continue;
                }

                Matrix4x4 matrix = ResolveCargoStateMatrix(binData, effectiveShow);
                binData.ShowCargo = effectiveShow;
                ApplyToConfigs(cellsLocation[i], matrix, effectiveShow);
            }

            if (autoUpdate)
            {
                RequestConfigUpdate();
            }
        }

        public void SetCargoState(Int4 cellLocation, bool show, bool autoUpdate = true)
        {
            if (!HasCargoConfigs() || !_binDataStore.TryGet(cellLocation, out RuntimeBinData binData)) return;
            if (!binData.SlotEnabled)
            {
                show = false;
            }

            if (binData.ShowCargo == show) return;

            Matrix4x4 matrix = ResolveCargoStateMatrix(binData, show);

            binData.ShowCargo = show;
            ApplyToConfigs(cellLocation, matrix, show);

            if (autoUpdate) RequestConfigUpdate();
        }

        /// <summary>
        /// 设置全部货位的显示状态。
        /// </summary>
        /// <param name="show">true 全部显示，false 全部不显示。</param>
        /// <param name="autoUpdate">是否立即请求分块重建。</param>
        public void SetAllCargoState(bool show, bool autoUpdate = true)
        {
            if (!HasCargoConfigs() || !_binDataStore.IsReady) return;

            _binDataStore.ForEachBin((layer, column, row, depth, binData) =>
            {
                bool effectiveShow = show && binData.SlotEnabled;
                if (binData.ShowCargo == effectiveShow) return;

                var cellLocation = new Int4(layer, column, row, depth);
                Matrix4x4 matrix = ResolveCargoStateMatrix(binData, effectiveShow);
                binData.ShowCargo = effectiveShow;
                ApplyToConfigs(cellLocation, matrix, effectiveShow);
            });

            if (autoUpdate) RequestConfigUpdate();
        }

        /// <summary>
        /// 清空全部货位显示（全部不显示），并隐藏高亮。
        /// </summary>
        public void ClearAllCargo(bool autoUpdate = true)
        {
            HideHighlightBin();
            SetAllCargoState(false, autoUpdate);
        }

        /// <summary>
        /// 全局控制所有货物 GPU 实例的显隐（材质 <c>_DitherVisibility</c>，当帧生效，无分块重建）。
        /// </summary>
        /// <param name="visibility">0 全隐，1 全显，中间值为稀疏显示。</param>
        public void SetGlobalCargoVisibility(float visibility, bool autoUpdate = true)
        {
            if (!HasCargoConfigs())
            {
                return;
            }

            float clampedVisibility = Mathf.Clamp01(visibility);
            if (Mathf.Approximately(_globalCargoVisibility, clampedVisibility))
            {
                return;
            }

            _globalCargoVisibility = clampedVisibility;
            for (int i = 0; i < _cargoConfigs.Length; i++)
            {
                _cargoConfigs[i]?.SetGlobalVisibility(clampedVisibility, false);
            }
        }

        /// <summary>
        /// 全局显示或隐藏所有货物实例（等价于 <see cref="SetGlobalCargoVisibility(float,bool)"/> 的 0/1）。
        /// </summary>
        public void SetGlobalCargoVisibility(bool visible, bool autoUpdate = true)
        {
            SetGlobalCargoVisibility(visible ? 1f : 0f, autoUpdate);
        }

        #endregion

        #region LocalHighlight

        public bool LocateHighlightBin(Int4 cellLocation)
        {
            if (!_highlightController.CanHighlight())
            {
                Debug.LogWarning("[Warehouse] 高光货位对象未配置（货物和指示物均为空）。");
                return false;
            }

            if (!_binDataStore.TryGet(cellLocation, out RuntimeBinData binData))
            {
                HideHighlightBin();
                return false;
            }

            return _highlightController.Locate(transform, binData,m_ignoreGpuPickShowCargo);
        }

        public bool LocateHighlightBin(int layer, int column, int row, int depth)
        {
            return LocateHighlightBin(new Int4(layer, column, row, depth));
        }

        public void HideHighlightBin()
        {
            _highlightController.Hide();
        }

        #endregion

        #region GPU Picking

        public async UniTask<bool> TryPickCargoAsync(
            Vector2 screenPosition,
            Action<Int4> onPicked = null,
            bool locateHighlight = true)
        {
            return await TryPickCargoAsync(screenPosition, m_renderCamera ?? ResolveRenderCamera(), onPicked, locateHighlight);
        }

        /// <summary>
        /// Gpu picking 点击货物,返回货架货物映射坐标
        /// </summary>
        /// <param name="screenPosition">屏幕坐标</param>
        /// <param name="renderCamera">渲染相机</param>
        /// <param name="onPicked">点击事件</param>
        /// <param name="locateHighlight">是否触发高亮事件</param>
        /// <returns></returns>
        public async UniTask<bool> TryPickCargoAsync(
            Vector2 screenPosition,
            Camera renderCamera,
            Action<Int4> onPicked = null,
            bool locateHighlight = true)
        {
            if (_destroying || !_inited || !HasCargoConfigs() || !_binDataStore.IsReady || _gpuPicker == null)
            {
                return false;
            }

            if (!m_enableGpuPicking)
            {
                return false;
            }

            if (renderCamera == null)
            {
                Debug.LogWarning("[Warehouse] GPU Picking 失败：未找到可用 Camera。");
                return false;
            }

            EnsureGpuPicker();
            IReadOnlyList<CargoConfig> pickConfigs = GetGpuPickConfigs();
            WarehousePickResult result = await _gpuPicker.PickAsync(
                screenPosition,
                renderCamera,
                pickConfigs,
                _binDataStore.Size,
                _globalCargoVisibility,
                _binDataStore,
                m_ignoreGpuPickShowCargo);

            if (!result.Hit)
            {
                return false;
            }

            if (locateHighlight)
            {
                LocateHighlightBin(result.Location);
            }

            onPicked?.Invoke(result.Location);
            return true;
        }

        private void EnsureGpuPicker()
        {
            if (m_useSharedGpuPicker)
            {
                if (s_sharedGpuPicker == null)
                {
                    s_sharedGpuPicker = new WarehouseGpuPicker();
                }

                s_sharedGpuManagers.Add(this);
                _gpuPicker = s_sharedGpuPicker;
                if (m_enableGpuPicking)
                {
                    _gpuPicker.DebugEnabled = m_debugGpuPicking;
                }

                return;
            }

            if (_gpuPicker != null)
            {
                _gpuPicker.DebugEnabled = m_debugGpuPicking;
                return;
            }

            _gpuPicker = new WarehouseGpuPicker
            {
                DebugEnabled = m_debugGpuPicking
            };
        }

        private void ReleaseGpuPicker()
        {
            bool wasShared = s_sharedGpuManagers.Remove(this);
            if (wasShared)
            {
                _gpuPicker = null;
                if (s_sharedGpuManagers.Count == 0 && s_sharedGpuPicker != null)
                {
                    s_sharedGpuPicker.Release();
                    s_sharedGpuPicker = null;
                    s_sharedGpuPickConfigs.Clear();
                }

                return;
            }

            if (_gpuPicker == null)
            {
                return;
            }

            _gpuPicker.Release();
            _gpuPicker = null;
        }

        private IReadOnlyList<CargoConfig> GetGpuPickConfigs()
        {
            if (!m_useSharedGpuPicker)
            {
                return _cargoConfigs;
            }

            s_sharedGpuPickConfigs.Clear();
            Int4 dimensions = _binDataStore.Size;
            foreach (WarehouseManager manager in s_sharedGpuManagers)
            {
                if (manager == null
                    || manager._destroying
                    || !manager.m_useSharedGpuPicker
                    || !manager._inited
                    || !manager.isActiveAndEnabled
                    || !manager.HasCargoConfigs()
                    || manager.m_warehouseName != m_warehouseName
                    || !HasSameDimensions(manager._binDataStore.Size, dimensions))
                {
                    continue;
                }

                for (int i = 0; i < manager._cargoConfigs.Length; i++)
                {
                    CargoConfig config = manager._cargoConfigs[i];
                    if (config != null)
                    {
                        s_sharedGpuPickConfigs.Add(config);
                    }
                }
            }

            return s_sharedGpuPickConfigs;
        }

        private static bool HasSameDimensions(Int4 a, Int4 b)
        {
            return a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.W == b.W;
        }

        #endregion

        private bool CanProcessColumn(int columnIndex)
        {
            if (!HasCargoConfigs() || !_binDataStore.IsReady) return false;
            if (_binDataStore.IsColumnInRange(columnIndex)) return true;
            Debug.LogWarning($"[Warehouse] 非法列索引: {columnIndex}");
            return false;
        }

        private static bool HasSameLengthData<T1, T2>(T1[] first, T2[] second)
        {
            return first != null && second != null && first.Length > 0 && first.Length == second.Length;
        }

        private void ApplyBatchCargoState<TValue>(
            Int4[] cellsLocation,
            TValue[] values,
            string invalidMessage,
            Action<Int4, TValue> applyAction,
            bool autoUpdate)
        {
            if (!HasCargoConfigs()) return;
            if (!HasSameLengthData(cellsLocation, values))
            {
                Debug.LogWarning(invalidMessage);
                return;
            }

            for (int i = 0; i < cellsLocation.Length; i++)
            {
                applyAction(cellsLocation[i], values[i]);
            }

            if (autoUpdate) RequestConfigUpdate();
        }

        private void ApplyToConfigs(Int4 cellLocation, Matrix4x4 matrix, bool show)
        {
            for (int i = 0; i < _cargoConfigs.Length; i++)
            {
                _cargoConfigs[i]?.SetNewState(cellLocation, matrix, show, false);
            }
        }

        private Matrix4x4 ResolveCargoStateMatrix(RuntimeBinData binData, bool show)
        {
            if (!show)
            {
                return binData.CachedMatrix;
            }

            if (binData.HasCachedMatrix)
            {
                return binData.CachedMatrix;
            }

            Matrix4x4 matrix = Matrix4x4.TRS(binData.Pos, transform.rotation, Vector3.one);
            binData.CachedMatrix = matrix;
            binData.HasCachedMatrix = true;
            return matrix;
        }

        private async UniTaskVoid Init(CancellationToken cancellationToken)
        {
            if (_inited || _destroying)
            {
                return;
            }

            try
            {
                bool loaded = await ReadDataFile(cancellationToken);
                if (!loaded || ShouldAbortInit(cancellationToken))
                {
                    return;
                }

                bool cargoReady = await InitCargo(cancellationToken);
                if (!cargoReady || ShouldAbortInit(cancellationToken))
                {
                    return;
                }

                Subscribe();
            }
            catch (OperationCanceledException)
            {
                ReleaseCargoConfigs();
            }
        }

        private void Subscribe()
        {
            if (m_enableHighlightCargo)
            { 
                AddHandler<Int4, bool>("LocateHighlightBin", m_warehouseName, LocateHighlightBin);
            }
            Subscribe("HideHighlightBin", m_warehouseName, HideHighlightBin);
        }

        private async UniTask<bool> ReadDataFile(CancellationToken cancellationToken)
        {
            bool loaded = await _binDataStore.LoadAsync(m_warehouseName)
                .AttachExternalCancellation(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return loaded;
        }

        private async UniTask<bool> InitCargo(CancellationToken cancellationToken)
        {
            try
            {
                if (ShouldAbortInit(cancellationToken))
                {
                    return false;
                }

                var initializer = new WarehouseCargoInitializer(
                    _binDataStore,
                    m_cargoPrefabs,
                    m_chunkLevel,
                    m_chunkCullDistance);
                if (!initializer.ValidateInputs(out string error))
                {
                    Debug.LogError(error);
                    return false;
                }

                _ltwMatrix = transform.localToWorldMatrix;
                _cargoConfigs = initializer.CreateConfigs(_ltwMatrix);
                if (ShouldAbortInit(cancellationToken))
                {
                    ReleaseCargoConfigs();
                    return false;
                }

                // 捕获本地引用，避免 OnDestroy 清空字段后线程池读到 null。
                CargoConfig[] configs = _cargoConfigs;
                Quaternion initRotation = transform.rotation;
                if (WarehousePlatformCompat.CpuInstancingBuildMustUseMainThread)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    initializer.BuildInitialStates(configs, initRotation);
                }
                else
                {
                    await UniTask.RunOnThreadPool(
                        () => initializer.BuildInitialStates(configs, initRotation),
                        cancellationToken: cancellationToken);
                }

                if (ShouldAbortInit(cancellationToken))
                {
                    ReleaseCargoConfigs();
                    return false;
                }

                foreach (var item in configs)
                {
                    item?.UpdateParts().Forget();
                }

                await UniTask.SwitchToMainThread(cancellationToken);
                if (ShouldAbortInit(cancellationToken))
                {
                    ReleaseCargoConfigs();
                    return false;
                }

                _inited = true;
                return true;
            }
            catch (OperationCanceledException)
            {
                ReleaseCargoConfigs();
                throw;
            }
            catch (Exception e)
            {
                ReleaseCargoConfigs();
                Debug.LogError($"GPU实例模型初始化异常: {e}");
                return false;
            }
        }

        private CancellationToken BeginInitToken()
        {
            CancelInit();
            _initCts = new CancellationTokenSource();
            return _initCts.Token;
        }

        private void CancelInit()
        {
            if (_initCts == null)
            {
                return;
            }

            try
            {
                _initCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _initCts.Dispose();
            _initCts = null;
        }

        private bool ShouldAbortInit(CancellationToken cancellationToken)
        {
            return _destroying || cancellationToken.IsCancellationRequested;
        }

        private void ReleaseCargoConfigs()
        {
            if (_cargoConfigs == null)
            {
                return;
            }

            CargoConfig[] configs = _cargoConfigs;
            _cargoConfigs = null;
            _inited = false;
            foreach (var item in configs)
            {
                item?.Release();
            }
        }

        private bool HasCargoConfigs()
        {
            return _cargoConfigs != null && _cargoConfigs.Length > 0;
        }

        private Camera ResolveRenderCamera()
        {
            if (m_renderCamera != null) return m_renderCamera;
            if (Camera.main != null) return Camera.main;
            return Camera.current;
        }

        private void RequestConfigUpdate(bool immediate = false)
        {
            if (_destroying || !HasCargoConfigs()) return;
            _updateScheduler.Request(m_updateIntervalFrames, immediate, ExecuteConfigUpdate);
        }

        private void TryExecuteScheduledConfigUpdate()
        {
            _updateScheduler.TryExecuteScheduled(m_updateIntervalFrames, ExecuteConfigUpdate);
        }

        private void ExecuteConfigUpdate()
        {
            _updateScheduler.NotifyExecuted(m_updateIntervalFrames);
            for (int i = 0; i < _cargoConfigs.Length; i++)
            {
                _cargoConfigs[i]?.UpdateParts().Forget();
            }
        }

        private void LogPerfIfNeeded()
        {
            if (!m_logWarehousePerf || !HasCargoConfigs()) return;
            float now = Time.unscaledTime;
            if (now < _nextPerfLogTime) return;

            int totalChunks = 0;
            int updatedChunks = 0;
            int updateTasks = 0;
            for (int i = 0; i < _cargoConfigs.Length; i++)
            {
                CargoConfig config = _cargoConfigs[i];
                if (config == null) continue;
                totalChunks += config.ChunkCount;
                updatedChunks += config.LastUpdatedChunkCount;
                updateTasks += config.LastUpdateTaskCount;
            }

            int dispatchedUpdateCount = _updateScheduler.ConsumeDispatchedCount();
            Debug.Log(
                $"[Warehouse][Perf] intervalFrames={Mathf.Max(1, m_updateIntervalFrames)}, dispatchedUpdates={dispatchedUpdateCount}/s, updatedChunks={updatedChunks}, totalChunks={totalChunks}, updateTasks={updateTasks}, pending={_updateScheduler.HasPendingUpdate}");
            _nextPerfLogTime = now + 1f;
        }
    }
}
