using UnityEngine;
#if UNITY_EDITOR
using System;
using System.IO;
using NonsensicalKit.Core;
using NonsensicalKit.DigitalTwin.Warehouse;

/// <summary>
/// WarehouseManager 外挂：运行时直观调货位布局并写回 .dat。
/// 仅 Editor 编译；调完删掉本组件即可，不进正式流程。
/// 世界坐标 = Origin + 排标量×排方向 + 层标量×层方向 + 列标量×列方向；
/// 深度 Depth 是索引维度（堆垛机等据此判断叉子伸出距离），不参与世界坐标计算——
/// 同排/层/列下各 Depth 共用同一基准点。深度方向仅写入 .dat 供设备使用。
/// 专注模式：
/// - 切片：只显示某一层/列/排；
/// - 整轴：固定另外两轴（如层专注固定某一排×某一列），显示目标轴全部档位，便于一次配完。
/// 轴操作：翻转索引（坐标集合不变）、单轴标量平移、原点世界平移。
/// 禁用规则：按排/列/层/深范围禁用结构空洞货位。
/// </summary>
[DisallowMultipleComponent]
public class WarehouseManagerLayoutEditor : MonoBehaviour
{
    public enum DepthDirectionMode
    {
        [InspectorName("详细配置")]
        Detailed = 0,

        [InspectorName("奇偶模式")]
        OddEven = 1,
    }

    public enum FocusAxisMode
    {
        [InspectorName("关闭")]
        Off = 0,

        [InspectorName("层 (Level)")]
        Layer = 1,

        [InspectorName("列 (Column)")]
        Column = 2,

        [InspectorName("排 (Row)")]
        Row = 3,
    }

    public enum FocusStyle
    {
        [InspectorName("切片（只看一档）")]
        Slice = 0,

        [InspectorName("整轴（配全部档）")]
        AxisStack = 1,
    }

    public enum AxisOpTarget
    {
        [InspectorName("排 (Row)")]
        Row = 0,

        [InspectorName("层 (Layer)")]
        Layer = 1,

        [InspectorName("列 (Column)")]
        Column = 2,
    }

    private const float AutoApplyDebounceSeconds = 0.05f;
    private const int MaxGizmoCells = 2000;

    [Header("目标")]
    [SerializeField]
    private WarehouseManager m_warehouseManager;

    [SerializeField, Tooltip("对应 StreamingAssets/Warehouse/{name}.dat（仅文件名，不含路径）")]
    private string m_warehouseFileName = "StackerWarehouse";

    [Header("轴坐标（与货架对齐）")]
    [SerializeField, Tooltip("布局原点。世界坐标 = Origin + 各轴标量 × 对应方向。")]
    private Vector3 m_layoutOrigin;

    [SerializeField, Tooltip("排方向（世界空间，不强制归一化；负向量即反向）。")]
    private Vector3 m_rowDirection = Vector3.right;

    [SerializeField, Tooltip("层方向（世界空间，不强制归一化；负向量即反向）。")]
    private Vector3 m_layerDirection = Vector3.up;

    [SerializeField, Tooltip("列方向（世界空间，不强制归一化；负向量即反向）。")]
    private Vector3 m_columnDirection = Vector3.forward;

    [SerializeField, Tooltip("沿排方向的标量档位。")]
    private float[] m_posRow = Array.Empty<float>();

    [SerializeField, Tooltip("沿层方向的标量档位。")]
    private float[] m_posLayer = Array.Empty<float>();

    [SerializeField, Tooltip("沿列方向的标量档位。")]
    private float[] m_posColumn = Array.Empty<float>();

    [SerializeField, Min(1), Tooltip("深度档位数（索引维度）。不改变货位世界坐标，供堆垛机等设备使用。")]
    private int m_depthCount = 1;

    [SerializeField, Tooltip("详细配置：每列单独方向；奇偶模式：偶数列/奇数列各共用一个方向。仅写入 .dat，不参与坐标计算。")]
    private DepthDirectionMode m_depthDirectionMode = DepthDirectionMode.Detailed;

    [SerializeField, Tooltip("每列一个 Vector3：该列叉伸/取货深度方向（设备用）。长度应与列数一致。不参与货位世界坐标。")]
    private Vector3[] m_depthDirection = Array.Empty<Vector3>();

    [SerializeField, Tooltip("Column 索引为偶数（0,2,4…）时共用的深度方向（设备用，不参与坐标）。")]
    private Vector3 m_evenColumnDepthDirection;

    [SerializeField, Tooltip("Column 索引为奇数（1,3,5…）时共用的深度方向（设备用，不参与坐标）。")]
    private Vector3 m_oddColumnDepthDirection;

    [Header("运行时刷新")]
    [SerializeField]
    private bool m_autoApplyAtRuntime = true;

    [SerializeField, Tooltip("输出每次 Apply / 专注刷新的详细日志（拖参时建议关闭）。")]
    private bool m_verboseLog;

    [Header("专注模式")]
    [SerializeField, Tooltip("关闭=全显。层/列/排配合下方「专注样式」使用。")]
    private FocusAxisMode m_focusMode = FocusAxisMode.Off;

    [SerializeField, Tooltip("切片：只显示某一档。整轴：固定另外两轴，显示目标轴全部档（如层专注=某一排×某一列上的所有层）。")]
    private FocusStyle m_focusStyle = FocusStyle.AxisStack;

    [SerializeField, Min(0), Tooltip("切片样式下生效：只显示该索引的层/列/排。")]
    private int m_focusIndex;

    [SerializeField, Min(0), Tooltip("整轴样式下：层/列专注时固定这一排。")]
    private int m_focusPinRow;

    [SerializeField, Min(0), Tooltip("整轴样式下：层/排专注时固定这一列。")]
    private int m_focusPinColumn;

    [SerializeField, Min(0), Tooltip("整轴样式下：列/排专注时固定这一层。")]
    private int m_focusPinLayer;

    [SerializeField, Tooltip("开启后 Scene 预览盒与货物显隐范围一致。货位过多时会在初始化时自动关闭「绘制货位预览」，仍可手动开启。")]
    private bool m_focusGizmosOnly = true;

    [Header("禁用货位")]
    [SerializeField, Tooltip("满足规则的货位结构禁用：不显示，且业务无法再打开。多条规则为「或」。")]
    private WarehouseSlotDisableRule[] m_disableRules = Array.Empty<WarehouseSlotDisableRule>();

    [Header("均匀生成辅助")]
    [SerializeField, Tooltip("生成时写入布局原点。")]
    private Vector3 m_uniformOrigin;

    [SerializeField, Tooltip("各轴标量间距（排/层/列）。可为负以实现反向。")]
    private Vector3 m_uniformSpacing = Vector3.one;

    [SerializeField]
    private Vector3Int m_uniformCount = new Vector3Int(1, 1, 1);

    [SerializeField, Tooltip("生成轴或补齐列深度方向时使用（详细配置模式；仅设备元数据）")]
    private Vector3 m_defaultDepthDirection;

    public int RowCount => m_posRow?.Length ?? 0;
    public int LayerCount => m_posLayer?.Length ?? 0;
    public int ColumnCount => m_posColumn?.Length ?? 0;
    public int DepthAxisCount => Mathf.Max(1, m_depthCount);

    public FocusAxisMode FocusMode => m_focusMode;

    public bool IsDetailedDepthMode => m_depthDirectionMode == DepthDirectionMode.Detailed;

    public bool IsOddEvenDepthMode => m_depthDirectionMode == DepthDirectionMode.OddEven;

    public bool IsFocusModeOn => m_focusMode != FocusAxisMode.Off;

    public bool IsFocusSlice => IsFocusModeOn && m_focusStyle == FocusStyle.Slice;

    public bool IsFocusAxisStack => IsFocusModeOn && m_focusStyle == FocusStyle.AxisStack;

    public bool ShowFocusPinRow =>
        IsFocusAxisStack && (m_focusMode == FocusAxisMode.Layer || m_focusMode == FocusAxisMode.Column);

    public bool ShowFocusPinColumn =>
        IsFocusAxisStack && (m_focusMode == FocusAxisMode.Layer || m_focusMode == FocusAxisMode.Row);

    public bool ShowFocusPinLayer =>
        IsFocusAxisStack && (m_focusMode == FocusAxisMode.Column || m_focusMode == FocusAxisMode.Row);

    [Header("轴操作")]
    [SerializeField, Tooltip("翻转 / 单轴平移作用于此轴。")]
    private AxisOpTarget m_axisOpTarget = AxisOpTarget.Layer;

    [SerializeField, Tooltip("对该轴所有标量统一加上此值。")]
    private float m_axisTranslateDelta;

    [SerializeField, Tooltip("将布局原点按世界 XYZ 平移。")]
    private Vector3 m_offset;

    [Header("Gizmo")]
    [SerializeField, Tooltip("货位超过建议上限时会在启用/加载时自动关闭，之后可手动重新勾选。")]
    private bool m_drawGizmos = true;

    [SerializeField]
    private Vector3 m_gizmoSize = new Vector3(0.8f, 0.4f, 0.8f);

    [SerializeField]
    private Color m_gizmoColor = new Color(0.2f, 0.85f, 1f, 0.35f);

    private bool _layoutDirty;
    private bool _visibilityDirty;
    private float _applyAfterUnscaledTime;
    private bool _applying;
    private bool _loggedSizeMismatch;
    private string _lastAppliedSignature;
    private string _lastVisibilitySignature;
    /// <summary>上次已处理过自动关 Gizmo 的货位规模；同规模下不再强关，便于手动重开。</summary>
    private long _lastAutoDisableGizmoCellCount = -1;

    private Transform WarehouseTransform =>
        m_warehouseManager != null ? m_warehouseManager.transform : transform;

    private string DatRelativePath => $"Warehouse/{m_warehouseFileName}.dat";

    private string DatFullPath =>
        Path.Combine(Application.streamingAssetsPath, "Warehouse", $"{m_warehouseFileName}.dat");

    private void Reset()
    {
        m_warehouseManager = GetComponent<WarehouseManager>();
        if (m_warehouseManager == null)
        {
            m_warehouseManager = GetComponentInParent<WarehouseManager>();
        }

        ApplyDefaultAxisDirections();
    }

    private void OnEnable()
    {
        EnsureAxisDirectionsValid();
        TryAutoDisableGizmosIfOversize();
    }

    private void OnValidate()
    {
        EnsureAxisDirectionsValid();

        if (m_depthCount < 1)
        {
            m_depthCount = 1;
        }

        if (IsDetailedDepthMode)
        {
            EnsureDepthDirectionLength();
        }

        ClampFocusIndices();

        if (!Application.isPlaying || !m_autoApplyAtRuntime || _applying)
        {
            return;
        }

        // 不在 OnValidate 里直接 Apply，避免拖参时同步重建；交给 LateUpdate 防抖。
        MarkLayoutDirty();
        MarkVisibilityDirty();
    }

    private void EnsureAxisDirectionsValid()
    {
        // 旧组件升级后新字段默认为零，会导致坐标塌缩；自动回落到世界正轴。
        if (m_rowDirection == Vector3.zero)
        {
            m_rowDirection = Vector3.right;
        }

        if (m_layerDirection == Vector3.zero)
        {
            m_layerDirection = Vector3.up;
        }

        if (m_columnDirection == Vector3.zero)
        {
            m_columnDirection = Vector3.forward;
        }
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying || !m_autoApplyAtRuntime || _applying)
        {
            return;
        }

        if (!_layoutDirty && !_visibilityDirty)
        {
            return;
        }

        if (Time.unscaledTime < _applyAfterUnscaledTime)
        {
            return;
        }

        if (_layoutDirty)
        {
            // OnValidate 可能连专注一起标脏；轴未变则跳过位置刷新，只走显隐。
            string layoutSignature = BuildLayoutSignature();
            if (layoutSignature == _lastAppliedSignature)
            {
                ClearLayoutDirty();
            }
            else
            {
                ApplyToRuntime();
                return;
            }
        }

        if (_visibilityDirty)
        {
            ApplyEditorVisibility(force: true);
        }
    }

    private void OnDestroy()
    {
        if (!Application.isPlaying || m_warehouseManager == null || !m_warehouseManager.Inited)
        {
            return;
        }

        if (m_focusMode == FocusAxisMode.Off)
        {
            return;
        }

        // 组件被删时按当前禁用规则恢复显隐，避免专注态残留。
        m_focusMode = FocusAxisMode.Off;
        ApplyEditorVisibility(force: true);
    }

    public void LoadFromFile()
    {
        if (!TryValidateFileName(out string fileError))
        {
            Debug.LogError($"[WarehouseLayoutEditor] {fileError}", this);
            return;
        }

        if (!File.Exists(DatFullPath))
        {
            Debug.LogError($"[WarehouseLayoutEditor] 文件不存在: {DatFullPath}", this);
            return;
        }

        try
        {
            WarehouseData data = BinDataIO.LoadSync(DatFullPath);

            if (data.LayoutAxisConfig != null)
            {
                ApplyLayoutAxisConfig(data.LayoutAxisConfig);
            }
            else
            {
                ApplyDefaultAxisDirections();
                m_layoutOrigin = Vector3.zero;
            }

            if (!TryExtractAxes(
                    data.Bins,
                    m_layoutOrigin,
                    m_rowDirection,
                    m_layerDirection,
                    m_columnDirection,
                    out float[] rows,
                    out float[] layers,
                    out float[] columns,
                    out Vector3[] depthDirections,
                    out int depthCount))
            {
                Debug.LogError("[WarehouseLayoutEditor] 无法从文件解析轴坐标（需要规则网格）。", this);
                return;
            }

            m_posRow = rows;
            m_posLayer = layers;
            m_posColumn = columns;
            m_depthCount = depthCount;

            if (data.LayoutDepthConfig != null)
            {
                ApplyLayoutDepthConfig(data.LayoutDepthConfig);
            }
            else
            {
                m_depthDirection = depthDirections;
                ApplyLoadedDepthDirections(depthDirections);
            }

            m_disableRules = data.SlotDisableRules != null
                ? CloneDisableRules(data.SlotDisableRules)
                : Array.Empty<WarehouseSlotDisableRule>();

            _lastAppliedSignature = null;
            _lastVisibilitySignature = null;
            MarkLayoutDirty();
            MarkVisibilityDirty();
            TryAutoDisableGizmosIfOversize();

            int disableCount = CountActiveDisableRules();
            Debug.Log(
                $"[WarehouseLayoutEditor] 已加载 {DatRelativePath}：排{rows.Length} 层{layers.Length} 列{columns.Length} 深{depthCount}，模式={m_depthDirectionMode}" +
                (data.LayoutAxisConfig != null ? "，轴配置来自文件" : "，轴配置用默认正轴") +
                (data.LayoutDepthConfig != null ? "，深度配置来自文件" : "，无扩展深度配置") +
                $"，禁用规则 {disableCount} 条",
                this);

            if (Application.isPlaying && m_autoApplyAtRuntime)
            {
                ApplyToRuntime();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[WarehouseLayoutEditor] 加载失败: {e}", this);
        }
    }

    public void ApplyToRuntime()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[WarehouseLayoutEditor] 仅在运行时可刷新货物位置。可先保存 .dat，再进 Play。", this);
            return;
        }

        if (m_warehouseManager == null)
        {
            Debug.LogError("[WarehouseLayoutEditor] 未绑定 WarehouseManager。", this);
            ClearLayoutDirty();
            return;
        }

        if (!m_warehouseManager.Inited)
        {
            Debug.LogWarning("[WarehouseLayoutEditor] WarehouseManager 尚未初始化完成，稍后再 Apply。", this);
            // 保持 dirty，等 Inited 后 LateUpdate 再试。
            ScheduleApplyDebounce(0.1f);
            return;
        }

        if (!TryBuildBins(out BinData[] bins))
        {
            ClearLayoutDirty();
            return;
        }

        if (!TryMatchRuntimeDimensions(out string dimensionError))
        {
            if (!_loggedSizeMismatch)
            {
                Debug.LogError($"[WarehouseLayoutEditor] {dimensionError}", this);
                _loggedSizeMismatch = true;
            }

            // 记入当前签名，避免同尺寸失败时反复 TryBuildBins；改轴后签名变化会再试。
            _lastAppliedSignature = BuildLayoutSignature();
            ClearLayoutDirty();
            ClearVisibilityDirty();
            return;
        }

        _loggedSizeMismatch = false;

        Int4[] locations = new Int4[bins.Length];
        Vector3[] positions = new Vector3[bins.Length];
        for (int i = 0; i < bins.Length; i++)
        {
            BinData bin = bins[i];
            locations[i] = new Int4(bin.Level, bin.Column, bin.Row, bin.Depth);
            positions[i] = new Vector3(bin.PosX, bin.PosY, bin.PosZ);
        }

        _applying = true;
        try
        {
            m_warehouseManager.SetCargoState(locations, positions, true);
            _lastAppliedSignature = BuildLayoutSignature();
            ClearLayoutDirty();
            // 位置刷新不改 ShowCargo；补一次专注+禁用显隐。
            ApplyEditorVisibility(force: true);
            LogVerbose($"[WarehouseLayoutEditor] 已刷新 {bins.Length} 个货位位置。");
        }
        finally
        {
            _applying = false;
        }
    }

    public void SaveToFile()
    {
        if (!TryValidateFileName(out string fileError))
        {
            Debug.LogError($"[WarehouseLayoutEditor] {fileError}", this);
            return;
        }

        if (!TryBuildBins(out BinData[] bins))
        {
            return;
        }

        try
        {
            string directory = Path.GetDirectoryName(DatFullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            WarehouseLayoutDepthConfig depthConfig = BuildLayoutDepthConfig();
            WarehouseLayoutAxisConfig axisConfig = BuildLayoutAxisConfig();
            BinDataIO.SaveSync(bins, DatFullPath, depthConfig, m_disableRules, axisConfig);
            Debug.Log(
                $"[WarehouseLayoutEditor] 已保存 {bins.Length} 个货位 + 轴配置 + 深度配置({m_depthDirectionMode}) + 禁用规则{CountActiveDisableRules()}条 → {DatFullPath}",
                this);
        }
        catch (Exception e)
        {
            Debug.LogError($"[WarehouseLayoutEditor] 保存失败: {e}", this);
        }
    }

    public void GenerateUniformAxes()
    {
        int rowCount = Mathf.Max(1, m_uniformCount.x);
        int layerCount = Mathf.Max(1, m_uniformCount.y);
        int columnCount = Mathf.Max(1, m_uniformCount.z);

        m_layoutOrigin = m_uniformOrigin;
        m_posRow = new float[rowCount];
        m_posLayer = new float[layerCount];
        m_posColumn = new float[columnCount];

        for (int i = 0; i < rowCount; i++)
        {
            m_posRow[i] = i * m_uniformSpacing.x;
        }

        for (int i = 0; i < layerCount; i++)
        {
            m_posLayer[i] = i * m_uniformSpacing.y;
        }

        for (int i = 0; i < columnCount; i++)
        {
            m_posColumn[i] = i * m_uniformSpacing.z;
        }

        if (IsDetailedDepthMode)
        {
            m_depthDirection = new Vector3[columnCount];
            for (int i = 0; i < columnCount; i++)
            {
                m_depthDirection[i] = m_defaultDepthDirection;
            }
        }

        MarkLayoutDirty();
        TryAutoDisableGizmosIfOversize();
        Debug.Log(
            $"[WarehouseLayoutEditor] 已生成均匀轴：排{rowCount} 层{layerCount} 列{columnCount}，原点={m_layoutOrigin}，深度模式={m_depthDirectionMode}",
            this);

        if (Application.isPlaying && m_autoApplyAtRuntime)
        {
            ApplyToRuntime();
        }
    }

    public void FlipSelectedAxis()
    {
        bool flipped;
        switch (m_axisOpTarget)
        {
            case AxisOpTarget.Row:
                flipped = TryReverseArray(m_posRow);
                if (flipped)
                {
                    RemapDisableRulesAfterAxisFlip(AxisOpTarget.Row, m_posRow.Length);
                    RemapIndexAfterFlip(ref m_focusPinRow, m_posRow.Length);
                    if (m_focusMode == FocusAxisMode.Row && m_focusStyle == FocusStyle.Slice)
                    {
                        RemapIndexAfterFlip(ref m_focusIndex, m_posRow.Length);
                    }
                }

                break;

            case AxisOpTarget.Layer:
                flipped = TryReverseArray(m_posLayer);
                if (flipped)
                {
                    RemapDisableRulesAfterAxisFlip(AxisOpTarget.Layer, m_posLayer.Length);
                    RemapIndexAfterFlip(ref m_focusPinLayer, m_posLayer.Length);
                    if (m_focusMode == FocusAxisMode.Layer && m_focusStyle == FocusStyle.Slice)
                    {
                        RemapIndexAfterFlip(ref m_focusIndex, m_posLayer.Length);
                    }
                }

                break;

            case AxisOpTarget.Column:
                flipped = TryReverseArray(m_posColumn);
                if (flipped)
                {
                    RemapDisableRulesAfterAxisFlip(AxisOpTarget.Column, m_posColumn.Length);
                    FlipColumnDepthDirections();
                    RemapIndexAfterFlip(ref m_focusPinColumn, m_posColumn.Length);
                    if (m_focusMode == FocusAxisMode.Column && m_focusStyle == FocusStyle.Slice)
                    {
                        RemapIndexAfterFlip(ref m_focusIndex, m_posColumn.Length);
                    }
                }

                break;

            default:
                flipped = false;
                break;
        }

        if (!flipped)
        {
            Debug.LogWarning($"[WarehouseLayoutEditor] {AxisOpLabel()} 无法翻转（需要至少 2 个坐标）。", this);
            return;
        }

        MarkLayoutDirty();
        MarkVisibilityDirty();
        Debug.Log(
            $"[WarehouseLayoutEditor] 已翻转 {AxisOpLabel()} 索引（坐标集合不变，仅索引对调；专注探针已同步重映射）。",
            this);

        if (Application.isPlaying && m_autoApplyAtRuntime)
        {
            ApplyToRuntime();
        }
    }

    public void TranslateSelectedAxis()
    {
        if (Mathf.Approximately(m_axisTranslateDelta, 0f))
        {
            Debug.LogWarning("[WarehouseLayoutEditor] 单轴平移量为 0，已跳过。", this);
            return;
        }

        float[] target = m_axisOpTarget switch
        {
            AxisOpTarget.Row => m_posRow,
            AxisOpTarget.Layer => m_posLayer,
            AxisOpTarget.Column => m_posColumn,
            _ => null,
        };

        if (target == null || target.Length == 0)
        {
            Debug.LogError($"[WarehouseLayoutEditor] {AxisOpLabel()} 轴为空，无法平移。", this);
            return;
        }

        OffsetArray(target, m_axisTranslateDelta);
        MarkLayoutDirty();
        Debug.Log(
            $"[WarehouseLayoutEditor] {AxisOpLabel()} 已整体平移 {m_axisTranslateDelta}",
            this);

        if (Application.isPlaying && m_autoApplyAtRuntime)
        {
            ApplyToRuntime();
        }
    }

    public void ApplyOffsetToAxes()
    {
        if (m_offset == Vector3.zero)
        {
            Debug.LogWarning("[WarehouseLayoutEditor] 原点平移量为 0，已跳过。", this);
            return;
        }

        m_layoutOrigin += m_offset;
        MarkLayoutDirty();

        Debug.Log($"[WarehouseLayoutEditor] 布局原点已平移 {m_offset} → {m_layoutOrigin}", this);

        if (Application.isPlaying && m_autoApplyAtRuntime)
        {
            ApplyToRuntime();
        }
    }

    public void FillDepthDirectionsWithDefault()
    {
        EnsureDepthDirectionLength();
        for (int i = 0; i < m_depthDirection.Length; i++)
        {
            m_depthDirection[i] = m_defaultDepthDirection;
        }

        // 深度方向不参与坐标计算，无需刷新货位位置。
        Debug.Log(
            $"[WarehouseLayoutEditor] 已将 {m_depthDirection.Length} 列深度方向设为 {m_defaultDepthDirection}（仅元数据，坐标不变）",
            this);
    }

    public void FocusPrevious()
    {
        int count = GetSliceAxisCount();
        if (count <= 0)
        {
            return;
        }

        m_focusIndex = (m_focusIndex - 1 + count) % count;
        ApplyEditorVisibility(force: true);
    }

    public void FocusNext()
    {
        int count = GetSliceAxisCount();
        if (count <= 0)
        {
            return;
        }

        m_focusIndex = (m_focusIndex + 1) % count;
        ApplyEditorVisibility(force: true);
    }

    public void CycleFocusPinRow()
    {
        int count = m_posRow?.Length ?? 0;
        if (count <= 0)
        {
            return;
        }

        m_focusPinRow = (m_focusPinRow + 1) % count;
        ApplyEditorVisibility(force: true);
    }

    public void CycleFocusPinColumn()
    {
        int count = m_posColumn?.Length ?? 0;
        if (count <= 0)
        {
            return;
        }

        m_focusPinColumn = (m_focusPinColumn + 1) % count;
        ApplyEditorVisibility(force: true);
    }

    public void CycleFocusPinLayer()
    {
        int count = m_posLayer?.Length ?? 0;
        if (count <= 0)
        {
            return;
        }

        m_focusPinLayer = (m_focusPinLayer + 1) % count;
        ApplyEditorVisibility(force: true);
    }

    /// <summary>
    /// 保存 .dat 并退出专注显隐。组件删除由 Inspector 用 Undo.DestroyObjectImmediate 处理。
    /// </summary>
    public void SaveBeforeRemove()
    {
        SaveToFile();
        if (Application.isPlaying && m_warehouseManager != null && m_warehouseManager.Inited)
        {
            m_focusMode = FocusAxisMode.Off;
            ApplyEditorVisibility(force: true);
        }
    }

    public void SaveAndRemoveSelf()
    {
        SaveBeforeRemove();
        if (Application.isPlaying)
        {
            Destroy(this);
        }
        else
        {
            DestroyImmediate(this);
        }
    }

    public void ApplyFocusNow()
    {
        ApplyEditorVisibility(force: true);
    }

    public void ApplyDisableRulesNow()
    {
        ApplyEditorVisibility(force: true);
    }

    private void MarkLayoutDirty()
    {
        _layoutDirty = true;
        _loggedSizeMismatch = false;
        ScheduleApplyDebounce();
    }

    private void MarkVisibilityDirty()
    {
        _visibilityDirty = true;
        ScheduleApplyDebounce();
    }

    private void ClearLayoutDirty()
    {
        _layoutDirty = false;
    }

    private void ClearVisibilityDirty()
    {
        _visibilityDirty = false;
    }

    private void ScheduleApplyDebounce(float seconds = AutoApplyDebounceSeconds)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        float due = Time.unscaledTime + seconds;
        if (due > _applyAfterUnscaledTime)
        {
            _applyAfterUnscaledTime = due;
        }
    }

    private void ApplyEditorVisibility(bool force = false)
    {
        if (!Application.isPlaying)
        {
            ClearVisibilityDirty();
            return;
        }

        if (m_warehouseManager == null || !m_warehouseManager.Inited)
        {
            return;
        }

        if (!force && !_visibilityDirty)
        {
            return;
        }

        ClampFocusIndices();

        string visibilitySignature = BuildVisibilitySignature();
        if (!force && visibilitySignature == _lastVisibilitySignature)
        {
            ClearVisibilityDirty();
            return;
        }

        if (!TryMatchRuntimeDimensions(out _))
        {
            // 尺寸不一致时无法安全改显隐；布局 Apply 会负责提示。
            ClearVisibilityDirty();
            return;
        }

        if (!TryBuildBins(out BinData[] bins))
        {
            ClearVisibilityDirty();
            return;
        }

        Int4[] locations = new Int4[bins.Length];
        bool[] slotEnabled = new bool[bins.Length];
        bool[] shows = new bool[bins.Length];
        int visibleCount = 0;
        int disabledCount = 0;
        for (int i = 0; i < bins.Length; i++)
        {
            BinData bin = bins[i];
            locations[i] = new Int4(bin.Level, bin.Column, bin.Row, bin.Depth);
            bool enabled = !IsSlotDisabled(bin.Level, bin.Column, bin.Row, bin.Depth);
            bool focused = m_focusMode == FocusAxisMode.Off || IsFocusedBin(bin.Level, bin.Column, bin.Row);
            slotEnabled[i] = enabled;
            shows[i] = enabled && focused;
            if (!enabled)
            {
                disabledCount++;
            }

            if (shows[i])
            {
                visibleCount++;
            }
        }

        m_warehouseManager.ApplyStructuralAndShowStates(locations, slotEnabled, shows, true);
        _lastVisibilitySignature = visibilitySignature;
        ClearVisibilityDirty();

        if (m_focusMode == FocusAxisMode.Off)
        {
            if (disabledCount > 0)
            {
                LogVerbose(
                    $"[WarehouseLayoutEditor] 显隐已更新：禁用 {disabledCount}，显示 {visibleCount}/{bins.Length}。");
            }

            return;
        }

        LogVerbose(
            $"[WarehouseLayoutEditor] {DescribeFocus()}：禁用 {disabledCount}，显示 {visibleCount}/{bins.Length}。");
    }

    private bool TryMatchRuntimeDimensions(out string error)
    {
        error = null;
        if (m_warehouseManager == null || !m_warehouseManager.Inited)
        {
            error = "WarehouseManager 未就绪。";
            return false;
        }

        Int4 runtime = m_warehouseManager.RuntimeBinSize;
        int layers = m_posLayer?.Length ?? 0;
        int columns = m_posColumn?.Length ?? 0;
        int rows = m_posRow?.Length ?? 0;
        int depths = Mathf.Max(1, m_depthCount);

        // RuntimeBinSize：X=层，Y=列，Z=排，W=深
        if (runtime.X == layers && runtime.Y == columns && runtime.Z == rows && runtime.W == depths)
        {
            return true;
        }

        error =
            $"轴数量与当前已加载仓库不一致（编辑器 层{layers}/列{columns}/排{rows}/深{depths}，运行时 层{runtime.X}/列{runtime.Y}/排{runtime.Z}/深{runtime.W}）。" +
            "无法热刷新（含缩小轴）。请先「保存到 StreamingAssets」，退出 Play 再进以重载尺寸。";
        return false;
    }

    private bool TryValidateFileName(out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(m_warehouseFileName))
        {
            error = "仓库文件名为空。";
            return false;
        }

        string name = m_warehouseFileName.Trim();
        if (name != m_warehouseFileName)
        {
            m_warehouseFileName = name;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains("/") ||
            name.Contains("\\") ||
            name.Contains(".."))
        {
            error = $"仓库文件名非法（仅允许文件名，禁止路径）：「{m_warehouseFileName}」";
            return false;
        }

        return true;
    }

    private bool IsSlotDisabled(int level, int column, int row, int depth)
    {
        return WarehouseSlotDisableRule.MatchesAny(m_disableRules, level, column, row, depth);
    }

    private bool IsFocusedBin(int level, int column, int row)
    {
        if (m_focusStyle == FocusStyle.Slice)
        {
            return m_focusMode switch
            {
                FocusAxisMode.Layer => level == m_focusIndex,
                FocusAxisMode.Column => column == m_focusIndex,
                FocusAxisMode.Row => row == m_focusIndex,
                _ => true,
            };
        }

        // 整轴：固定另外两轴，展开目标轴全部档位。
        return m_focusMode switch
        {
            FocusAxisMode.Layer => row == m_focusPinRow && column == m_focusPinColumn,
            FocusAxisMode.Column => row == m_focusPinRow && level == m_focusPinLayer,
            FocusAxisMode.Row => column == m_focusPinColumn && level == m_focusPinLayer,
            _ => true,
        };
    }

    private int GetSliceAxisCount()
    {
        return m_focusMode switch
        {
            FocusAxisMode.Layer => m_posLayer?.Length ?? 0,
            FocusAxisMode.Column => m_posColumn?.Length ?? 0,
            FocusAxisMode.Row => m_posRow?.Length ?? 0,
            _ => 0,
        };
    }

    private void ClampFocusIndices()
    {
        m_focusIndex = ClampIndex(m_focusIndex, GetSliceAxisCount());
        m_focusPinRow = ClampIndex(m_focusPinRow, m_posRow?.Length ?? 0);
        m_focusPinColumn = ClampIndex(m_focusPinColumn, m_posColumn?.Length ?? 0);
        m_focusPinLayer = ClampIndex(m_focusPinLayer, m_posLayer?.Length ?? 0);
    }

    private static int ClampIndex(int index, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        return Mathf.Clamp(index, 0, count - 1);
    }

    public string DescribeFocus()
    {
        if (m_focusMode == FocusAxisMode.Off)
        {
            return "专注关闭";
        }

        if (m_focusStyle == FocusStyle.Slice)
        {
            return $"切片-{FocusAxisLabel()}[{m_focusIndex}]";
        }

        return m_focusMode switch
        {
            FocusAxisMode.Layer => $"整轴-层（排{m_focusPinRow} × 列{m_focusPinColumn}）",
            FocusAxisMode.Column => $"整轴-列（排{m_focusPinRow} × 层{m_focusPinLayer}）",
            FocusAxisMode.Row => $"整轴-排（列{m_focusPinColumn} × 层{m_focusPinLayer}）",
            _ => "专注",
        };
    }

    private string FocusAxisLabel()
    {
        return m_focusMode switch
        {
            FocusAxisMode.Layer => "层",
            FocusAxisMode.Column => "列",
            FocusAxisMode.Row => "排",
            _ => "关闭",
        };
    }

    private void LogVerbose(string message)
    {
        if (m_verboseLog)
        {
            Debug.Log(message, this);
        }
    }

    private int CountActiveDisableRules()
    {
        if (m_disableRules == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < m_disableRules.Length; i++)
        {
            if (m_disableRules[i] != null && m_disableRules[i].Enabled && m_disableRules[i].HasAnyConstraint)
            {
                count++;
            }
        }

        return count;
    }

    private static WarehouseSlotDisableRule[] CloneDisableRules(WarehouseSlotDisableRule[] source)
    {
        if (source == null || source.Length == 0)
        {
            return Array.Empty<WarehouseSlotDisableRule>();
        }

        var clone = new WarehouseSlotDisableRule[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            WarehouseSlotDisableRule src = source[i];
            if (src == null)
            {
                continue;
            }

            clone[i] = new WarehouseSlotDisableRule
            {
                Enabled = src.Enabled,
                ConstrainRow = src.ConstrainRow,
                RowMin = src.RowMin,
                RowMax = src.RowMax,
                ConstrainColumn = src.ConstrainColumn,
                ColumnMin = src.ColumnMin,
                ColumnMax = src.ColumnMax,
                ConstrainLevel = src.ConstrainLevel,
                LevelMin = src.LevelMin,
                LevelMax = src.LevelMax,
                ConstrainDepth = src.ConstrainDepth,
                DepthMin = src.DepthMin,
                DepthMax = src.DepthMax,
            };
        }

        return clone;
    }

    private void RemapDisableRulesAfterAxisFlip(AxisOpTarget axis, int count)
    {
        if (m_disableRules == null || count <= 0)
        {
            return;
        }

        for (int i = 0; i < m_disableRules.Length; i++)
        {
            WarehouseSlotDisableRule rule = m_disableRules[i];
            if (rule == null)
            {
                continue;
            }

            switch (axis)
            {
                case AxisOpTarget.Row when rule.ConstrainRow:
                    RemapIndexRange(ref rule.RowMin, ref rule.RowMax, count);
                    break;
                case AxisOpTarget.Layer when rule.ConstrainLevel:
                    RemapIndexRange(ref rule.LevelMin, ref rule.LevelMax, count);
                    break;
                case AxisOpTarget.Column when rule.ConstrainColumn:
                    RemapIndexRange(ref rule.ColumnMin, ref rule.ColumnMax, count);
                    break;
            }
        }
    }

    private static void RemapIndexRange(ref int min, ref int max, int count)
    {
        int a = count - 1 - max;
        int b = count - 1 - min;
        min = a < b ? a : b;
        max = a < b ? b : a;
    }

    private bool TryBuildBins(out BinData[] bins)
    {
        bins = null;
        if (m_posRow == null || m_posLayer == null || m_posColumn == null)
        {
            Debug.LogError("[WarehouseLayoutEditor] 轴数组为空。", this);
            return false;
        }

        if (m_posRow.Length == 0 || m_posLayer.Length == 0 || m_posColumn.Length == 0)
        {
            Debug.LogError("[WarehouseLayoutEditor] 排/层/列至少各需要 1 个坐标。", this);
            return false;
        }

        if (m_depthCount < 1)
        {
            Debug.LogError("[WarehouseLayoutEditor] Depth 数量必须 ≥ 1。", this);
            return false;
        }

        if (IsDetailedDepthMode)
        {
            EnsureDepthDirectionLength();
        }

        int totalCount = checked(m_posRow.Length * m_posColumn.Length * m_posLayer.Length * m_depthCount);
        bins = new BinData[totalCount];
        int index = 0;

        for (int row = 0; row < m_posRow.Length; row++)
        {
            for (int column = 0; column < m_posColumn.Length; column++)
            {
                for (int level = 0; level < m_posLayer.Length; level++)
                {
                    // 深度仅作索引；同排/层/列各 Depth 共用同一世界坐标。
                    Vector3 pos = ComposeWorldPosition(m_posRow[row], m_posLayer[level], m_posColumn[column]);
                    for (int depth = 0; depth < m_depthCount; depth++)
                    {
                        bins[index++] = new BinData
                        {
                            Row = row,
                            Column = column,
                            Level = level,
                            Depth = depth,
                            PosX = pos.x,
                            PosY = pos.y,
                            PosZ = pos.z,
                        };
                    }
                }
            }
        }

        return true;
    }

    private Vector3 ComposeWorldPosition(float rowScalar, float layerScalar, float columnScalar)
    {
        return m_layoutOrigin
               + m_rowDirection * rowScalar
               + m_layerDirection * layerScalar
               + m_columnDirection * columnScalar;
    }

    private static bool TryExtractAxes(
        BinData[] bins,
        Vector3 origin,
        Vector3 rowDirection,
        Vector3 layerDirection,
        Vector3 columnDirection,
        out float[] rows,
        out float[] layers,
        out float[] columns,
        out Vector3[] depthDirections,
        out int depthCount)
    {
        rows = null;
        layers = null;
        columns = null;
        depthDirections = null;
        depthCount = 1;

        if (bins == null || bins.Length == 0)
        {
            return false;
        }

        Int4 dimensions = BinDataIO.InferDimensions(bins);
        int layerCount = dimensions.X;
        int columnCount = dimensions.Y;
        int rowCount = dimensions.Z;
        depthCount = Mathf.Max(1, dimensions.W);

        rows = new float[rowCount];
        layers = new float[layerCount];
        columns = new float[columnCount];
        depthDirections = new Vector3[columnCount];

        bool[] rowFilled = new bool[rowCount];
        bool[] layerFilled = new bool[layerCount];
        bool[] columnFilled = new bool[columnCount];

        // 深度不参与坐标，各 Depth 位置相同；按轴方向把世界坐标反解为标量。
        for (int i = 0; i < bins.Length; i++)
        {
            BinData bin = bins[i];
            if (!TryDecomposeWorldPosition(
                    new Vector3(bin.PosX, bin.PosY, bin.PosZ),
                    origin,
                    rowDirection,
                    layerDirection,
                    columnDirection,
                    out float rowScalar,
                    out float layerScalar,
                    out float columnScalar))
            {
                return false;
            }

            if ((uint)bin.Row < (uint)rowCount && !rowFilled[bin.Row])
            {
                rows[bin.Row] = rowScalar;
                rowFilled[bin.Row] = true;
            }

            if ((uint)bin.Level < (uint)layerCount && !layerFilled[bin.Level])
            {
                layers[bin.Level] = layerScalar;
                layerFilled[bin.Level] = true;
            }

            if ((uint)bin.Column < (uint)columnCount && !columnFilled[bin.Column])
            {
                columns[bin.Column] = columnScalar;
                columnFilled[bin.Column] = true;
            }
        }

        for (int i = 0; i < rowCount; i++)
        {
            if (!rowFilled[i]) return false;
        }

        for (int i = 0; i < layerCount; i++)
        {
            if (!layerFilled[i]) return false;
        }

        for (int i = 0; i < columnCount; i++)
        {
            if (!columnFilled[i]) return false;
        }

        return true;
    }

    /// <summary>
    /// 解 pos = origin + r·rowDir + l·layerDir + c·colDir（3×3 线性方程组）。
    /// </summary>
    private static bool TryDecomposeWorldPosition(
        Vector3 worldPos,
        Vector3 origin,
        Vector3 rowDirection,
        Vector3 layerDirection,
        Vector3 columnDirection,
        out float rowScalar,
        out float layerScalar,
        out float columnScalar)
    {
        rowScalar = 0f;
        layerScalar = 0f;
        columnScalar = 0f;

        Vector3 delta = worldPos - origin;
        // 列向量为三轴方向的矩阵 M，解 M · (r,l,c) = delta
        float a11 = rowDirection.x;
        float a12 = layerDirection.x;
        float a13 = columnDirection.x;
        float a21 = rowDirection.y;
        float a22 = layerDirection.y;
        float a23 = columnDirection.y;
        float a31 = rowDirection.z;
        float a32 = layerDirection.z;
        float a33 = columnDirection.z;

        float det =
            a11 * (a22 * a33 - a23 * a32) -
            a12 * (a21 * a33 - a23 * a31) +
            a13 * (a21 * a32 - a22 * a31);

        if (Mathf.Abs(det) < 1e-8f)
        {
            return false;
        }

        float invDet = 1f / det;
        rowScalar = invDet * (
            delta.x * (a22 * a33 - a23 * a32) -
            a12 * (delta.y * a33 - a23 * delta.z) +
            a13 * (delta.y * a32 - a22 * delta.z));
        layerScalar = invDet * (
            a11 * (delta.y * a33 - a23 * delta.z) -
            delta.x * (a21 * a33 - a23 * a31) +
            a13 * (a21 * delta.z - delta.y * a31));
        columnScalar = invDet * (
            a11 * (a22 * delta.z - delta.y * a32) -
            a12 * (a21 * delta.z - delta.y * a31) +
            delta.x * (a21 * a32 - a22 * a31));
        return true;
    }

    private WarehouseLayoutDepthConfig BuildLayoutDepthConfig()
    {
        if (IsDetailedDepthMode)
        {
            EnsureDepthDirectionLength();
        }

        return new WarehouseLayoutDepthConfig(
            IsOddEvenDepthMode
                ? WarehouseDepthDirectionMode.OddEven
                : WarehouseDepthDirectionMode.Detailed,
            m_evenColumnDepthDirection,
            m_oddColumnDepthDirection,
            m_depthDirection ?? Array.Empty<Vector3>());
    }

    private WarehouseLayoutAxisConfig BuildLayoutAxisConfig()
    {
        return new WarehouseLayoutAxisConfig(
            m_layoutOrigin,
            m_rowDirection,
            m_layerDirection,
            m_columnDirection);
    }

    private void ApplyLayoutAxisConfig(WarehouseLayoutAxisConfig config)
    {
        if (config == null)
        {
            ApplyDefaultAxisDirections();
            m_layoutOrigin = Vector3.zero;
            return;
        }

        m_layoutOrigin = config.Origin;
        m_rowDirection = config.RowDirection;
        m_layerDirection = config.LayerDirection;
        m_columnDirection = config.ColumnDirection;
    }

    private void ApplyDefaultAxisDirections()
    {
        m_rowDirection = Vector3.right;
        m_layerDirection = Vector3.up;
        m_columnDirection = Vector3.forward;
    }

    private void ApplyLayoutDepthConfig(WarehouseLayoutDepthConfig config)
    {
        if (config == null)
        {
            return;
        }

        m_depthDirectionMode = config.Mode == WarehouseDepthDirectionMode.OddEven
            ? DepthDirectionMode.OddEven
            : DepthDirectionMode.Detailed;
        m_evenColumnDepthDirection = config.EvenColumnDepthDirection;
        m_oddColumnDepthDirection = config.OddColumnDepthDirection;
        m_depthDirection = config.ColumnDepthDirections != null
            ? (Vector3[])config.ColumnDepthDirections.Clone()
            : Array.Empty<Vector3>();

        if (IsDetailedDepthMode)
        {
            EnsureDepthDirectionLength();
        }
    }

    private void ApplyLoadedDepthDirections(Vector3[] depthDirections)
    {
        if (TryInferOddEvenDirections(depthDirections, out Vector3 evenDir, out Vector3 oddDir))
        {
            m_depthDirectionMode = DepthDirectionMode.OddEven;
            m_evenColumnDepthDirection = evenDir;
            m_oddColumnDepthDirection = oddDir;
            return;
        }

        m_depthDirectionMode = DepthDirectionMode.Detailed;
        m_depthDirection = depthDirections ?? Array.Empty<Vector3>();
    }

    private static bool TryInferOddEvenDirections(
        Vector3[] depthDirections,
        out Vector3 evenDir,
        out Vector3 oddDir)
    {
        evenDir = Vector3.zero;
        oddDir = Vector3.zero;

        if (depthDirections == null || depthDirections.Length == 0)
        {
            return true;
        }

        evenDir = depthDirections[0];
        oddDir = depthDirections.Length > 1 ? depthDirections[1] : evenDir;

        for (int i = 0; i < depthDirections.Length; i++)
        {
            Vector3 expected = (i & 1) == 0 ? evenDir : oddDir;
            if (depthDirections[i] != expected)
            {
                return false;
            }
        }

        return true;
    }

    private void EnsureDepthDirectionLength()
    {
        int columnCount = m_posColumn != null ? m_posColumn.Length : 0;
        if (columnCount <= 0)
        {
            m_depthDirection = Array.Empty<Vector3>();
            return;
        }

        if (m_depthDirection != null && m_depthDirection.Length == columnCount)
        {
            return;
        }

        var resized = new Vector3[columnCount];
        int copyCount = m_depthDirection != null ? Mathf.Min(m_depthDirection.Length, columnCount) : 0;
        for (int i = 0; i < copyCount; i++)
        {
            resized[i] = m_depthDirection[i];
        }

        for (int i = copyCount; i < columnCount; i++)
        {
            resized[i] = m_defaultDepthDirection;
        }

        m_depthDirection = resized;
    }

    private static void OffsetArray(float[] values, float offset)
    {
        if (values == null || Mathf.Approximately(offset, 0f))
        {
            return;
        }

        for (int i = 0; i < values.Length; i++)
        {
            values[i] += offset;
        }
    }

    private void FlipColumnDepthDirections()
    {
        if (IsDetailedDepthMode)
        {
            EnsureDepthDirectionLength();
            TryReverseArray(m_depthDirection);
            return;
        }

        // 奇偶模式：列数偶数时，翻转后原偶/奇列落到相反奇偶性上，需对调方向以保持物理列深度不变。
        if (IsOddEvenDepthMode && m_posColumn != null && (m_posColumn.Length & 1) == 0)
        {
            (m_evenColumnDepthDirection, m_oddColumnDepthDirection) =
                (m_oddColumnDepthDirection, m_evenColumnDepthDirection);
        }
    }

    private static bool TryReverseArray<T>(T[] values)
    {
        if (values == null || values.Length < 2)
        {
            return false;
        }

        Array.Reverse(values);
        return true;
    }

    private static void RemapIndexAfterFlip(ref int index, int count)
    {
        if (count <= 0)
        {
            index = 0;
            return;
        }

        index = count - 1 - Mathf.Clamp(index, 0, count - 1);
    }

    private string AxisOpLabel()
    {
        return m_axisOpTarget switch
        {
            AxisOpTarget.Row => "排",
            AxisOpTarget.Layer => "层",
            AxisOpTarget.Column => "列",
            _ => "未知轴",
        };
    }

    private string BuildLayoutSignature()
    {
        // 深度方向不参与坐标，不纳入布局签名；原点、轴方向与标量决定世界位置。
        return
            $"{FormatVector3(m_layoutOrigin)}|{FormatVector3(m_rowDirection)}|{FormatVector3(m_layerDirection)}|{FormatVector3(m_columnDirection)}|" +
            $"{ArraySignature(m_posRow)}|{ArraySignature(m_posLayer)}|{ArraySignature(m_posColumn)}|{m_depthCount}";
    }

    private string BuildVisibilitySignature()
    {
        return $"{m_focusMode}|{m_focusStyle}|{m_focusIndex}|{m_focusPinRow}|{m_focusPinColumn}|{m_focusPinLayer}|{DisableRulesSignature()}";
    }

    private string DisableRulesSignature()
    {
        if (m_disableRules == null || m_disableRules.Length == 0)
        {
            return "none";
        }

        var parts = new string[m_disableRules.Length];
        for (int i = 0; i < m_disableRules.Length; i++)
        {
            WarehouseSlotDisableRule rule = m_disableRules[i];
            if (rule == null)
            {
                parts[i] = "null";
                continue;
            }

            parts[i] =
                $"{rule.Enabled},R:{rule.ConstrainRow}:{rule.RowMin}:{rule.RowMax}," +
                $"C:{rule.ConstrainColumn}:{rule.ColumnMin}:{rule.ColumnMax}," +
                $"L:{rule.ConstrainLevel}:{rule.LevelMin}:{rule.LevelMax}," +
                $"D:{rule.ConstrainDepth}:{rule.DepthMin}:{rule.DepthMax}";
        }

        return string.Join(";", parts);
    }

    private static string FormatVector3(Vector3 v)
    {
        return $"{v.x:R},{v.y:R},{v.z:R}";
    }

    private static string ArraySignature(float[] values)
    {
        if (values == null)
        {
            return "null";
        }

        var parts = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            parts[i] = values[i].ToString("R");
        }

        return string.Join(",", parts);
    }

    private void OnDrawGizmosSelected()
    {
        if (!m_drawGizmos || m_posRow == null || m_posLayer == null || m_posColumn == null)
        {
            return;
        }

        if (m_posRow.Length == 0 || m_posLayer.Length == 0 || m_posColumn.Length == 0)
        {
            return;
        }

        int depthCount = Mathf.Max(1, m_depthCount);
        bool filterByFocus = IsFocusModeOn && m_focusGizmosOnly;

        Transform anchor = WarehouseTransform;
        Matrix4x4 prev = Gizmos.matrix;
        Color prevColor = Gizmos.color;
        Gizmos.matrix = anchor.localToWorldMatrix;
        Gizmos.color = m_gizmoColor;

        for (int row = 0; row < m_posRow.Length; row++)
        {
            for (int column = 0; column < m_posColumn.Length; column++)
            {
                for (int level = 0; level < m_posLayer.Length; level++)
                {
                    if (filterByFocus && !IsFocusedBin(level, column, row))
                    {
                        continue;
                    }

                    // 各 Depth 同坐标：只要该格有任一未禁用深度，画一个预览盒即可。
                    bool anyEnabled = false;
                    for (int depth = 0; depth < depthCount; depth++)
                    {
                        if (!IsSlotDisabled(level, column, row, depth))
                        {
                            anyEnabled = true;
                            break;
                        }
                    }

                    if (!anyEnabled)
                    {
                        continue;
                    }

                    var pos = ComposeWorldPosition(m_posRow[row], m_posLayer[level], m_posColumn[column]);
                    Gizmos.DrawWireCube(pos, m_gizmoSize);
                }
            }
        }

        Gizmos.matrix = prev;
        Gizmos.color = prevColor;
    }

    /// <summary>
    /// 货位过多时自动关闭 Gizmo（仅在规模变化时触发一次），之后仍可在 Inspector 手动开启。
    /// </summary>
    private void TryAutoDisableGizmosIfOversize()
    {
        long cellCount = EstimateGizmoCellCount();
        if (cellCount <= MaxGizmoCells)
        {
            _lastAutoDisableGizmoCellCount = cellCount;
            return;
        }

        // 同规模已处理过：不重复强关，保留用户手动重开的结果。
        if (cellCount == _lastAutoDisableGizmoCellCount)
        {
            return;
        }

        _lastAutoDisableGizmoCellCount = cellCount;
        if (!m_drawGizmos)
        {
            return;
        }

        m_drawGizmos = false;
        Debug.LogWarning(
            $"[WarehouseLayoutEditor] 货位约 {cellCount} 超过 Gizmo 建议上限 {MaxGizmoCells}，已自动关闭预览；可在「Gizmo」中手动重新开启。",
            this);
    }

    private long EstimateGizmoCellCount()
    {
        if (m_posRow == null || m_posLayer == null || m_posColumn == null)
        {
            return 0;
        }

        if (m_posRow.Length == 0 || m_posLayer.Length == 0 || m_posColumn.Length == 0)
        {
            return 0;
        }

        return (long)m_posRow.Length * m_posColumn.Length * m_posLayer.Length;
    }
}
#else
/// <summary>Player 构建占位：布局编辑器仅 Editor 可用，避免 Missing Script。</summary>
[DisallowMultipleComponent]
public class WarehouseManagerLayoutEditor : MonoBehaviour
{
}
#endif
