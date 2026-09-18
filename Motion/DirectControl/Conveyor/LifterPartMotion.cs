using System;
using System.Collections.Generic;
using System.Globalization;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.Tools;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{

/// <summary>
/// 提升机：可改变高度的输送工位。
/// 水平方向复用 <see cref="ConveyorPartMotion"/> 交界切权与皮带驱动；
/// 垂直方向按 CurrentFloor（PLC 高度值）升降轿厢，持货时托盘挂在升降轴上一起走。
/// 点位：CurrentFloor / CabinPlatform / ProcessStatus / ActualSpeed。
/// 流程状态判方向（2 正转进、4 反转出），实际转速只表示速度大小（0–1500，无符号）。
/// CurrentFloor 是连续高度：一楼 0、二楼 50、三楼 100，层间也会变化；
/// 世界坐标按相邻停靠点插值。皮带拓扑仍按逻辑楼层（1/2/3，下标 = 楼层 - 1）。
/// </summary>
public class LifterPartMotion : ConveyorPartMotion
{
    private const string PointCurrentFloor = "CurrentFloor";
    private const string PointCabinPlatform = "CabinPlatform";
    private const string PointProcessStatus = "ProcessStatus";
    private const string PointActualSpeed = "ActualSpeed";

    /// <summary>到达接货层，平台正转进。</summary>
    private const int ProcessInbound = 2;
    /// <summary>到达目标层，反转出货。</summary>
    private const int ProcessOutbound = 4;

    /// <summary>未配置 PLC 高度表时，按层间距 50 生成（0 / 50 / 100 …）。</summary>
    private const int DefaultPlcHeightStep = 50;

    [Serializable]
    public class FloorBeltLink
    {
        [Tooltip("正转下游（往里进；常为空，停在轿厢中心）")]
        public ConveyorPartMotion forwardNext;
        [Tooltip("反转下游（往外出到该层门口工位）")]
        public ConveyorPartMotion reverseNext;
        [Tooltip("该层进/出方向取反（流程状态 2/4 对调；个别楼层一层反转进时用）")]
        public bool invertSpeedSign;
    }

    [Header("轴引用")]
    [Tooltip("轿厢/升降平台；为空则移动本物体")]
    [SerializeField] private Transform m_liftAxis;
    [Tooltip("物料挂点；为空则挂到升降轴")]
    [SerializeField] private Transform m_loadAnchor;

    [Header("楼层高度")]
    [Tooltip("各逻辑层对应的 PLC CurrentFloor 高度值（一楼0、二楼50、三楼100），与 Markers/Heights/BeltLinks 下标对齐")]
    [SerializeField] private int[] m_floorPlcHeights = { 0, 50, 100 };
    [Tooltip("各层停靠点 Transform，下标 = 逻辑楼层 - 1；优先于高度表")]
    [SerializeField] private Transform[] m_floorMarkers;
    [Tooltip("各层沿升降轴的世界坐标分量，下标 = 逻辑楼层 - 1；无 Marker 时使用")]
    [SerializeField] private float[] m_floorHeights;

    [Header("楼层皮带拓扑")]
    [Tooltip("各层正反转下游，下标 = 逻辑楼层 - 1（编辑器用 1/2/3）")]
    [SerializeField] private FloorBeltLink[] m_floorBeltLinks;

    [Header("运动轴向")]
    [SerializeField] private SignedAxis m_liftDirection = SignedAxis.PositiveY;

    [Header("速度（m/s）")]
    [SerializeField] private float m_liftSpeed = 1.2f;
    [Tooltip("ActualSpeed 死区：低于此视为停转（转速无正负）")]
    [SerializeField] private float m_speedDeadzone = 1f;
    [Tooltip("皮带线速度 = ActualSpeed * 比例；≤0 则只用基类 m_speed")]
    [SerializeField] private float m_rpmToMetersPerSec;

    [Header("调试测试")]
    [Tooltip("测试用：PLC CurrentFloor 高度值（一楼0、二楼50、三楼100）")]
    [SerializeField] private int m_testPlcHeight;
    [Tooltip("升降步进时增减的 PLC 高度")]
    [SerializeField] private int m_testStep = 10;
    [Tooltip("测试用：流程状态（2 正转进，4 反转出）")]
    [SerializeField] private int m_testProcessStatus = 2;
    [Tooltip("测试用：ActualSpeed（0–1500，无符号）")]
    [SerializeField] private int m_testActualSpeed = 100;

    private Transform _lift;
    private Tweener _liftTweener;
    private bool _first = true;
    /// <summary>最近一次 PLC CurrentFloor 高度值。</summary>
    private int _plcHeight = int.MinValue;
    /// <summary>按高度解析出的逻辑楼层（1 起算），用于皮带拓扑。</summary>
    private int _logicalFloor = 1;
    private int _processStatus;
    private float _baseBeltSpeed = 0.2f;
    private bool _baseBeltSpeedCaptured;

    private Transform Lift => _lift != null ? _lift : transform;

    /// <summary>任意楼层的正转/反转下游是否指向指定工位（换层后仍可用于上游探测）。</summary>
    public bool IsFloorNeighbor(ConveyorPartMotion station)
    {
        if (station == null || m_floorBeltLinks == null)
            return false;

        for (int i = 0; i < m_floorBeltLinks.Length; i++)
        {
            var link = m_floorBeltLinks[i];
            if (link == null) continue;
            if (link.forwardNext == station || link.reverseNext == station)
                return true;
        }

        return false;
    }

    /// <summary>停靠点跟轿厢走：水平用升降轴，高度 = 轿厢 + materialHeight。</summary>
    public override Vector3 MaterialPoint
    {
        get
        {
            var lift = Lift;
            var origin = lift.position + lift.up * m_materialHeight;
            var axis = ForwardWorldAxis;
            if (axis.sqrMagnitude < 1e-8f || Mathf.Abs(m_parkOffset) < 1e-6f)
                return origin;
            return origin + axis.normalized * m_parkOffset;
        }
    }

    protected override void Init()
    {
        base.Init();
        _lift = m_liftAxis != null ? m_liftAxis : transform;
        CaptureBaseBeltSpeed();
        _first = true;
        if (_plcHeight == int.MinValue)
            _plcHeight = GetFloorPlcHeight(0);
        _logicalFloor = ResolveLogicalFloor(_plcHeight);
        SyncFloorBeltLinks(_logicalFloor);
    }

    protected override void Dispose()
    {
        AbortTween(ref _liftTweener);
        base.Dispose();
    }

    protected override void Update()
    {
        base.Update();
        KeepCargoOnLift();
    }

    protected override void OnReceiveData(List<PointData> part)
    {
        if (part == null || part.Count == 0)
            return;

        int plcHeight = ReadInt(
            part,
            PointCurrentFloor,
            1,
            _plcHeight == int.MinValue ? GetFloorPlcHeight(0) : _plcHeight);
        bool isLoad = ReadBool(part, PointCabinPlatform, 3, IsLoad);
        int process = ReadInt(part, PointProcessStatus, 7, _processStatus);
        int rpm = ReadActualSpeed(part, m_testActualSpeed);
        _processStatus = process;

        bool snap = _first;
        _first = false;

        int logicalFloor = ResolveLogicalFloor(plcHeight);
        if (plcHeight != _plcHeight)
        {
            _plcHeight = plcHeight;
            _logicalFloor = logicalFloor;
            SyncFloorBeltLinks(logicalFloor);
            ApplyPlcHeight(plcHeight, snap);
        }
        else
        {
            _logicalFloor = logicalFloor;
            SyncFloorBeltLinks(logicalFloor);
        }

        MapProcessToBelt(process, rpm, out bool forward, out bool reverse);
        ApplyMotionSignals(forward, reverse, isLoad);
    }

    /// <summary>补建时直接落在轿厢中心，避免入口偏移甩到轿厢外。</summary>
    protected override Vector3 GetCreateSpawnPoint() => MaterialPoint;

    protected override void OnMaterialAccepted(Transform material)
    {
        AttachToLift(material, snapToAnchor: false);
    }

    protected override void OnMaterialParked()
    {
        if (Material == null) return;
        AttachToLift(Material, snapToAnchor: false);
    }

    protected override void OnBeforeTransferDownstream(Transform material)
    {
        DetachFromLift(material);
    }

    protected override void OnMaterialTakenAway(Transform material)
    {
        DetachFromLift(material);
    }

    protected override string DiagStateSnapshot()
    {
        return base.DiagStateSnapshot()
            + $" plcH={(_plcHeight == int.MinValue ? "-" : _plcHeight.ToString())}"
            + $" floor={_logicalFloor}"
            + $" process={_processStatus}"
            + $" liftPos={Lift.position.y:F3}";
    }

    protected override PartDataInfo GetInfo() =>
        new PartDataInfo("提升机", m_partID,
            new List<PointDataInfo>
            {
                new PointDataInfo("模式", PointDataType.Int, false),
                new PointDataInfo("当前高度", PointDataType.Int, false),
                new PointDataInfo("升降状态", PointDataType.Int, false),
                new PointDataInfo("轿厢内平台", PointDataType.Bool, false),
                new PointDataInfo("轿厢口光幕", PointDataType.Bool, false),
                new PointDataInfo("故障", PointDataType.Bool, false),
                new PointDataInfo("运行状态", PointDataType.Bool, false),
                new PointDataInfo("流程状态", PointDataType.Int, false),
                new PointDataInfo("输出频率", PointDataType.Int, false),
                new PointDataInfo("输出电流", PointDataType.Int, false),
                new PointDataInfo("输出电压", PointDataType.Int, false),
                new PointDataInfo("母线电压", PointDataType.Int, false),
                new PointDataInfo("实际转速", PointDataType.Int, false),
            });

    /// <summary>
    /// 流程状态 2=正转进、4=反转出；其余（1 校位、3 换层、5 结束）停转。
    /// ActualSpeed 只提供速度大小，不参与方向。
    /// </summary>
    private void MapProcessToBelt(int processStatus, int rpm, out bool forward, out bool reverse)
    {
        CaptureBaseBeltSpeed();

        bool inbound = processStatus == ProcessInbound;
        bool outbound = processStatus == ProcessOutbound;
        var link = GetFloorLink(_logicalFloor);
        if (link != null && link.invertSpeedSign)
            (inbound, outbound) = (outbound, inbound);

        if (rpm < m_speedDeadzone || (!inbound && !outbound))
        {
            forward = false;
            reverse = false;
            m_speed = _baseBeltSpeed;
            return;
        }

        forward = inbound;
        reverse = outbound;

        if (m_rpmToMetersPerSec > 0f)
            m_speed = Mathf.Max(0.01f, rpm * m_rpmToMetersPerSec);
        else
            m_speed = _baseBeltSpeed;
    }

    private void SyncFloorBeltLinks(int logicalFloor)
    {
        var link = GetFloorLink(logicalFloor);
        if (link == null)
        {
            m_forwardNext = null;
            m_reverseNext = null;
            return;
        }

        m_forwardNext = link.forwardNext;
        m_reverseNext = link.reverseNext;
    }

    /// <summary>逻辑楼层从 1 起算；编辑器 / ConveyorLineManager.Floor 同样约定。</summary>
    private FloorBeltLink GetFloorLink(int logicalFloor)
    {
        if (m_floorBeltLinks == null || logicalFloor < 1)
            return null;
        int index = logicalFloor - 1;
        if (index >= m_floorBeltLinks.Length)
            return null;
        return m_floorBeltLinks[index];
    }

    private void ApplyPlcHeight(int plcHeight, bool snap)
    {
        if (!TryResolveLiftFromPlcHeight(plcHeight, out float liftValue))
        {
            Debug.LogWarning(
                $"[LifterPartMotion] 无法解析 PLC 高度={plcHeight}（需配置 floorMarkers / floorHeights），partID={m_partID}",
                this);
            return;
        }

        if (Material != null)
            AttachToLift(Material, snapToAnchor: false);

        Transform lift = Lift;
        AbortTween(ref _liftTweener);

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
    /// 将 PLC CurrentFloor 高度映射到世界升降分量：
    /// 落在停靠点上直接取值，落在层间则按相邻停靠点线性插值。
    /// </summary>
    private bool TryResolveLiftFromPlcHeight(int plcHeight, out float liftValue)
    {
        liftValue = 0f;
        int stopCount = GetFloorStopCount();
        if (stopCount <= 0)
            return false;

        if (stopCount == 1)
            return TryGetFloorLiftValueByIndex(0, out liftValue);

        // 找最近的下方/上方停靠点做插值
        int lower = 0;
        int upper = 0;
        bool foundLower = false;
        bool foundUpper = false;
        for (int i = 0; i < stopCount; i++)
        {
            int plc = GetFloorPlcHeight(i);
            if (plc <= plcHeight && (!foundLower || plc >= GetFloorPlcHeight(lower)))
            {
                lower = i;
                foundLower = true;
            }
            if (plc >= plcHeight && (!foundUpper || plc <= GetFloorPlcHeight(upper)))
            {
                upper = i;
                foundUpper = true;
            }
        }

        if (!foundLower && !foundUpper)
            return false;

        if (!foundLower)
            return TryGetFloorLiftValueByIndex(upper, out liftValue);
        if (!foundUpper || lower == upper)
            return TryGetFloorLiftValueByIndex(lower, out liftValue);

        if (!TryGetFloorLiftValueByIndex(lower, out float lowerLift)
            || !TryGetFloorLiftValueByIndex(upper, out float upperLift))
            return false;

        int lowerPlc = GetFloorPlcHeight(lower);
        int upperPlc = GetFloorPlcHeight(upper);
        if (lowerPlc == upperPlc)
        {
            liftValue = lowerLift;
            return true;
        }

        float t = (plcHeight - lowerPlc) / (float)(upperPlc - lowerPlc);
        liftValue = Mathf.LerpUnclamped(lowerLift, upperLift, t);
        return true;
    }

    private bool TryGetFloorLiftValueByIndex(int index, out float liftValue)
    {
        liftValue = 0f;
        if (index < 0)
            return false;

        if (m_floorMarkers != null && index < m_floorMarkers.Length && m_floorMarkers[index] != null)
        {
            liftValue = SignedAxisUtil.GetComponent(m_floorMarkers[index].position, m_liftDirection);
            return true;
        }

        if (m_floorHeights != null && index < m_floorHeights.Length)
        {
            liftValue = m_floorHeights[index];
            return true;
        }

        return false;
    }

    /// <summary>取最近停靠点对应的逻辑楼层（1 起算），用于皮带拓扑。</summary>
    private int ResolveLogicalFloor(int plcHeight)
    {
        int stopCount = GetFloorStopCount();
        if (stopCount <= 0)
            return 1;

        int best = 0;
        int bestDist = Mathf.Abs(GetFloorPlcHeight(0) - plcHeight);
        for (int i = 1; i < stopCount; i++)
        {
            int dist = Mathf.Abs(GetFloorPlcHeight(i) - plcHeight);
            if (dist < bestDist)
            {
                best = i;
                bestDist = dist;
            }
        }

        return best + 1;
    }

    private int GetFloorStopCount()
    {
        int count = 0;
        if (m_floorPlcHeights != null)
            count = Mathf.Max(count, m_floorPlcHeights.Length);
        if (m_floorMarkers != null)
            count = Mathf.Max(count, m_floorMarkers.Length);
        if (m_floorHeights != null)
            count = Mathf.Max(count, m_floorHeights.Length);
        if (m_floorBeltLinks != null)
            count = Mathf.Max(count, m_floorBeltLinks.Length);
        return count;
    }

    private int GetFloorPlcHeight(int index)
    {
        if (m_floorPlcHeights != null && index >= 0 && index < m_floorPlcHeights.Length)
            return m_floorPlcHeights[index];
        return index * DefaultPlcHeightStep;
    }

    private void KeepCargoOnLift()
    {
        if (Material == null) return;
        AttachToLift(Material, snapToAnchor: false);
    }

    private Transform ResolveLoadAnchor()
    {
        if (m_loadAnchor != null)
            return m_loadAnchor;
        return Lift;
    }

    private bool AttachToLift(Transform material, bool snapToAnchor)
    {
        if (material == null) return false;

        var anchor = ResolveLoadAnchor();
        bool newlyParented = material.parent != anchor;
        if (newlyParented)
            material.SetParent(anchor, true);

        if (snapToAnchor)
        {
            material.position = anchor.position + anchor.up * m_materialHeight;
            return true;
        }

        if (!newlyParented)
            return false;

        // 只对齐升降轴，保留水平位置，避免挂接时左右跳动
        var follow = anchor.position + anchor.up * m_materialHeight;
        material.position = SignedAxisUtil.WithComponent(
            material.position,
            m_liftDirection,
            SignedAxisUtil.GetComponent(follow, m_liftDirection));
        return true;
    }

    private bool DetachFromLift(Transform material)
    {
        if (material == null) return false;
        var anchor = ResolveLoadAnchor();
        if (material.parent != anchor && material.parent != Lift)
            return false;

        material.SetParent(null, true);
        return true;
    }

    private void CaptureBaseBeltSpeed()
    {
        if (_baseBeltSpeedCaptured) return;
        _baseBeltSpeed = Mathf.Max(0.01f, m_speed);
        _baseBeltSpeedCaptured = true;
    }

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

    private static void AbortTween(ref Tweener tweener)
    {
        if (tweener == null)
            return;

        tweener.Abort();
        tweener = null;
    }

    private static bool TryFind(List<PointData> part, string pointCode, out PointData point)
    {
        point = null;
        if (string.IsNullOrEmpty(pointCode) || part == null)
            return false;

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

    /// <summary>ActualSpeed：无符号转速（地址表 0–1500R），不按补码解析。</summary>
    private static int ReadActualSpeed(List<PointData> part, int fallback)
    {
        if (TryFind(part, PointActualSpeed, out var point))
            return ParseActualSpeed(point.Value, fallback);

        // PartConfig 中 ActualSpeed 为第 12 点（0-based）
        if (part != null && part.Count > 12 && part[12] != null)
            return ParseActualSpeed(part[12].Value, fallback);

        return fallback;
    }

    private static int ParseActualSpeed(string raw, int fallback)
    {
        if (string.IsNullOrEmpty(raw))
            return fallback;

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
            return Mathf.Abs(v);

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
            return (int)Math.Min(int.MaxValue, Math.Abs(l));

        if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
            return Mathf.Abs(Mathf.RoundToInt(f));

        return fallback;
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

    // -------------------------------------------------------------------------
    // 调试测试
    // -------------------------------------------------------------------------

    [Button("瞬移到测试高度")]
    [ContextMenu("Lifter/瞬移到测试高度")]
    private void TestSnapToFloor()
    {
        EnsureTestReady();
        _plcHeight = m_testPlcHeight;
        _logicalFloor = ResolveLogicalFloor(m_testPlcHeight);
        SyncFloorBeltLinks(_logicalFloor);
        ApplyPlcHeight(m_testPlcHeight, snap: true);
        Debug.Log(
            $"[LifterPartMotion] 瞬移到 PLC 高度={m_testPlcHeight} → 逻辑层={_logicalFloor} lift={Lift.position}",
            this);
    }

    [Button("升降步进 +高度")]
    [ContextMenu("Lifter/升降步进 +高度")]
    private void TestLiftStepPlus() => TestLiftStep(+Mathf.Max(1, m_testStep));

    [Button("升降步进 -高度")]
    [ContextMenu("Lifter/升降步进 -高度")]
    private void TestLiftStepMinus() => TestLiftStep(-Mathf.Max(1, m_testStep));

    [Button("应用测试皮带信号")]
    [ContextMenu("Lifter/应用测试皮带信号")]
    private void TestApplySpeed()
    {
        EnsureTestReady();
        _processStatus = m_testProcessStatus;
        if (_plcHeight == int.MinValue)
        {
            _plcHeight = m_testPlcHeight;
            _logicalFloor = ResolveLogicalFloor(_plcHeight);
        }
        SyncFloorBeltLinks(_logicalFloor);
        MapProcessToBelt(m_testProcessStatus, m_testActualSpeed, out bool forward, out bool reverse);
        ApplyMotionSignals(forward, reverse, IsLoad || Material != null);
    }

    private void TestLiftStep(int deltaHeight)
    {
        EnsureTestReady();
        int plcHeight = (_plcHeight == int.MinValue ? m_testPlcHeight : _plcHeight) + deltaHeight;
        m_testPlcHeight = plcHeight;
        _plcHeight = plcHeight;
        _logicalFloor = ResolveLogicalFloor(plcHeight);
        SyncFloorBeltLinks(_logicalFloor);
        ApplyPlcHeight(plcHeight, snap: false);
        Debug.Log(
            $"[LifterPartMotion] 升降步进 Δ={deltaHeight} → plcH={plcHeight} 逻辑层={_logicalFloor} pos={Lift.position}",
            this);
    }

    private void EnsureTestReady()
    {
        _lift = m_liftAxis != null ? m_liftAxis : transform;
        CaptureBaseBeltSpeed();
    }

#if UNITY_EDITOR
    public override ConveyorPartMotion EditorGetForwardNext(int floorPlc) =>
        GetFloorLink(floorPlc)?.forwardNext;

    public override ConveyorPartMotion EditorGetReverseNext(int floorPlc) =>
        GetFloorLink(floorPlc)?.reverseNext;

    public override void EditorSetNeighbor(ConveyorPartMotion next, bool forward, bool writeOpposite, int floorPlc)
    {
        var link = GetFloorLink(floorPlc);
        var fwd = link != null ? link.forwardNext : null;
        var rev = link != null ? link.reverseNext : null;
        bool invert = link != null && link.invertSpeedSign;
        if (forward) fwd = next;
        else rev = next;
        EditorSetFloorBeltLink(floorPlc, fwd, rev, invert);
        if (writeOpposite && next != null)
            next.EditorSetOppositeIfEmpty(this, forward, floorPlc);
    }

    public override void EditorSetOppositeIfEmpty(ConveyorPartMotion other, bool otherSetForward, int floorPlc)
    {
        if (other == null) return;
        var link = GetFloorLink(floorPlc);
        if (otherSetForward)
        {
            if (link != null && link.reverseNext != null && link.reverseNext != other)
                return;
            EditorSetNeighbor(other, forward: false, writeOpposite: false, floorPlc);
        }
        else
        {
            if (link != null && link.forwardNext != null && link.forwardNext != other)
                return;
            EditorSetNeighbor(other, forward: true, writeOpposite: false, floorPlc);
        }
    }

    public override void EditorClearLinkTo(ConveyorPartMotion other, int floorPlc)
    {
        if (other == null) return;
        var link = GetFloorLink(floorPlc);
        if (link == null) return;
        if (link.forwardNext != other && link.reverseNext != other) return;

        EditorSetFloorBeltLink(
            floorPlc,
            link.forwardNext == other ? null : link.forwardNext,
            link.reverseNext == other ? null : link.reverseNext,
            link.invertSpeedSign);
    }

    public void EditorSetFloorBeltLink(int floorPlc, ConveyorPartMotion forward, ConveyorPartMotion reverse, bool invert = false)
    {
        if (floorPlc < 1) return;
        int index = floorPlc - 1;
        if (m_floorBeltLinks == null || m_floorBeltLinks.Length <= index)
        {
            var resized = new FloorBeltLink[index + 1];
            if (m_floorBeltLinks != null)
                Array.Copy(m_floorBeltLinks, resized, m_floorBeltLinks.Length);
            for (int i = 0; i < resized.Length; i++)
                resized[i] ??= new FloorBeltLink();
            m_floorBeltLinks = resized;
        }

        m_floorBeltLinks[index] ??= new FloorBeltLink();
        UnityEditor.Undo.RecordObject(this, "Set Lifter Floor Belt Link");
        m_floorBeltLinks[index].forwardNext = forward;
        m_floorBeltLinks[index].reverseNext = reverse;
        m_floorBeltLinks[index].invertSpeedSign = invert;
        if (_logicalFloor == floorPlc || _plcHeight == int.MinValue)
            SyncFloorBeltLinks(floorPlc);
        UnityEditor.EditorUtility.SetDirty(this);
    }

    protected override void DrawStationGizmos(bool selected)
    {
        base.DrawStationGizmos(selected);

        var lift = Lift;
        float size = selected ? 0.2f : 0.15f;
        Gizmos.color = new Color(1f, 0.92f, 0.15f, 1f);
        Gizmos.DrawWireCube(lift.position, Vector3.one * size);
        Gizmos.color = new Color(1f, 0.92f, 0.15f, 0.35f);
        Gizmos.DrawCube(lift.position, Vector3.one * (size * 0.55f));

        if (m_floorMarkers == null) return;
        int idx = _logicalFloor - 1;
        if (idx < 0 || idx >= m_floorMarkers.Length || m_floorMarkers[idx] == null) return;

        var floorPos = m_floorMarkers[idx].position;
        Gizmos.color = new Color(0.2f, 1f, 0.95f, 1f);
        Gizmos.DrawWireSphere(floorPos, selected ? 0.14f : 0.11f);
        UnityEditor.Handles.color = new Color(0.2f, 1f, 0.95f, 0.85f);
        UnityEditor.Handles.DrawAAPolyLine(selected ? 5f : 3.5f, lift.position, floorPos);
    }
#endif
}
}
