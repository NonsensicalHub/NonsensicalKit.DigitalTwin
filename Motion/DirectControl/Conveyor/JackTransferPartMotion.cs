using System.Collections.Generic;
using System.Globalization;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.Tools;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{

/// <summary>
/// 顶升移栽工位：普通输送（接货、停靠、正反转下传）+ 顶升换线。
/// <list type="bullet">
/// <item>顶起：从 <see cref="PairedStation"/> 抢走已停靠货物，挂到顶升平台</item>
/// <item>降下：落下到位后把货物交还给配对输送线</item>
/// <item>升起到位后仍可按正/反转走普通下游下传（换向移栽）</item>
/// </list>
/// </summary>
public class JackTransferPartMotion : ConveyorPartMotion
{
    private const string PointLiftUp = "LiftUp_FlipUp";
    private const string PointLiftDown = "LiftDown_FlipDown";
    private const string PointLiftUpInPos = "LiftUpInPos_FlipUpInPos";
    private const string PointLiftDownInPos = "LiftDownInPos_FlipDownInPos";

    [Header("顶升")]
    [Tooltip("顶升平台；为空则移动本物体")]
    [SerializeField] private Transform m_liftAxis;
    [Tooltip("升起时相对初始位置沿升降轴的偏移（米）")]
    [SerializeField] private float m_liftOffset = 0.15f;
    [SerializeField] private SignedAxis m_liftDirection = SignedAxis.PositiveY;
    [SerializeField] private float m_liftSpeed = 0.35f;

    [Header("配对输送线")]
    [Tooltip("升起时从该工位抢货；降下时把货交还给该工位。为空则尝试同物体上其它 ConveyorPartMotion")]
    [SerializeField] private ConveyorPartMotion m_pairedStation;

    private Transform _lift;
    private Vector3 _liftOrigin;
    private bool _liftOriginCaptured;
    private Tweener _liftTweener;
    private bool _first = true;
    private int _lastPairHandOffFailFrame = int.MinValue;

    private bool _liftUp;
    private bool _liftDown;
    private bool _liftUpInPos;
    private bool _liftDownInPos;

    public ConveyorPartMotion PairedStation => m_pairedStation;

    /// <summary>升起途中，正从配对输送线抢货（抑制下传、待停靠后取走）。</summary>
    public bool IsTakingFromPaired =>
        m_pairedStation != null && m_pairedStation != this && WantRaised && !WantLowered;

    /// <summary>货物应挂在顶升平台上：升起全程，以及尚未落到低位的降下过程。</summary>
    private bool ShouldCargoFollowLift =>
        Material != null && (WantRaised && !WantLowered || WantLowered && !IsLiftLoweredEnough);

    private Transform Lift => _lift != null ? _lift : transform;

    private bool WantRaised => _liftUpInPos || (_liftUp && !_liftDownInPos);
    private bool WantLowered => _liftDownInPos || (_liftDown && !_liftUpInPos);

    private bool IsLiftRaisedEnough
    {
        get
        {
            CaptureLiftOrigin();
            float bas = SignedAxisUtil.GetComponent(_liftOrigin, m_liftDirection);
            float cur = SignedAxisUtil.GetComponent(Lift.position, m_liftDirection);
            return cur >= bas + m_liftOffset * 0.85f;
        }
    }

    private bool IsLiftLoweredEnough
    {
        get
        {
            CaptureLiftOrigin();
            float bas = SignedAxisUtil.GetComponent(_liftOrigin, m_liftDirection);
            float cur = SignedAxisUtil.GetComponent(Lift.position, m_liftDirection);
            return cur <= bas + m_liftOffset * 0.15f;
        }
    }

    /// <summary>
    /// 降下且已配对时改走配对交还；升起途中暂缓下传，升/落到位后才允许交给下游。
    /// </summary>
    protected override bool CanTransferDownstream
    {
        get
        {
            if (m_pairedStation != null && WantLowered)
                return false;
            if (!base.CanTransferDownstream)
                return false;
            if (WantRaised && !WantLowered)
                return IsLiftRaisedEnough;
            if (WantLowered)
                return IsLiftLoweredEnough;
            return true;
        }
    }

    /// <summary>顶升交权走配对抢货/交还，不按光电空窗往下游滑。</summary>
    protected override bool ShouldCoastDrive => false;

    /// <summary>货挂在升降台上时不要按输送面高度往下拽。</summary>
    protected override bool ShouldStickToBeltHeight => !ShouldCargoFollowLift;

    protected override bool CanAcceptMaterial => true;

    protected override void Init()
    {
        base.Init();
        _lift = m_liftAxis != null ? m_liftAxis : transform;
        CaptureLiftOrigin();
        _first = true;
        ResolvePairedStation();
    }

    protected override void Dispose()
    {
        ClearPairedSuppress();
        AbortTween(ref _liftTweener);
        base.Dispose();
    }

    protected override void Update()
    {
        // 升/降途中不要走皮带吸回地面停靠点，否则箱子会在平台上滑偏
        bool liftTraveling = ShouldCargoFollowLift && !CanTransferDownstream;
        if (!liftTraveling)
            base.Update();

        KeepCargoOnLift();

        if (Material != null && CanTransferDownstream && (ForwardRun || ReverseRun)
            && !IsMaterialMoving && !_waitingHandoff)
            EnsureMaterialMotionWhileRunning();

        SyncPairedExchange();
    }

    protected override void OnParseExtraSignals(List<PointData> part)
    {
        var prevUp = _liftUp;
        var prevDown = _liftDown;
        var prevUpIn = _liftUpInPos;
        var prevDownIn = _liftDownInPos;

        _liftUp = ReadBool(part, PointLiftUp, 5, _liftUp);
        _liftDown = ReadBool(part, PointLiftDown, 6, _liftDown);
        _liftUpInPos = ReadBool(part, PointLiftUpInPos, 7, _liftUpInPos);
        _liftDownInPos = ReadBool(part, PointLiftDownInPos, 8, _liftDownInPos);

        if (m_enableDiagLog &&
            (prevUp != _liftUp || prevDown != _liftDown || prevUpIn != _liftUpInPos || prevDownIn != _liftDownInPos))
        {
            DiagLog("LIFT_SIGNAL",
                $"up={Bool01(prevUp)}->{Bool01(_liftUp)} down={Bool01(prevDown)}->{Bool01(_liftDown)} " +
                $"upIn={Bool01(prevUpIn)}->{Bool01(_liftUpInPos)} downIn={Bool01(prevDownIn)}->{Bool01(_liftDownInPos)} | " +
                DiagStateSnapshot());
        }
    }

    protected override void OnAfterReceiveData(bool isLoad)
    {
        bool snap = _first;
        _first = false;

        bool raise = WantRaised && !WantLowered;

        // 先挂货再动平台；第一次挂上时归位到顶升停靠点
        if (ShouldCargoFollowLift)
        {
            bool firstAttach = Material.parent != Lift;
            if (AttachToLift(Material, snapToAnchor: firstAttach))
                DiagLog("ATTACH_LIFT", DiagStateSnapshot());
        }

        ApplyLiftTarget(raise, snap);

        if (Material != null && WantLowered && IsLiftLoweredEnough)
        {
            if (DetachFromLift(Material))
                DiagLog("DETACH_LIFT", DiagStateSnapshot());
        }

        SyncPairedExchange();
    }

    protected override void OnIdleEmpty()
    {
        // 升降只跟点位走；无货时不要强行落下（过路顶起如 LR1032 会保持举升）
        if (WantRaised && !WantLowered)
            return;
        ClearPairedSuppress();
    }

    protected override void TryClaimMaterialBeforeCreate()
    {
        SyncPairedExchange();
    }

    protected override Vector3 GetCreateSpawnPoint()
    {
        if (WantRaised && !WantLowered)
            return MaterialPoint;
        return base.GetCreateSpawnPoint();
    }

    protected override bool TryFindUpstreamHoldingMaterial(out ConveyorPartMotion upstream)
    {
        ResolvePairedStation();
        if (m_pairedStation != null && m_pairedStation != this && m_pairedStation.HasMaterial)
        {
            upstream = m_pairedStation;
            return true;
        }

        return base.TryFindUpstreamHoldingMaterial(out upstream);
    }

    protected override bool ShouldRecycleWhenEmpty()
    {
        if (Material == null)
            return false;
        if (WantRaised && !WantLowered)
            return false;
        if (WantLowered)
            return false;
        return base.ShouldRecycleWhenEmpty();
    }

    protected override void OnMaterialAccepted(Transform material)
    {
        if (ShouldCargoFollowLift && AttachToLift(material, snapToAnchor: true))
            DiagLog("ATTACH_LIFT", "on accept | " + DiagStateSnapshot());
    }

    protected override void OnMaterialParked()
    {
        if (Material == null) return;
        if (ShouldCargoFollowLift)
        {
            if (AttachToLift(Material, snapToAnchor: true))
                DiagLog("ATTACH_LIFT", "on parked | " + DiagStateSnapshot());
            return;
        }

        SnapMaterialToPark();
    }

    private void SyncPairedExchange()
    {
        ResolvePairedStation();
        if (m_pairedStation == null || m_pairedStation == this)
            return;

        bool raise = WantRaised && !WantLowered;
        if (raise)
        {
            m_pairedStation.SetTransferSuppressed(true);
            TryTakeFromPaired();
            return;
        }

        if (WantLowered)
        {
            TryHandOffToPaired();
            if (Material == null)
                ClearPairedSuppress();
            return;
        }

        if (Material == null)
            ClearPairedSuppress();
    }

    /// <summary>顶起时从配对输送线抢走已归位货物，并落到顶升停靠点。</summary>
    private void TryTakeFromPaired()
    {
        if (Material != null || m_pairedStation == null)
            return;
        if (!m_pairedStation.HasMaterial)
            return;
        if (!m_pairedStation.IsAtPark)
            return;
        if (!m_pairedStation.TryTakeAwayMaterial(out var material) || material == null)
            return;

        Material = material;
        if (Material.parent != null && Material.parent != Lift)
            Material.SetParent(null, true);

        Parked = true;
        AttachToLift(Material, snapToAnchor: true);
        StopBeltDrive();
        DisarmEmptyRecycle();
        DiagLog("PAIR_TAKE",
            $"from={StationKeyOf(m_pairedStation)} mat={material.name} | {DiagStateSnapshot()}");
    }

    /// <summary>降下到位后把货物交还给配对输送线，并归位到对方停靠点。</summary>
    private void TryHandOffToPaired()
    {
        if (m_pairedStation == null || m_pairedStation == this)
            return;
        if (!WantLowered || !IsLiftLoweredEnough)
            return;
        if (Material == null || IsMaterialMoving || !Parked)
            return;

        var material = Material;
        var pairKey = StationKeyOf(m_pairedStation);
        DetachFromLift(material);
        SnapToPairedPark(m_pairedStation, material);
        if (!m_pairedStation.TryAcceptMaterial(material, snapToPark: true))
        {
            if (Time.frameCount - _lastPairHandOffFailFrame >= 30)
            {
                _lastPairHandOffFailFrame = Time.frameCount;
                DiagLog("PAIR_HANDOFF_FAIL",
                    $"to={pairKey} mat={material.name} | {DiagStateSnapshot()}");
            }

            return;
        }

        Material = null;
        Parked = false;
        StopBeltDrive();
        ClearPairedSuppress();
        DiagLog("PAIR_HANDOFF_OK",
            $"to={pairKey} mat={material.name} | {DiagStateSnapshot()}");
    }

    private void ResolvePairedStation()
    {
        if (m_pairedStation != null && m_pairedStation != this)
            return;

        var stations = GetComponents<ConveyorPartMotion>();
        for (int i = 0; i < stations.Length; i++)
        {
            var s = stations[i];
            if (s != null && s != this)
            {
                m_pairedStation = s;
                DiagLog("PAIR_RESOLVE", $"auto={StationKeyOf(m_pairedStation)}");
                return;
            }
        }
    }

    private void ClearPairedSuppress()
    {
        if (m_pairedStation != null && m_pairedStation != this)
            m_pairedStation.SetTransferSuppressed(false);
    }

    private void KeepCargoOnLift()
    {
        if (!ShouldCargoFollowLift) return;
        AttachToLift(Material, snapToAnchor: false);
    }

    private bool AttachToLift(Transform material, bool snapToAnchor = true)
    {
        if (material == null) return false;

        var anchor = Lift;
        bool newlyParented = material.parent != anchor;
        if (newlyParented)
            material.SetParent(anchor, true);

        if (snapToAnchor)
        {
            material.position = GetLiftCargoWorldPoint();
            return true;
        }

        if (!newlyParented)
            return false;

        // 只对齐升降轴，保留水平位置，避免半空脱挂后再挂时左右跳动
        var follow = GetLiftCargoWorldPoint();
        material.position = SignedAxisUtil.WithComponent(
            material.position,
            m_liftDirection,
            SignedAxisUtil.GetComponent(follow, m_liftDirection));
        return true;
    }

    /// <summary>顶升停靠点：水平用本工位 MaterialPoint，高度跟平台走。</summary>
    private Vector3 GetLiftCargoWorldPoint()
    {
        var dock = MaterialPoint;
        var liftPose = Lift.position + Lift.up * m_materialHeight;
        return SignedAxisUtil.WithComponent(
            dock,
            m_liftDirection,
            SignedAxisUtil.GetComponent(liftPose, m_liftDirection));
    }

    private static void SnapToPairedPark(ConveyorPartMotion paired, Transform material)
    {
        if (paired == null || material == null) return;
        material.position = paired.MaterialPoint;
    }

    private bool DetachFromLift(Transform material)
    {
        if (material == null) return false;
        if (material.parent != Lift) return false;

        material.SetParent(null, true);
        return true;
    }

    private void ApplyLiftTarget(bool raised, bool snap)
    {
        CaptureLiftOrigin();

        float baseValue = SignedAxisUtil.GetComponent(_liftOrigin, m_liftDirection);
        float target = raised ? baseValue + m_liftOffset : baseValue;

        Transform lift = Lift;
        float current = SignedAxisUtil.GetComponent(lift.position, m_liftDirection);
        if (Mathf.Abs(current - target) < 1e-4f)
            return;

        AbortTween(ref _liftTweener);

        if (snap || m_liftSpeed <= 0f)
        {
            lift.position = SignedAxisUtil.WithComponent(lift.position, m_liftDirection, target);
            return;
        }

        _liftTweener = DoAxisWorldMove(lift, m_liftDirection, target, m_liftSpeed);
    }

    private void CaptureLiftOrigin()
    {
        if (_liftOriginCaptured) return;
        _lift = m_liftAxis != null ? m_liftAxis : transform;
        _liftOrigin = Lift.position;
        _liftOriginCaptured = true;
    }

    private void SetLiftSignalsForTest(bool raised)
    {
        _lift = m_liftAxis != null ? m_liftAxis : transform;
        CaptureLiftOrigin();
        _liftUp = raised;
        _liftUpInPos = raised;
        _liftDown = !raised;
        _liftDownInPos = !raised;
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
        if (tweener == null) return;
        tweener.Abort();
        tweener = null;
    }

    private static bool TryFind(List<PointData> part, string pointCode, out PointData point)
    {
        point = null;
        if (string.IsNullOrEmpty(pointCode) || part == null) return false;

        for (int i = 0; i < part.Count; i++)
        {
            var p = part[i];
            if (p == null) continue;
            if (p.Point == pointCode || p.Name == pointCode)
            {
                point = p;
                return true;
            }
        }

        return false;
    }

    private static bool ReadBool(List<PointData> part, string pointCode, int fallbackIndex, bool fallbackValue)
    {
        if (!string.IsNullOrEmpty(pointCode) && TryFind(part, pointCode, out var point))
            return ParseBool(point.Value, fallbackValue);

        if (fallbackIndex >= 0 && fallbackIndex < part.Count && part[fallbackIndex] != null)
            return ParseBool(part[fallbackIndex].Value, fallbackValue);

        return fallbackValue;
    }

    private static bool ParseBool(string raw, bool fallback)
    {
        if (string.IsNullOrEmpty(raw)) return fallback;
        if (bool.TryParse(raw, out bool b)) return b;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            return n != 0;
        return fallback;
    }

    private static List<PointDataInfo> LiftPointInfos() =>
        new List<PointDataInfo>
        {
            new PointDataInfo("输送正转状态", PointDataType.Bool, false),
            new PointDataInfo("输送反转状态", PointDataType.Bool, false),
            new PointDataInfo("有货", PointDataType.Bool, false),
            new PointDataInfo("举升上升状态", PointDataType.Bool, false),
            new PointDataInfo("举升下降状态", PointDataType.Bool, false),
            new PointDataInfo("举升上到位", PointDataType.Bool, false),
            new PointDataInfo("举升下到位", PointDataType.Bool, false),
        };

    protected override string DiagStateSnapshot()
    {
        return base.DiagStateSnapshot()
            + $" liftUp={Bool01(_liftUp)} liftDown={Bool01(_liftDown)}"
            + $" upIn={Bool01(_liftUpInPos)} downIn={Bool01(_liftDownInPos)}"
            + $" raised={Bool01(IsLiftRaisedEnough)} lowered={Bool01(IsLiftLoweredEnough)}"
            + $" paired={StationKeyOf(m_pairedStation)}";
    }

    protected override PartDataInfo GetInfo() =>
        new PartDataInfo("顶升移栽机", m_partID, LiftPointInfos());

    [Button("测试抢货并顶升")]
    [ContextMenu("JackTransfer/测试抢货并顶升")]
    private void TestTakeAndRaise()
    {
        ResolvePairedStation();
        SetLiftSignalsForTest(raised: true);
        ApplyLiftTarget(true, snap: false);
        if (m_pairedStation != null)
            m_pairedStation.SetTransferSuppressed(true);
        TryTakeFromPaired();
        if (Material != null)
            AttachToLift(Material, snapToAnchor: true);
    }

    [Button("测试落下并交还")]
    [ContextMenu("JackTransfer/测试落下并交还")]
    private void TestLowerAndReturn()
    {
        ResolvePairedStation();
        SetLiftSignalsForTest(raised: false);
        ApplyLiftTarget(false, snap: false);
        if (Material != null)
            AttachToLift(Material, snapToAnchor: false);
        TryHandOffToPaired();
    }

#if UNITY_EDITOR
    public void EditorSetPairedStation(ConveyorPartMotion paired)
    {
        if (m_pairedStation == paired) return;
        UnityEditor.Undo.RecordObject(this, "Set Paired Station");
        m_pairedStation = paired;
        UnityEditor.EditorUtility.SetDirty(this);
    }

    protected override void DrawStationGizmos(bool selected)
    {
        base.DrawStationGizmos(selected);

        var lift = Lift;
        // 顶升标记抬到平台上方，避免和托盘/货物叠在一起
        float raise = selected ? 0.42f : 0.32f;
        var mark = lift.position + Vector3.up * raise;
        float size = selected ? 0.22f : 0.17f;
        var raisedColor = new Color(1f, 0.05f, 0.95f, 1f);
        var loweredColor = new Color(1f, 0.45f, 0.95f, 1f);

        // 平台 → 标记竖杆
        UnityEditor.Handles.color = new Color(0f, 0f, 0f, 0.9f);
        UnityEditor.Handles.DrawAAPolyLine(6f, lift.position, mark);
        UnityEditor.Handles.color = IsLiftRaisedEnough ? raisedColor : loweredColor;
        UnityEditor.Handles.DrawAAPolyLine(3f, lift.position, mark);

        if (IsLiftRaisedEnough)
        {
            Gizmos.color = new Color(0f, 0f, 0f, 0.95f);
            Gizmos.DrawCube(mark, Vector3.one * (size * 1.35f));
            Gizmos.color = raisedColor;
            Gizmos.DrawCube(mark, Vector3.one * size);
            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.DrawWireCube(mark, Vector3.one * (size * 1.55f));
            UnityEditor.Handles.color = raisedColor;
            UnityEditor.Handles.DrawWireCube(mark, Vector3.one * (size * 1.85f));
        }
        else
        {
            Gizmos.color = new Color(0f, 0f, 0f, 0.85f);
            Gizmos.DrawWireCube(mark, Vector3.one * (size * 1.25f));
            UnityEditor.Handles.color = loweredColor;
            UnityEditor.Handles.DrawWireCube(mark, Vector3.one * size);
            UnityEditor.Handles.DrawWireCube(mark, Vector3.one * (size * 1.45f));
        }

        if (IsTakingFromPaired)
        {
            var tip = mark + Vector3.up * (selected ? 0.28f : 0.22f);
            Gizmos.color = new Color(0f, 0f, 0f, 0.95f);
            Gizmos.DrawSphere(tip, selected ? 0.12f : 0.1f);
            Gizmos.color = new Color(1f, 0.95f, 0.05f, 1f);
            Gizmos.DrawSphere(tip, selected ? 0.09f : 0.075f);
            UnityEditor.Handles.color = Color.black;
            UnityEditor.Handles.DrawWireDisc(tip, Vector3.up, selected ? 0.16f : 0.13f);
            UnityEditor.Handles.color = new Color(1f, 0.95f, 0.05f, 1f);
            UnityEditor.Handles.DrawWireDisc(tip, Vector3.up, selected ? 0.2f : 0.16f);
        }
    }
#endif
}
}
