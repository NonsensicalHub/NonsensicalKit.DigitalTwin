using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.DigitalTwin.Motion;
using NonsensicalKit.Tools;
using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.DigitalTwin.Warehouse
{

/// <summary>
/// 堆垛机虚实同步：订阅 MotionPartUpdate，按 TravelCol / LiftLayer 驱动行走与升降，
/// 按 ForkSinglePos / ForkDoublePos 驱动货叉，并按 LoadStatus 生成或回收物料。
/// 点位约定与 MQTT 堆垛机报文 Points 顺序一致，见 <see cref="GetInfo"/>。
/// 注意：数据源 TravelCol（列位）对应本地仓库地图的排（Int4.Z），不是本地列。
/// 本地地图 Int4 从 0 起算；实际对接时列常从 0、层常从 1 起算，用 TravelColOffset / LiftLayerOffset 换算。
/// 模型枢轴与货位中心不一致时，用 TravelOffset / LiftOffset 修正轴目标。
/// </summary>
public class StackerPartMotion : PartMotionBase
{
    private const string PointTravelCol = "TravelCol";
    private const string PointLiftLayer = "LiftLayer";
    private const string PointForkSingle = "ForkSinglePos";
    private const string PointForkDouble = "ForkDoublePos";
    private const string PointLoadStatus = "LoadStatus";

    [Header("轴引用")]
    [Tooltip("行走轴；为空则移动本物体")]
    [SerializeField] private Transform m_travelAxis;
    [Tooltip("升降轴；为空则与行走轴相同")]
    [SerializeField] private Transform m_liftAxis;
    [SerializeField] private Transform m_forkSingle;
    [SerializeField] private Transform m_forkDouble;

    [Header("货位解析")]
    [Tooltip("通过 WarehouseManager.GetRuntimeBinData 解析货位世界坐标")]
    [SerializeField] private WarehouseManager m_warehouse;
    [Tooltip("机身所在巷道的本地地图列（Int4.Y）；数据 TravelCol 实际写入本地排")]
    [FormerlySerializedAs("m_aisleRow")]
    [SerializeField] private int m_aisleCol = 1;
    [Tooltip("机身深度（Int4.W）")]
    [SerializeField] private int m_aisleDepth;
    [Tooltip("数据 TravelCol → 本地排：本地排 = TravelCol + 本偏移。列从 0 起算时配 0，从 1 起算时配 -1")]
    [SerializeField] private int m_travelColOffset;
    [Tooltip("数据 LiftLayer → 本地层：本地层 = LiftLayer + 本偏移。层从 1 起算时配 -1，从 0 起算时配 0")]
    [SerializeField] private int m_liftLayerOffset = -1;

    [Header("运动轴向")]
    [Tooltip("行走方向应对齐本地排变化轴（本仓库排沿 X）")]
    [SerializeField] private SignedAxis m_travelDirection = SignedAxis.PositiveX;
    [SerializeField] private SignedAxis m_liftDirection = SignedAxis.PositiveY;

    [Header("位置修正")]
    [Tooltip("行走目标相对货位中心的修正（米），沿行走方向。模型枢轴与货位中心不一致时调整；正值沿配置轴向正向多走。")]
    [SerializeField] private float m_travelOffset;
    [Tooltip("升降目标相对货位中心的修正（米），沿升降方向。模型枢轴与货位中心不一致时调整；正值沿配置轴向正向多升。")]
    [SerializeField] private float m_liftOffset;

    [Header("货叉")]
    [SerializeField] private Vector3 m_forkAxisLocal = Vector3.forward;
    [Tooltip("货叉收回时的 PLC 编码器零位；位移 = (PLC - 本值) / UnitsPerMm 毫米")]
    [SerializeField] private int m_forkZeroPlc = 100000;
    [Tooltip("多少个 PLC 单位对应 1mm 位移；例如 20 表示每 20 单位移动 1mm")]
    [SerializeField] private float m_forkUnitsPerMm = 20f;

    [Header("速度（m/s）")]
    [SerializeField] private float m_travelSpeed = 4f;
    [SerializeField] private float m_liftSpeed = 1.2f;
    [SerializeField] private float m_forkSpeed = 0.8f;

    [Header("载货")]
    [Tooltip("物料挂点；为空则挂到货叉或升降轴。若挂点本身带 Renderer，会按 LoadStatus 显隐，不再走对象池")]
    [SerializeField] private Transform m_loadAnchor;
    [Tooltip("可选：单独指定场景货物模型做显隐；优先于 m_loadAnchor 上的 Renderer")]
    [SerializeField] private GameObject m_cargoVisual;
    [SerializeField] private float m_materialHeight = 0.1f;

    [Header("调试测试")]
    [Tooltip("测试用：数据 TravelCol（经 TravelColOffset 换算为本地排）")]
    [SerializeField] private int m_testTravelCol;
    [Tooltip("测试用：数据 LiftLayer（经 LiftLayerOffset 换算为本地层）")]
    [SerializeField] private int m_testLiftLayer = 1;
    [Tooltip("测试用：货叉 PLC 值（默认相对零位伸出约 25mm）")]
    [SerializeField] private int m_testForkPlc = 100500;
    [Tooltip("行走/升降步进时增减的排或层")]
    [SerializeField] private int m_testStep = 1;

    private Transform _travel;
    private Transform _lift;
    private Vector3 _forkSingleOrigin;
    private Vector3 _forkDoubleOrigin;
    private Vector3 _forkAxis;
    private bool _forkOriginCaptured;

    private Tweener _travelTweener;
    private Tweener _liftTweener;
    private Tweener _forkSingleTweener;
    private Tweener _forkDoubleTweener;

    private bool _first = true;
    private int _travelCol = int.MinValue;
    private int _liftLayer = int.MinValue;
    private int _forkSinglePos = int.MinValue;
    private int _forkDoublePos = int.MinValue;
    private bool _isLoad;

    private Transform _material;

    private Transform Travel => _travel != null ? _travel : transform;
    private Transform Lift => _lift != null ? _lift : Travel;

    protected override void Init()
    {
        base.Init();

        _travel = m_travelAxis != null ? m_travelAxis : transform;
        _lift = m_liftAxis != null ? m_liftAxis : _travel;
        _forkAxis = m_forkAxisLocal.sqrMagnitude > 1e-6f
            ? m_forkAxisLocal.normalized
            : Vector3.forward;

        if (m_forkSingle != null)
            _forkSingleOrigin = m_forkSingle.localPosition;
        if (m_forkDouble != null)
            _forkDoubleOrigin = m_forkDouble.localPosition;
        _forkOriginCaptured = true;

        // 场景货物默认隐藏，等首包 LoadStatus 再同步，避免一直显示载货
        if (TryGetSceneCargoVisual(out var visual))
            visual.SetActive(false);

        _first = true;
        _isLoad = false;
        _material = null;
    }

    protected override void Dispose()
    {
        AbortTween(ref _travelTweener);
        AbortTween(ref _liftTweener);
        AbortTween(ref _forkSingleTweener);
        AbortTween(ref _forkDoubleTweener);
        base.Dispose();
    }

    protected override void OnReceiveData(List<PointData> part)
    {
        if (part == null || part.Count == 0)
            return;

        int travelCol = ReadInt(part, PointTravelCol, 3, _travelCol);
        int liftLayer = ReadInt(part, PointLiftLayer, 4, _liftLayer);
        int forkSingle = ReadInt(part, PointForkSingle, 5, _forkSinglePos);
        int forkDouble = ReadInt(part, PointForkDouble, 6, _forkDoublePos);
        bool isLoad = ReadBool(part, PointLoadStatus, 7, _isLoad);

        bool snap = _first;
        _first = false;

        if (travelCol != _travelCol || liftLayer != _liftLayer)
        {
            _travelCol = travelCol;
            _liftLayer = liftLayer;
            // 数据源「列位」= 本地地图「排」；按配置偏移换算为 0 起算地图索引
            ApplySlot(
                travelAsRow: ToTravelRow(travelCol),
                layer: ToLiftLayer(liftLayer),
                snap);
        }

        if (forkSingle != _forkSinglePos)
        {
            _forkSinglePos = forkSingle;
            ApplyFork(m_forkSingle, ref _forkSingleTweener, _forkSingleOrigin, forkSingle, snap);
        }

        if (forkDouble != _forkDoublePos)
        {
            _forkDoublePos = forkDouble;
            ApplyFork(m_forkDouble, ref _forkDoubleTweener, _forkDoubleOrigin, forkDouble, snap);
        }

        ApplyLoadStatus(isLoad);
    }

    private void ApplyLoadStatus(bool isLoad)
    {
        if (isLoad)
        {
            if (!HasVisibleCargo())
                ShowCargo();
        }
        else if (HasVisibleCargo())
        {
            HideCargo();
        }

        _isLoad = isLoad;
    }

    private void ApplySlot(int travelAsRow, int layer, bool snap)
    {
        if (!TryGetSlotWorld(layer, travelAsRow, out Vector3 slotWorld))
        {
            Debug.LogWarning(
                $"[StackerPartMotion] 无法解析货位 mapLayer={layer} aisleCol={m_aisleCol} mapRow={travelAsRow} depth={m_aisleDepth}，partID={m_partID}",
                this);
            return;
        }

        Transform travel = Travel;
        Transform lift = Lift;

        // 货位中心 + 模型枢轴修正 → 轴目标
        float travelValue = SignedAxisUtil.GetComponent(slotWorld, m_travelDirection) + m_travelOffset;
        float liftValue = SignedAxisUtil.GetComponent(slotWorld, m_liftDirection) + m_liftOffset;

        AbortTween(ref _travelTweener);
        AbortTween(ref _liftTweener);

        if (travel == lift)
        {
            Vector3 target = travel.position;
            target = SignedAxisUtil.WithComponent(target, m_travelDirection, travelValue);
            target = SignedAxisUtil.WithComponent(target, m_liftDirection, liftValue);

            if (snap || m_travelSpeed <= 0f)
                travel.position = target;
            else
                _travelTweener = travel.DoMove(target, m_travelSpeed).SetSpeedBased();
            return;
        }

        // 先动行走轴
        Vector3 travelTarget = SignedAxisUtil.WithComponent(
            travel.position, m_travelDirection, travelValue);

        if (snap || m_travelSpeed <= 0f)
            travel.position = travelTarget;
        else
            _travelTweener = travel.DoMove(travelTarget, m_travelSpeed).SetSpeedBased();

        // 升降只改升降分量：必须基于「当前」世界坐标写入。
        // 若对子物体做完整 DoMove，会把行走分量锁在旧世界坐标上，父子跟随失效。
        if (snap || m_liftSpeed <= 0f)
        {
            lift.position = SignedAxisUtil.WithComponent(
                lift.position, m_liftDirection, liftValue);
        }
        else
        {
            _liftTweener = DoAxisWorldMove(lift, m_liftDirection, liftValue, m_liftSpeed);
        }
    }

    /// <summary>
    /// 仅插值世界坐标单一轴向；每帧读取当前位置，保留父节点带动的其它分量。
    /// </summary>
    private static Tweener DoAxisWorldMove(
        Transform target, SignedAxis axis, float endValue, float speed)
    {
        var tweener = new AxisWorldMoveTweener(target, axis, endValue, speed);
        var hub = NonsensicalInstance.Instance;
        if (hub != null)
            hub.Tweeners.Add(tweener);
        return tweener.SetSpeedBased();
    }

    private sealed class AxisWorldMoveTweener : Tweener
    {
        private readonly Transform _transform;
        private readonly SignedAxis _axis;
        private readonly float _startValue;
        private readonly float _endValue;

        public AxisWorldMoveTweener(
            Transform transform, SignedAxis axis, float endValue, float value) : base(value)
        {
            _transform = transform;
            _axis = axis;
            _startValue = transform != null
                ? SignedAxisUtil.GetComponent(transform.position, axis)
                : endValue;
            _endValue = endValue;
            TotalValue = Mathf.Abs(_endValue - _startValue);
        }

        public override bool DoSpecificBySchedule(float schedule)
        {
            if (_transform == null)
                return true;

            float v = _startValue + (_endValue - _startValue) * schedule;
            _transform.position = SignedAxisUtil.WithComponent(
                _transform.position, _axis, v);
            return false;
        }
    }

    private void ApplyFork(Transform fork, ref Tweener tweener, Vector3 origin, int plcValue, bool snap)
    {
        if (fork == null)
            return;

        Vector3 target = origin + _forkAxis * ForkPlcToMeters(plcValue);

        AbortTween(ref tweener);

        if (snap || m_forkSpeed <= 0f)
            fork.localPosition = target;
        else
            tweener = fork.DoLocalMove(target, m_forkSpeed).SetSpeedBased();
    }

    /// <summary>
    /// PLC 编码器 → 米：相对零位的差除以每毫米单位数，再换算为米。
    /// </summary>
    private float ForkPlcToMeters(int plcValue)
    {
        float unitsPerMm = m_forkUnitsPerMm > 1e-6f ? m_forkUnitsPerMm : 20f;
        return (plcValue - m_forkZeroPlc) / unitsPerMm * 0.001f;
    }

    /// <param name="travelAsRow">已转为 0 起算的本地排（Int4.Z）。</param>
    /// <param name="layer">已转为 0 起算的本地层（Int4.X）。</param>
    private bool TryGetSlotWorld(int layer, int travelAsRow, out Vector3 worldPos)
    {
        var cell = new Int4(layer, m_aisleCol, travelAsRow, m_aisleDepth);
        return TryResolveCellWorld(cell, out worldPos);
    }

    /// <summary>数据 TravelCol → 本地排（Int4.Z）。</summary>
    private int ToTravelRow(int travelCol) => travelCol + m_travelColOffset;

    /// <summary>数据 LiftLayer → 本地层（Int4.X）。</summary>
    private int ToLiftLayer(int liftLayer) => liftLayer + m_liftLayerOffset;

    private bool HasVisibleCargo()
    {
        if (TryGetSceneCargoVisual(out var visual))
            return visual.activeSelf;

        return _material != null;
    }

    private void ShowCargo()
    {
        if (TryGetSceneCargoVisual(out var visual))
        {
            visual.SetActive(true);
            _material = visual.transform;
            return;
        }

        Transform anchor = ResolveLoadAnchor();
        Vector3 pos = anchor.position + anchor.up * m_materialHeight;

        var go = Execute<Vector3, GameObject>("CreateNewMaterial", pos);
        if (go == null)
            return;

        _material = go.transform;
        _material.SetParent(anchor, true);
        _material.position = pos;
    }

    private void HideCargo()
    {
        if (TryGetSceneCargoVisual(out var visual))
        {
            visual.SetActive(false);
            _material = null;
            return;
        }

        if (_material == null)
            return;

        Publish("RecycleMaterialModel", _material);
        _material = null;
    }

    /// <summary>
    /// 优先 m_cargoVisual；否则若 m_loadAnchor 自身带 Renderer（场景已挂货物模型），则作为显隐对象。
    /// </summary>
    private bool TryGetSceneCargoVisual(out GameObject visual)
    {
        if (m_cargoVisual != null)
        {
            visual = m_cargoVisual;
            return true;
        }

        if (m_loadAnchor != null && m_loadAnchor.GetComponent<Renderer>() != null)
        {
            visual = m_loadAnchor.gameObject;
            return true;
        }

        visual = null;
        return false;
    }

    private Transform ResolveLoadAnchor()
    {
        if (m_loadAnchor != null)
            return m_loadAnchor;
        if (m_forkDouble != null)
            return m_forkDouble;
        if (m_forkSingle != null)
            return m_forkSingle;
        return Lift;
    }

    private static void AbortTween(ref Tweener tweener)
    {
        if (tweener == null)
            return;

        tweener.Abort();
        tweener = null;
    }

    private static bool TryFind(List<PointData> part, string pointCode, out PointData point)
    {
        for (int i = 0; i < part.Count; i++)
        {
            var p = part[i];
            if (p == null)
                continue;

            if (p.Point == pointCode || p.Name == pointCode)
            {
                point = p;
                return true;
            }
        }

        point = null;
        return false;
    }

    private static int ReadInt(List<PointData> part, string pointCode, int fallbackIndex, int fallbackValue)
    {
        if (TryFind(part, pointCode, out var point))
            return ParseInt(point.Value, fallbackValue);

        if (fallbackIndex >= 0 && fallbackIndex < part.Count && part[fallbackIndex] != null)
            return ParseInt(part[fallbackIndex].Value, fallbackValue);

        return fallbackValue;
    }

    private static bool ReadBool(List<PointData> part, string pointCode, int fallbackIndex, bool fallbackValue)
    {
        if (TryFind(part, pointCode, out var point))
            return ParseBool(point.Value, fallbackValue);

        if (fallbackIndex >= 0 && fallbackIndex < part.Count && part[fallbackIndex] != null)
            return ParseBool(part[fallbackIndex].Value, fallbackValue);

        return fallbackValue;
    }

    private static int ParseInt(string raw, int fallback)
    {
        if (string.IsNullOrEmpty(raw))
            return fallback;

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
            return v;

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
            return (int)l;

        return fallback;
    }

    private static bool ParseBool(string raw, bool fallback)
    {
        if (string.IsNullOrEmpty(raw))
            return fallback;

        if (bool.TryParse(raw, out bool b))
            return b;

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            return n != 0;

        return fallback;
    }

    protected override PartDataInfo GetInfo() =>
        new PartDataInfo("堆垛机", m_partID,
            new List<PointDataInfo>
            {
                new PointDataInfo("运行停止状态", PointDataType.Bool, false),
                new PointDataInfo("自动手动状态", PointDataType.Bool, false),
                new PointDataInfo("故障", PointDataType.Bool, false),
                new PointDataInfo("行走位置列位", PointDataType.Int, false),
                new PointDataInfo("提升高度层位", PointDataType.Int, false),
                new PointDataInfo("单伸货叉当前位置", PointDataType.Int, false),
                new PointDataInfo("双伸货叉当前位置", PointDataType.Int, false),
                new PointDataInfo("载货状态", PointDataType.Bool, false),
                new PointDataInfo("当前任务号", PointDataType.Int, false),
                new PointDataInfo("起始层", PointDataType.Int, false),
                new PointDataInfo("起始列", PointDataType.Int, false),
                new PointDataInfo("起始排", PointDataType.Int, false),
                new PointDataInfo("目标层", PointDataType.Int, false),
                new PointDataInfo("目标列", PointDataType.Int, false),
                new PointDataInfo("目标排", PointDataType.Int, false),
            });

    // -------------------------------------------------------------------------
    // 调试测试（Inspector Button / ContextMenu）
    // -------------------------------------------------------------------------

    [Button("校验轴向与货位映射")]
    [ContextMenu("Stacker/校验轴向与货位映射")]
    private void TestValidateAxes()
    {
        EnsureTestReady();

        var sb = new StringBuilder();
        sb.AppendLine($"[StackerPartMotion] 校验 partID={m_partID}");
        sb.AppendLine(
            $"配置: aisleCol={m_aisleCol} depth={m_aisleDepth} travelColOffset={m_travelColOffset} liftLayerOffset={m_liftLayerOffset} " +
            $"travelDir={m_travelDirection} liftDir={m_liftDirection} " +
            $"travelOffset={m_travelOffset:F3} liftOffset={m_liftOffset:F3}");
        sb.AppendLine(
            $"轴: travel={(m_travelAxis != null ? m_travelAxis.name : transform.name)} " +
            $"lift={(m_liftAxis != null ? m_liftAxis.name : "(同行走)")}");

        int baseLayer = Mathf.Max(0, ToLiftLayer(m_testLiftLayer));
        int baseRow = Mathf.Max(0, ToTravelRow(m_testTravelCol));
        int baseCol = m_aisleCol;

        if (!TryResolveAny(baseLayer, baseCol, baseRow, out Vector3 basePos))
        {
            sb.AppendLine(
                $"失败: 基准货位无法解析 Int4(层={baseLayer},列={baseCol},排={baseRow},深={m_aisleDepth})。" +
                "请确认 Warehouse 已初始化（建议 Play 模式）或 CellTable 有数据。");
            Debug.LogWarning(sb.ToString(), this);
            return;
        }

        bool hasRow = TryResolveAny(baseLayer, baseCol, baseRow + 1, out Vector3 rowPos);
        bool hasLayer = TryResolveAny(baseLayer + 1, baseCol, baseRow, out Vector3 layerPos);
        bool hasCol = TryResolveAny(baseLayer, baseCol + 1, baseRow, out Vector3 colPos);

        Vector3 dRow = hasRow ? rowPos - basePos : Vector3.zero;
        Vector3 dLayer = hasLayer ? layerPos - basePos : Vector3.zero;
        Vector3 dCol = hasCol ? colPos - basePos : Vector3.zero;

        string rowAxis = DominantAxisName(dRow);
        string layerAxis = DominantAxisName(dLayer);
        string colAxis = DominantAxisName(dCol);

        sb.AppendLine($"基准货位 {FmtCell(baseLayer, baseCol, baseRow)} => {Fmt(basePos)}");
        if (hasRow)
            sb.AppendLine($"排+1 变化 {Fmt(dRow)} 主轴={rowAxis}（数据 TravelCol 应对齐此轴）");
        else
            sb.AppendLine("排+1 货位不存在，跳过行走轴推断");

        if (hasLayer)
            sb.AppendLine($"层+1 变化 {Fmt(dLayer)} 主轴={layerAxis}（LiftLayer 应对齐此轴）");
        else
            sb.AppendLine("层+1 货位不存在，跳过升降轴推断");

        if (hasCol)
            sb.AppendLine($"列+1 变化 {Fmt(dCol)} 主轴={colAxis}（应接近固定巷道，行走不应跟此轴）");

        string travelAxisName = AxisComponentName(m_travelDirection);
        string liftAxisName = AxisComponentName(m_liftDirection);

        bool travelOk = !hasRow || rowAxis == travelAxisName;
        bool liftOk = !hasLayer || layerAxis == liftAxisName;
        bool axesDistinct = SignedAxisUtil.ToComponentIndex(m_travelDirection)
                            != SignedAxisUtil.ToComponentIndex(m_liftDirection);

        sb.AppendLine(
            travelOk
                ? $"行走方向 OK：配置 {m_travelDirection} 与排变化主轴 {rowAxis} 一致"
                : $"行走方向可疑：配置 {m_travelDirection}({travelAxisName})，排变化主轴为 {rowAxis}，建议改成 ±{rowAxis}");
        sb.AppendLine(
            liftOk
                ? $"升降方向 OK：配置 {m_liftDirection} 与层变化主轴 {layerAxis} 一致"
                : $"升降方向可疑：配置 {m_liftDirection}({liftAxisName})，层变化主轴为 {layerAxis}，建议改成 ±{layerAxis}");
        sb.AppendLine(
            axesDistinct
                ? "行走/升降轴向分量不同 OK"
                : "警告：行走与升降使用了同一世界分量，双轴会互相覆盖");

        Transform travel = Travel;
        float travelComp = SignedAxisUtil.GetComponent(travel.position, m_travelDirection);
        float slotTravelComp = SignedAxisUtil.GetComponent(basePos, m_travelDirection) + m_travelOffset;
        float aisleDelta = Mathf.Abs(travelComp - slotTravelComp);
        sb.AppendLine(
            $"机身相对基准货位（含 travelOffset）行走分量差={aisleDelta:F3}m（过大说明 aisleCol/排基准或轴向可能不对）");

        if (travelOk && liftOk && axesDistinct)
            Debug.Log(sb.ToString(), this);
        else
            Debug.LogWarning(sb.ToString(), this);
    }

    [Button("瞬移到测试货位")]
    [ContextMenu("Stacker/瞬移到测试货位")]
    private void TestSnapToSlot()
    {
        EnsureTestReady();
        _travelCol = m_testTravelCol;
        _liftLayer = m_testLiftLayer;
        ApplySlot(
            travelAsRow: ToTravelRow(m_testTravelCol),
            layer: ToLiftLayer(m_testLiftLayer),
            snap: true);
        Debug.Log(
            $"[StackerPartMotion] 瞬移到测试货位 TravelCol={m_testTravelCol}→排{ToTravelRow(m_testTravelCol)} " +
            $"LiftLayer={m_testLiftLayer}→层{ToLiftLayer(m_testLiftLayer)} " +
            $"aisleCol={m_aisleCol} depth={m_aisleDepth} " +
            $"travelOffset={m_travelOffset:F3} liftOffset={m_liftOffset:F3}\n" +
            $"travel={Travel.position} lift={Lift.position}",
            this);
    }

    [Button("行走步进 +排")]
    [ContextMenu("Stacker/行走步进 +排")]
    private void TestTravelStepPlus() => TestTravelStep(+Mathf.Max(1, m_testStep));

    [Button("行走步进 -排")]
    [ContextMenu("Stacker/行走步进 -排")]
    private void TestTravelStepMinus() => TestTravelStep(-Mathf.Max(1, m_testStep));

    [Button("升降步进 +层")]
    [ContextMenu("Stacker/升降步进 +层")]
    private void TestLiftStepPlus() => TestLiftStep(+Mathf.Max(1, m_testStep));

    [Button("升降步进 -层")]
    [ContextMenu("Stacker/升降步进 -层")]
    private void TestLiftStepMinus() => TestLiftStep(-Mathf.Max(1, m_testStep));

    [Button("货叉伸出测试")]
    [ContextMenu("Stacker/货叉伸出测试")]
    private void TestForkExtend()
    {
        EnsureTestReady();
        ApplyFork(m_forkSingle, ref _forkSingleTweener, _forkSingleOrigin, m_testForkPlc, snap: true);
        ApplyFork(m_forkDouble, ref _forkDoubleTweener, _forkDoubleOrigin, m_testForkPlc, snap: true);
        Debug.Log(
            $"[StackerPartMotion] 货叉伸出 PLC={m_testForkPlc} zero={m_forkZeroPlc} unitsPerMm={m_forkUnitsPerMm} " +
            $"→ 本地偏移 {_forkAxis * ForkPlcToMeters(m_testForkPlc)}",
            this);
    }

    [Button("货叉回零")]
    [ContextMenu("Stacker/货叉回零")]
    private void TestForkHome()
    {
        EnsureTestReady();
        ApplyFork(m_forkSingle, ref _forkSingleTweener, _forkSingleOrigin, m_forkZeroPlc, snap: true);
        ApplyFork(m_forkDouble, ref _forkDoubleTweener, _forkDoubleOrigin, m_forkZeroPlc, snap: true);
        Debug.Log($"[StackerPartMotion] 货叉已回零 PLC={m_forkZeroPlc}", this);
    }

    private void TestTravelStep(int deltaRow)
    {
        EnsureTestReady();
        int travelCol = (_travelCol == int.MinValue ? m_testTravelCol : _travelCol) + deltaRow;
        int liftLayer = _liftLayer == int.MinValue ? m_testLiftLayer : _liftLayer;
        m_testTravelCol = travelCol;
        _travelCol = travelCol;
        _liftLayer = liftLayer;
        ApplySlot(
            travelAsRow: ToTravelRow(travelCol),
            layer: ToLiftLayer(liftLayer),
            snap: true);
        Debug.Log(
            $"[StackerPartMotion] 行走步进 Δ列={deltaRow} → TravelCol={travelCol}→排{ToTravelRow(travelCol)} " +
            $"LiftLayer={liftLayer}→层{ToLiftLayer(liftLayer)} pos={Travel.position}",
            this);
    }

    private void TestLiftStep(int deltaLayer)
    {
        EnsureTestReady();
        int travelCol = _travelCol == int.MinValue ? m_testTravelCol : _travelCol;
        int liftLayer = (_liftLayer == int.MinValue ? m_testLiftLayer : _liftLayer) + deltaLayer;
        m_testLiftLayer = liftLayer;
        _travelCol = travelCol;
        _liftLayer = liftLayer;
        ApplySlot(
            travelAsRow: ToTravelRow(travelCol),
            layer: ToLiftLayer(liftLayer),
            snap: true);
        Debug.Log(
            $"[StackerPartMotion] 升降步进 Δ层={deltaLayer} → TravelCol={travelCol}→排{ToTravelRow(travelCol)} " +
            $"LiftLayer={liftLayer}→层{ToLiftLayer(liftLayer)} pos={Lift.position}",
            this);
    }

    private void EnsureTestReady()
    {
        _travel = m_travelAxis != null ? m_travelAxis : transform;
        _lift = m_liftAxis != null ? m_liftAxis : _travel;
        _forkAxis = m_forkAxisLocal.sqrMagnitude > 1e-6f
            ? m_forkAxisLocal.normalized
            : Vector3.forward;

        if (!_forkOriginCaptured)
        {
            if (m_forkSingle != null)
                _forkSingleOrigin = m_forkSingle.localPosition;
            if (m_forkDouble != null)
                _forkDoubleOrigin = m_forkDouble.localPosition;
            _forkOriginCaptured = true;
        }
    }

    private bool TryResolveAny(int layer, int col, int row, out Vector3 worldPos)
    {
        var cell = new Int4(layer, col, row, m_aisleDepth);
        return TryResolveCellWorld(cell, out worldPos);
    }

    private bool TryResolveCellWorld(Int4 cell, out Vector3 worldPos)
    {
        var data = m_warehouse != null ? m_warehouse.GetRuntimeBinData(cell) : null;
        if (data == null)
        {
            worldPos = default;
            return false;
        }

        worldPos = m_warehouse.transform.TransformPoint(data.Pos);
        return true;
    }

    private static string DominantAxisName(Vector3 delta)
    {
        float ax = Mathf.Abs(delta.x);
        float ay = Mathf.Abs(delta.y);
        float az = Mathf.Abs(delta.z);
        if (ax < 1e-4f && ay < 1e-4f && az < 1e-4f)
            return "无";
        if (ax >= ay && ax >= az)
            return "X";
        if (ay >= ax && ay >= az)
            return "Y";
        return "Z";
    }

    private static string AxisComponentName(SignedAxis axis) =>
        SignedAxisUtil.ToComponentIndex(axis) switch
        {
            0 => "X",
            1 => "Y",
            _ => "Z"
        };

    private static string Fmt(Vector3 v) => $"({v.x:F2}, {v.y:F2}, {v.z:F2})";

    private string FmtCell(int layer, int col, int row) =>
        $"Int4(层={layer},列={col},排={row},深={m_aisleDepth})";
}
}
