using System.Collections.Generic;
using System.Text;
using NonsensicalKit.DigitalTwin.Motion;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{

/// <summary>
/// 普通输送工位（逻辑驱动，非物理仿真）：有货生成/回收、正反转下传。
/// 物料由本工位按皮带速度平移；有下游时沿两工位停靠点连线方向驶向交界切权点
/// （间隙按 <c>centerDist * lenA / (lenA + lenB)</c> 分摊），避免工位左右误差把货物瞬时吸回配置轴向。
/// 正反转均关闭时立刻原地刹停。
/// 顶升移栽 <see cref="JackTransferPartMotion"/> 继承此类并扩展换线逻辑。
/// PLC：part[2] 正转，part[3] 反转，part[4] 有货（传输机经占位填充后）。
/// </summary>
public class ConveyorPartMotion : PartMotionBase
{
    [Header("Motion")]
    [Tooltip("输送正向轴向（正转沿此方向，反转取反）")]
    [SerializeField] protected Dir m_forwardAxis;
    [SerializeField] protected float m_speed = 1f;
    [Tooltip("开启后用正转轴向对应的 localScale 分量作为有效长度（X/XI→x，Y/YI→y，Z/ZI→z）")]
    [SerializeField] protected bool m_useForwardScaleAsLength;
    [Tooltip("本段输送线有效长度（米），用于与下游按比例计算交界切权点；开启「轴向缩放作长度」时忽略")]
    [SerializeField, Min(0.01f)] protected float m_length = 1.5f;
    [Tooltip("物料相对工位的高度偏移")]
    [SerializeField] protected float m_materialHeight = 0.1f;
    [Tooltip("有货上升沿生成点相对停靠点的上游偏移（米）")]
    [SerializeField] protected float m_entryDistance = 1.5f;
    [Tooltip("停靠点相对物体原点沿正转方向的偏移（米）")]
    [SerializeField] protected float m_parkOffset;

    [Header("Topology")]
    [Tooltip("正转时的下游工位（ConveyorPartMotion / 顶升移栽机等）")]
    [SerializeField] protected ConveyorPartMotion m_forwardNext;
    [Tooltip("反转时的下游工位（ConveyorPartMotion / 顶升移栽机等）")]
    [SerializeField] protected ConveyorPartMotion m_reverseNext;

    [Header("Label")]
    [SerializeField, InspectorName("显示标签")]
    protected bool m_showLabel = true;
    [SerializeField, InspectorName("标签偏移")]
    protected Vector3 m_labelOffset = new Vector3(0f, 0.5f, 0f);
    [SerializeField, InspectorName("标签颜色")]
    protected Color m_labelColor = Color.yellow;
    [SerializeField, InspectorName("字体大小"), Min(8)]
    protected int m_fontSize = 12;

    [Header("诊断日志")]
    [Tooltip("开启后写入 项目根/Log/{会话}.log（全线共用一个文件）")]
    [SerializeField] protected bool m_enableDiagLog;

    public bool ShowLabel => m_showLabel;
    public Vector3 LabelOffset => m_labelOffset;
    public Color LabelColor => m_labelColor;
    public int FontSize => m_fontSize;
    public Vector3 LabelWorldPosition => transform.position + m_labelOffset;

    /// <summary>本段输送线有效长度（米）。开启轴向缩放作长度时取正转轴向对应 localScale。</summary>
    public float Length => m_useForwardScaleAsLength
        ? Mathf.Max(0.01f, GetForwardAxisLocalScale())
        : Mathf.Max(0.01f, m_length);

    public bool UseForwardScaleAsLength => m_useForwardScaleAsLength;

    protected Transform Material;

    protected bool IsLoad;
    protected bool ForwardRun;
    protected bool ReverseRun;
    protected bool Parked;

    private bool _transferSuppressed;
    private ConveyorMotionDiagLog _diagLog;
    private int _lastXferRejectFrame = int.MinValue;
    private bool _beltDriving;
    protected bool _waitingHandoff;
    /// <summary>有货上升沿因上游持货跳过创建；上游清空后补建。</summary>
    private bool _createDeferred;
    /// <summary>光电已灭且已停转；下一帧仍无人取走才回收，避免同帧顶升抢货前被销掉。</summary>
    protected bool _emptyRecycleArmed;
    /// <summary>武装回收的帧号；下一帧起由 Update 补完成，不依赖 MQTT 再推一包。</summary>
    private int _emptyRecycleArmFrame = -1;

    protected bool IsMaterialMoving => _beltDriving && Material != null;

    public bool HasMaterial => Material != null;

    public bool IsParked => Parked && Material != null && !IsMaterialMoving;

    /// <summary>当前运行方向对应的下游；正反同时开时优先正转。</summary>
    protected ConveyorPartMotion CurrentDownstream
    {
        get
        {
            if (ForwardRun) return m_forwardNext;
            if (ReverseRun) return m_reverseNext;
            return null;
        }
    }

    /// <summary>外部设备（如顶升移栽）抑制本工位自动下传，避免货物被提前送走。</summary>
    public void SetTransferSuppressed(bool suppressed)
    {
        if (_transferSuppressed == suppressed) return;
        _transferSuppressed = suppressed;
        DiagLog("SUPPRESS", $"value={Bool01(suppressed)} | {DiagStateSnapshot()}");
    }

    public ConveyorPartMotion ForwardNext => m_forwardNext;
    public ConveyorPartMotion ReverseNext => m_reverseNext;

    /// <summary>正转世界轴向（编辑器与调试可视化可用）。</summary>
    public Vector3 ForwardWorldDirection => ForwardWorldAxis;

#if UNITY_EDITOR
    public Dir EditorForwardAxis
    {
        get => m_forwardAxis;
        set
        {
            if (m_forwardAxis == value) return;
            UnityEditor.Undo.RecordObject(this, "Set Station Forward Axis");
            m_forwardAxis = value;
            UnityEditor.EditorUtility.SetDirty(this);
        }
    }

    public bool EditorUseForwardScaleAsLength
    {
        get => m_useForwardScaleAsLength;
        set
        {
            if (m_useForwardScaleAsLength == value) return;
            UnityEditor.Undo.RecordObject(this, "Set Use Forward Scale As Length");
            m_useForwardScaleAsLength = value;
            UnityEditor.EditorUtility.SetDirty(this);
        }
    }

    public void EditorApplyForwardScaleToLength()
    {
        UnityEditor.Undo.RecordObject(this, "Apply Forward Scale To Length");
        ApplyForwardScaleToLength();
        UnityEditor.EditorUtility.SetDirty(this);
    }

    public void EditorSetNeighbor(ConveyorPartMotion next, bool forward, bool writeOpposite) =>
        EditorSetNeighbor(next, forward, writeOpposite, floorPlc: 1);

    public virtual void EditorSetNeighbor(ConveyorPartMotion next, bool forward, bool writeOpposite, int floorPlc)
    {
        ApplyNeighborLink(next, forward, writeOpposite: false);
        if (writeOpposite && next != null)
            next.EditorSetOppositeIfEmpty(this, forward, floorPlc);
    }

    /// <summary>对侧把本工位写成反向下游；仅在空位或已指向对方时写入。</summary>
    public virtual void EditorSetOppositeIfEmpty(ConveyorPartMotion other, bool otherSetForward, int floorPlc)
    {
        if (other == null) return;
        if (otherSetForward)
        {
            if (m_reverseNext == null || m_reverseNext == other)
                ApplyNeighborLink(other, forward: false, writeOpposite: false);
        }
        else
        {
            if (m_forwardNext == null || m_forwardNext == other)
                ApplyNeighborLink(other, forward: true, writeOpposite: false);
        }
    }

    public virtual ConveyorPartMotion EditorGetForwardNext(int floorPlc) => m_forwardNext;

    public virtual ConveyorPartMotion EditorGetReverseNext(int floorPlc) => m_reverseNext;

    /// <summary>清除本工位指向 other 的正转/反转引用（不改 other 侧）。</summary>
    public void EditorClearLinkTo(ConveyorPartMotion other) => EditorClearLinkTo(other, 1);

    public virtual void EditorClearLinkTo(ConveyorPartMotion other, int floorPlc)
    {
        if (other == null) return;
        if (m_forwardNext != other && m_reverseNext != other) return;

        UnityEditor.Undo.RecordObject(this, "Clear Station Neighbor Link");
        if (m_forwardNext == other) m_forwardNext = null;
        if (m_reverseNext == other) m_reverseNext = null;
        UnityEditor.EditorUtility.SetDirty(this);
    }

    /// <summary>前后工位互相引用，删除时两端一起清。</summary>
    public static void EditorClearMutualNeighbor(ConveyorPartMotion a, ConveyorPartMotion b) =>
        EditorClearMutualNeighbor(a, b, 1);

    public static void EditorClearMutualNeighbor(ConveyorPartMotion a, ConveyorPartMotion b, int floorPlc)
    {
        if (a == null || b == null || a == b) return;
        a.EditorClearLinkTo(b, floorPlc);
        b.EditorClearLinkTo(a, floorPlc);
    }

    public void EditorAutoBindForward() => BindForwardNext();
    public void EditorAutoBindReverse() => BindReverseNext();
    public void EditorAutoBindBoth() => BindForwardAndReverseNext();

    /// <summary>仅在对应下游为空（或 overwrite）时自动绑定。</summary>
    public bool EditorTryAutoBindForward(bool overwrite) =>
        EditorTryAutoBindForward(overwrite, null, 1);

    public bool EditorTryAutoBindForward(bool overwrite, IList<ConveyorPartMotion> candidates, int floorPlc)
    {
        if (!overwrite && EditorGetForwardNext(floorPlc) != null) return false;
        var next = FindNeighbor(ForwardWorldAxis, candidates);
        if (next == null) return false;
        EditorSetNeighbor(next, forward: true, writeOpposite: true, floorPlc);
        return true;
    }

    public bool EditorTryAutoBindReverse(bool overwrite) =>
        EditorTryAutoBindReverse(overwrite, null, 1);

    public bool EditorTryAutoBindReverse(bool overwrite, IList<ConveyorPartMotion> candidates, int floorPlc)
    {
        if (!overwrite && EditorGetReverseNext(floorPlc) != null) return false;
        var next = FindNeighbor(-ForwardWorldAxis, candidates);
        if (next == null) return false;
        EditorSetNeighbor(next, forward: false, writeOpposite: true, floorPlc);
        return true;
    }

    /// <summary>在本地六向中选取与目标世界方向最接近的 <see cref="Dir"/>。</summary>
    public static Dir PickDirMatchingWorld(Transform t, Vector3 worldDir)
    {
        if (t == null || worldDir.sqrMagnitude < 1e-8f)
            return Dir.Z;

        worldDir.Normalize();
        var best = Dir.Z;
        var bestDot = float.NegativeInfinity;
        foreach (Dir dir in System.Enum.GetValues(typeof(Dir)))
        {
            var axis = GetWorldAxis(t, dir);
            if (axis.sqrMagnitude < 1e-8f) continue;
            var dot = Vector3.Dot(axis.normalized, worldDir);
            if (dot <= bestDot) continue;
            bestDot = dot;
            best = dir;
        }

        return best;
    }

    public static Vector3 GetWorldAxis(Transform t, Dir dir)
    {
        if (t == null) return Vector3.zero;
        return dir switch
        {
            Dir.X => t.right,
            Dir.Y => t.up,
            Dir.Z => t.forward,
            Dir.XI => -t.right,
            Dir.YI => -t.up,
            Dir.ZI => -t.forward,
            _ => Vector3.zero
        };
    }
#endif

    /// <summary>本工位物料停靠世界坐标（段中心）。</summary>
    public virtual Vector3 MaterialPoint
    {
        get
        {
            var axis = ForwardWorldAxis;
            var origin = transform.position + transform.up * m_materialHeight;
            if (axis.sqrMagnitude < 1e-8f || Mathf.Abs(m_parkOffset) < 1e-6f)
                return origin;
            return origin + axis.normalized * m_parkOffset;
        }
    }

    /// <summary>本工位入口侧生成点（停靠点上游）。</summary>
    public virtual Vector3 EntryPoint
    {
        get
        {
            var axis = ForwardWorldAxis;
            if (axis.sqrMagnitude < 1e-8f)
                return MaterialPoint;
            return MaterialPoint - axis.normalized * m_entryDistance;
        }
    }

    protected Vector3 ForwardWorldAxis => m_forwardAxis switch
    {
        Dir.X => transform.right,
        Dir.Y => transform.up,
        Dir.Z => transform.forward,
        Dir.XI => -transform.right,
        Dir.YI => -transform.up,
        Dir.ZI => -transform.forward,
        _ => Vector3.zero
    };

    /// <summary>正转轴向对应的 localScale 分量绝对值。</summary>
    protected float GetForwardAxisLocalScale()
    {
        var s = transform.localScale;
        return m_forwardAxis switch
        {
            Dir.X or Dir.XI => Mathf.Abs(s.x),
            Dir.Y or Dir.YI => Mathf.Abs(s.y),
            _ => Mathf.Abs(s.z)
        };
    }

    /// <summary>把当前正转轴向的 localScale 写入手动长度配置。</summary>
    public void ApplyForwardScaleToLength()
    {
        m_length = Mathf.Max(0.01f, GetForwardAxisLocalScale());
#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    /// <summary>当前是否允许自动下传（子类可按顶升状态等限制）。</summary>
    protected virtual bool CanTransferDownstream => !_transferSuppressed;

    /// <summary>是否允许接货（子类可按顶升状态等限制）。</summary>
    protected virtual bool CanAcceptMaterial => true;

    protected override void Init()
    {
        base.Init();
        if (m_enableDiagLog)
        {
            EnsureDiagLog();
            DiagLog("INIT", DiagStateSnapshot());
        }
    }

    protected override void Dispose()
    {
        CloseDiagLog();
        base.Dispose();
    }

    protected virtual void Update()
    {
        if (Material == null) return;

        // 交界等待：下游拒收时每帧重试，不继续前冲
        if (_waitingHandoff && (ForwardRun || ReverseRun))
        {
            TryTransferDownstream();
            return;
        }

        if (!_beltDriving) return;
        if (!ForwardRun && !ReverseRun) return;

        TickBeltMove(Time.deltaTime);
    }

    protected virtual void LateUpdate()
    {
        // MQTT 无变化时不会再推包；武装后下一帧在此完成回收（堆垛机取走等只灭有货的场景）。
        // 放 LateUpdate，便于同帧内顶升机 Update 先 PAIR_TAKE。
        TryCompleteArmedEmptyRecycle();
    }

    /// <summary>接收上游物料。成功则接管；运行中继续由本段皮带驱动。</summary>
    public virtual bool TryAcceptMaterial(Transform material)
    {
        if (material == null || Material != null || !CanAcceptMaterial)
            return false;

        Material = material;
        if (Material.parent != null)
            Material.SetParent(null, true);

        Parked = false;
        _waitingHandoff = false;
        _createDeferred = false;
        DisarmEmptyRecycle();
        OnMaterialAccepted(Material);

        if (ForwardRun || ReverseRun)
            StartBeltDrive();
        else
        {
            Parked = true;
            OnMaterialParked();
        }

        DiagLog("ACCEPT_OK", DiagStateSnapshot());
        return true;
    }

    /// <summary>外部取走本工位物料。成功则清空本地引用并停止移动。</summary>
    public virtual bool TryTakeAwayMaterial(out Transform material)
    {
        material = null;
        if (Material == null)
            return false;

        material = Material;
        StopBeltDrive();
        Material = null;
        Parked = false;
        _waitingHandoff = false;
        DisarmEmptyRecycle();
        OnMaterialTakenAway(material);
        DiagLog("TAKE_OK", $"mat={material.name} | {DiagStateSnapshot()}");
        return true;
    }

    protected override void OnReceiveData(List<PointData> part)
    {
        if (part == null || part.Count < 5)
            return;

        bool forward = bool.Parse(part[2].Value);
        bool reverse = bool.Parse(part[3].Value);
        bool isLoad = bool.Parse(part[4].Value);

        OnParseExtraSignals(part);
        ApplyMotionSignals(forward, reverse, isLoad);
    }

    /// <summary>
    /// 应用正转/反转/有货信号并驱动物料。子类可自行解析点位后调用本方法
    /// （如提升机用 ProcessStatus / ActualSpeed / CabinPlatform）。
    /// </summary>
    protected void ApplyMotionSignals(bool forward, bool reverse, bool isLoad)
    {
        var prevF = ForwardRun;
        var prevR = ReverseRun;
        var prevLoad = IsLoad;

        ForwardRun = forward;
        ReverseRun = reverse;

        if (m_enableDiagLog && (prevF != ForwardRun || prevR != ReverseRun || prevLoad != isLoad))
            DiagLog("SIGNAL",
                $"F={Bool01(prevF)}->{Bool01(ForwardRun)} R={Bool01(prevR)}->{Bool01(ReverseRun)} load={Bool01(prevLoad)}->{Bool01(isLoad)} | {DiagStateSnapshot()}");

        // 顶升等子类先从配对站抢货，避免随后被有货上升沿入口新建挡住
        TryClaimMaterialBeforeCreate();

        if (isLoad && Material == null)
        {
            // PLC 有货常比视觉交权更早：上游/配对站仍持货时勿新建，否则中间会叠两箱
            if (TryFindUpstreamHoldingMaterial(out var upstream))
            {
                _createDeferred = true;
                if (m_enableDiagLog && !IsLoad)
                    DiagLog("CREATE_SKIP",
                        $"upstream holding from={StationKeyOf(upstream)} | " + DiagStateSnapshot());
            }
            else if (!IsLoad || _createDeferred)
            {
                _createDeferred = false;
                CreateMaterialAtEntry();
            }
        }
        else if (m_enableDiagLog && isLoad && !IsLoad && Material != null)
            DiagLog("CREATE_SKIP", "rising load but already has material | " + DiagStateSnapshot());

        if (!isLoad || Material != null)
            _createDeferred = false;

        if (Material != null)
        {
            if (ForwardRun || ReverseRun)
            {
                DisarmEmptyRecycle();
                EnsureMaterialMotionWhileRunning();
            }
            else
            {
                if (IsMaterialMoving || _waitingHandoff)
                    StopMaterialInPlace();

                TryRecycleIfEmpty(isLoad);
            }
        }
        else
        {
            DisarmEmptyRecycle();
            if (!isLoad)
                OnIdleEmpty();
        }

        IsLoad = isLoad;
        OnAfterReceiveData(isLoad);
    }

    /// <summary>解析子类额外点位（如举升）。</summary>
    protected virtual void OnParseExtraSignals(List<PointData> part) { }

    /// <summary>本帧点位处理收尾。</summary>
    protected virtual void OnAfterReceiveData(bool isLoad) { }

    /// <summary>无货且本地无物料时（如顶升复位）。</summary>
    protected virtual void OnIdleEmpty() { }

    /// <summary>刚接到上游物料。</summary>
    protected virtual void OnMaterialAccepted(Transform material) { }

    /// <summary>物料刚停靠到位（停转或顶升抑制回中心）。</summary>
    protected virtual void OnMaterialParked() { }

    /// <summary>物料被取走后。</summary>
    protected virtual void OnMaterialTakenAway(Transform material) { }

    /// <summary>下传前一刻（如顶升机脱挂平台）。</summary>
    protected virtual void OnBeforeTransferDownstream(Transform material) { }

    /// <summary>有货上升沿创建之前：子类可先从配对站抢货。</summary>
    protected virtual void TryClaimMaterialBeforeCreate() { }

    /// <summary>有货上升沿的生成点。普通输送在入口；顶升抢货失败补建时用中心。</summary>
    protected virtual Vector3 GetCreateSpawnPoint() => EntryPoint;

    protected void CreateMaterialAtEntry()
    {
        var spawn = GetCreateSpawnPoint();
        var go = Execute<Vector3, GameObject>("CreateNewMaterial", spawn);
        if (go == null)
        {
            DiagLog("CREATE_FAIL", "CreateNewMaterial returned null | " + DiagStateSnapshot());
            return;
        }

        Material = go.transform;
        Material.position = spawn;
        _waitingHandoff = false;
        _createDeferred = false;
        DisarmEmptyRecycle();

        var atPark = (spawn - MaterialPoint).sqrMagnitude < 1e-6f;
        Parked = atPark && !ForwardRun && !ReverseRun;
        OnMaterialAccepted(Material);

        if (ForwardRun || ReverseRun)
            EnsureMaterialMotionWhileRunning();
        else if (Parked)
            OnMaterialParked();

        DiagLog("CREATE_OK", DiagStateSnapshot());
    }

    /// <summary>
    /// 有货上升沿时：若拓扑上游或配对顶升仍持有物料，说明货还在交权途中，不应再生成。
    /// </summary>
    protected virtual bool TryFindUpstreamHoldingMaterial(out ConveyorPartMotion upstream)
    {
        upstream = null;

        if (IsUpstreamCandidate(m_reverseNext, expectAsForwardNext: true))
        {
            upstream = m_reverseNext;
            return true;
        }

        if (IsUpstreamCandidate(m_forwardNext, expectAsForwardNext: false))
        {
            upstream = m_forwardNext;
            return true;
        }

        // 邻接未互配 / 顶升配对 / 提升机各层皮带链路：兜底扫一次（仅上升沿，频率低）
        foreach (var s in FindObjectsByType<ConveyorPartMotion>(FindObjectsSortMode.None))
        {
            if (s == this || !s.HasMaterial) continue;
            if (s.ForwardNext == this || s.ReverseNext == this)
            {
                upstream = s;
                return true;
            }

            if (s is JackTransferPartMotion jack && jack.PairedStation == this)
            {
                upstream = s;
                return true;
            }

            if (s is LifterPartMotion lifter && lifter.IsFloorNeighbor(this))
            {
                upstream = s;
                return true;
            }
        }

        return false;
    }

    /// <summary>停转且光电无货时是否允许回收本地物料。</summary>
    protected virtual bool ShouldRecycleWhenEmpty()
    {
        if (Material == null || IsMaterialMoving || _waitingHandoff)
            return false;
        if (IsClaimedByPairedJack())
            return false;
        return true;
    }

    /// <summary>配对顶升机正在升起抢本站货物。</summary>
    protected bool IsClaimedByPairedJack()
    {
        foreach (var jack in FindObjectsByType<JackTransferPartMotion>(FindObjectsSortMode.None))
        {
            if (jack == null || jack == this) continue;
            if (jack.PairedStation == this && jack.IsTakingFromPaired)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 光电灭且停转：本帧只刹停并武装；下一帧 LateUpdate 仍无人取走才回收。
    /// 避免同帧「CC 先无货、LR 后顶起」把货销掉；也不依赖 MQTT 再推一包（堆垛机取走后常不再变化）。
    /// </summary>
    private void TryRecycleIfEmpty(bool isLoad)
    {
        if (isLoad || IsMaterialMoving)
        {
            DisarmEmptyRecycle();
            return;
        }

        if (!_emptyRecycleArmed)
        {
            _emptyRecycleArmed = true;
            _emptyRecycleArmFrame = Time.frameCount;
            DiagLog("RECYCLE_ARM", DiagStateSnapshot());
            return;
        }

        // 同帧内又推到停转无货（或诊断日志下重复包）：直接尝试完成
        TryCompleteArmedEmptyRecycle(forceSameFrame: true);
    }

    /// <summary>武装后的下一帧（或同批二次无货）完成回收；顶升同帧抢货会清掉 Material / 武装。</summary>
    private void TryCompleteArmedEmptyRecycle(bool forceSameFrame = false)
    {
        if (!_emptyRecycleArmed || Material == null)
            return;
        if (IsLoad || ForwardRun || ReverseRun || IsMaterialMoving)
        {
            DisarmEmptyRecycle();
            return;
        }

        if (!forceSameFrame && Time.frameCount <= _emptyRecycleArmFrame)
            return;

        if (!ShouldRecycleWhenEmpty())
        {
            DiagLog("RECYCLE_SKIP", "claimed or waiting | " + DiagStateSnapshot());
            return;
        }

        RecycleMaterial();
    }

    protected void DisarmEmptyRecycle()
    {
        _emptyRecycleArmed = false;
        _emptyRecycleArmFrame = -1;
    }

    private bool IsUpstreamCandidate(ConveyorPartMotion other, bool expectAsForwardNext)
    {
        if (other == null || !other.HasMaterial) return false;
        if (expectAsForwardNext)
            return other.ForwardNext == this || other.CurrentDownstream == this;
        return other.ReverseNext == this || other.CurrentDownstream == this;
    }

    /// <summary>运行中：有下游则驶向交界切权；抑制下传时驶回中心停靠；无下游则沿轴向自由输送。</summary>
    protected void EnsureMaterialMotionWhileRunning()
    {
        if (Material == null || (!ForwardRun && !ReverseRun)) return;

        var downstream = CurrentDownstream;
        if (downstream != null && _waitingHandoff)
        {
            TryTransferDownstream();
            return;
        }

        StartBeltDrive();
    }

    /// <summary>开始由本段皮带驱动物料。</summary>
    protected void StartBeltDrive()
    {
        if (Material == null) return;
        if (_beltDriving && !Parked && !_waitingHandoff) return;

        _beltDriving = true;
        Parked = false;
        _waitingHandoff = false;
        DiagLog("BELT_ON", DiagStateSnapshot());
    }

    /// <summary>停止皮带驱动（保留物料当前位置）。</summary>
    protected void StopBeltDrive()
    {
        _beltDriving = false;
        _waitingHandoff = false;
    }

    /// <summary>按速度平移一帧：有下游则沿两工位连线驶向交界；无下游则沿配置轴向。</summary>
    protected void TickBeltMove(float dt)
    {
        if (Material == null || dt <= 0f) return;

        var park = MaterialPoint;
        var step = m_speed * dt;

        // 顶升等抑制下传：驶回段中心停靠，供配对设备取货（不吸回配置轴，避免左右闪现）
        if (_transferSuppressed || !CanTransferDownstream)
        {
            Material.position = Vector3.MoveTowards(Material.position, park, step);
            if ((Material.position - park).sqrMagnitude <= 1e-8f)
            {
                Material.position = park;
                _beltDriving = false;
                Parked = true;
                _waitingHandoff = false;
                DiagLog("PARKED", "suppress/center | " + DiagStateSnapshot());
                OnMaterialParked();
            }

            return;
        }

        var downstream = CurrentDownstream;
        if (downstream != null && TryGetHandover(downstream, out var handoffPoint, out var handoffDist))
        {
            var from = park;
            var to = downstream.MaterialPoint;
            var delta = to - from;
            if (delta.sqrMagnitude < 1e-10f)
            {
                Material.position = handoffPoint;
                TryTransferDownstream();
                return;
            }

            var dir = delta.normalized;
            var progress = Vector3.Dot(Material.position - from, dir);

            // 沿两工位连线前进；到交界只收束纵向进度，不把货物吸到连线上（避免左右闪现）
            if (progress + step >= handoffDist - 1e-4f)
            {
                Material.position += dir * Mathf.Max(0f, handoffDist - progress);
                DiagLog("HANDOFF_REACH",
                    $"dist={handoffDist:F3} to={StationKeyOf(downstream)} | {DiagStateSnapshot()}");
                TryTransferDownstream();
                return;
            }

            Material.position += dir * step;
            return;
        }

        // 无下游：沿运行方向持续平移
        var axis = ForwardWorldAxis;
        if (axis.sqrMagnitude < 1e-8f) return;
        axis.Normalize();
        var sign = ForwardRun ? 1f : -1f;
        Material.position += axis * (sign * step);
    }

    /// <summary>
    /// 计算与下游的交界切权点：centerDist * lenSelf / (lenSelf + lenDown)。
    /// 间隙按两端长度比例分摊。
    /// </summary>
    protected bool TryGetHandover(ConveyorPartMotion downstream, out Vector3 handoffPoint, out float handoffDist)
    {
        handoffPoint = default;
        handoffDist = 0f;
        if (downstream == null) return false;

        var from = MaterialPoint;
        var to = downstream.MaterialPoint;
        var delta = to - from;
        var centerDist = delta.magnitude;
        if (centerDist < 1e-5f)
        {
            handoffPoint = from;
            handoffDist = 0f;
            return true;
        }

        var lenA = Length;
        var lenB = downstream.Length;
        handoffDist = centerDist * (lenA / (lenA + lenB));
        handoffPoint = from + delta * (handoffDist / centerDist);
        return true;
    }

    protected void ClampToHandover(ConveyorPartMotion downstream)
    {
        if (Material == null) return;
        if (!TryGetHandover(downstream, out var handoffPoint, out var handoffDist))
            return;

        var from = MaterialPoint;
        var to = downstream.MaterialPoint;
        var delta = to - from;
        if (delta.sqrMagnitude < 1e-10f)
        {
            Material.position = handoffPoint;
            return;
        }

        // 只收束沿连线的进度，保留横向偏差，避免拒收等待时左右闪现
        var dir = delta.normalized;
        var progress = Vector3.Dot(Material.position - from, dir);
        Material.position += dir * (handoffDist - progress);
    }

    /// <summary>停转时立刻原地刹停。</summary>
    protected void StopMaterialInPlace()
    {
        if (Material == null) return;

        var wasMoving = IsMaterialMoving || _waitingHandoff;
        StopBeltDrive();

        if (wasMoving)
        {
            Parked = true;
            DiagLog("STOP_IN_PLACE", DiagStateSnapshot());
            OnMaterialParked();
        }
        else
        {
            DiagLog("STOP_IN_PLACE", DiagStateSnapshot());
        }
    }

    /// <summary>将本地物料交给当前运行方向的下游（须已到交界或正在等待接货）。</summary>
    protected void TryTransferDownstream()
    {
        if (!CanTransferDownstream) return;
        if (Material == null) return;
        if (!ForwardRun && !ReverseRun) return;

        var downstream = CurrentDownstream;
        if (downstream == null)
        {
            _waitingHandoff = false;
            if (!_beltDriving)
                StartBeltDrive();
            return;
        }

        if (!_waitingHandoff && !IsAtOrPastHandover(downstream))
            return;

        var material = Material;
        var downKey = StationKeyOf(downstream);
        OnBeforeTransferDownstream(material);
        if (!downstream.TryAcceptMaterial(material))
        {
            ClampToHandover(downstream);
            _beltDriving = false;
            _waitingHandoff = true;
            Parked = false;

            if (Time.frameCount - _lastXferRejectFrame >= 30)
            {
                _lastXferRejectFrame = Time.frameCount;
                DiagLog("XFER_REJECT", $"to={downKey} mat={material.name} | {DiagStateSnapshot()}");
            }

            return;
        }

        Material = null;
        Parked = false;
        _beltDriving = false;
        _waitingHandoff = false;
        DiagLog("XFER_OK", $"to={downKey} mat={material.name} | {DiagStateSnapshot()}");
    }

    protected bool IsAtOrPastHandover(ConveyorPartMotion downstream)
    {
        if (Material == null || !TryGetHandover(downstream, out _, out var handoffDist))
            return false;

        var from = MaterialPoint;
        var to = downstream.MaterialPoint;
        var delta = to - from;
        if (delta.sqrMagnitude < 1e-10f)
            return true;

        var progress = Vector3.Dot(Material.position - from, delta.normalized);
        return progress >= handoffDist - 1e-3f;
    }

    protected void RecycleMaterial()
    {
        if (Material == null) return;

        var matName = Material.name;
        Publish("RecycleMaterialModel", Material);
        Material = null;
        Parked = false;
        DisarmEmptyRecycle();
        StopBeltDrive();
        DiagLog("RECYCLE", $"mat={matName} | {DiagStateSnapshot()}");
    }

    /// <summary>子类与基类共用的诊断日志入口；未开启时无开销。</summary>
    protected void DiagLog(string evt, string detail = null)
    {
        if (!m_enableDiagLog) return;
        EnsureDiagLog();
        _diagLog.Write(StationKeyOf(this), evt, detail ?? string.Empty);
    }

    /// <summary>当前工位状态快照，便于事后对照闪现。</summary>
    protected virtual string DiagStateSnapshot()
    {
        var sb = new StringBuilder(128);
        sb.Append("mat=").Append(Material != null ? Material.name : "-");
        sb.Append(" parked=").Append(Bool01(Parked));
        sb.Append(" moving=").Append(Bool01(IsMaterialMoving));
        sb.Append(" waitXfer=").Append(Bool01(_waitingHandoff));
        sb.Append(" len=").Append(Length.ToString("F2"));
        if (m_useForwardScaleAsLength)
            sb.Append("(scale)");
        sb.Append(" F=").Append(Bool01(ForwardRun));
        sb.Append(" R=").Append(Bool01(ReverseRun));
        sb.Append(" load=").Append(Bool01(IsLoad));
        sb.Append(" suppress=").Append(Bool01(_transferSuppressed));
        sb.Append(" canXfer=").Append(Bool01(CanTransferDownstream));
        sb.Append(" canAccept=").Append(Bool01(CanAcceptMaterial));
        if (Material != null)
        {
            var p = Material.position;
            sb.Append(" pos=(")
                .Append(p.x.ToString("F3")).Append(',')
                .Append(p.y.ToString("F3")).Append(',')
                .Append(p.z.ToString("F3")).Append(')');
        }

        return sb.ToString();
    }

    protected static string Bool01(bool v) => v ? "1" : "0";

    protected string StationKeyOf(ConveyorPartMotion station)
    {
        if (station == null) return "null";
        if (!string.IsNullOrEmpty(station.m_partID))
            return station.m_partID;
        return station.name;
    }

    private void EnsureDiagLog()
    {
        if (_diagLog != null) return;
        _diagLog = ConveyorMotionDiagLog.Acquire();
    }

    private void CloseDiagLog()
    {
        if (_diagLog == null) return;
        _diagLog.Dispose();
        _diagLog = null;
    }

    protected override PartDataInfo GetInfo() => null;

#if UNITY_EDITOR
    protected static readonly Color GizmoForwardColor = new Color(0.1f, 1f, 0.3f, 1f);
    protected static readonly Color GizmoReverseColor = new Color(1f, 0.28f, 0.08f, 1f);
    protected static readonly Color GizmoIdleColor = new Color(0.55f, 0.62f, 0.72f, 0.55f);
    protected static readonly Color GizmoCargoColor = new Color(1f, 0.82f, 0.1f, 1f);
    protected static readonly Color GizmoParkedColor = new Color(0.15f, 0.95f, 1f, 1f);
    protected static readonly Color GizmoEmptyColor = new Color(0.55f, 0.6f, 0.65f, 0.5f);

    /// <summary>工位状态 Gizmos；显示由 <see cref="ConveyorLineManager"/> 统一开关（编辑态选中 / 运行态有货或运转时可见）。</summary>
    protected virtual void OnDrawGizmos()
    {
        if (!ConveyorLineManager.ShouldDrawGizmos(this)) return;
        bool selected = UnityEditor.Selection.Contains(gameObject);
        DrawStationGizmos(selected);
    }

    protected virtual void DrawStationGizmos(bool selected)
    {
        bool running = ForwardRun || ReverseRun;
        // 空闲且无货时不画，避免整线铺满；选中时仍可看轴向
        if (!selected && !running && !HasMaterial) return;

        var park = MaterialPoint;
        float scale = selected ? 1.4f : 1f;
        // 有货时抬高标记，避免埋进托盘/货物模型里
        float markerLift = HasMaterial ? 0.55f : 0.14f;
        var marker = park + Vector3.up * markerLift;

        DrawCargoMarker(park, marker, scale);

        var axis = ForwardWorldAxis;
        if (axis.sqrMagnitude < 1e-8f) return;
        axis.Normalize();

        if (running)
        {
            var runDir = ForwardRun ? axis : -axis;
            var runColor = ForwardRun ? GizmoForwardColor : GizmoReverseColor;
            DrawRuntimeArrow(marker, runDir, selected ? 0.95f : 0.75f, runColor, selected);
        }
        else if (selected)
        {
            DrawRuntimeIdleMark(marker, axis, selected: true);
        }
    }

    /// <summary>有货：头顶实心球 + 黑描边 + 竖杆指向停靠点，避免和货物融色。</summary>
    protected void DrawCargoMarker(Vector3 park, Vector3 marker, float scale)
    {
        if (HasMaterial)
        {
            var color = IsParked ? GizmoParkedColor : GizmoCargoColor;
            float r = (IsParked ? 0.16f : 0.14f) * scale;

            // 竖杆：从停靠点连到头顶标记，方便定位是哪一托
            UnityEditor.Handles.color = new Color(0f, 0f, 0f, 0.85f);
            UnityEditor.Handles.DrawAAPolyLine(5f, park, marker);
            UnityEditor.Handles.color = color;
            UnityEditor.Handles.DrawAAPolyLine(2.5f, park, marker);

            // 黑底描边球，再叠亮色实心，对比更强
            Gizmos.color = new Color(0f, 0f, 0f, 0.9f);
            Gizmos.DrawSphere(marker, r * 1.35f);
            Gizmos.color = color;
            Gizmos.DrawSphere(marker, r);

            UnityEditor.Handles.color = Color.black;
            UnityEditor.Handles.DrawWireDisc(marker, Vector3.up, r * 1.55f);
            UnityEditor.Handles.DrawWireDisc(marker, Vector3.forward, r * 1.55f);
            UnityEditor.Handles.color = color;
            UnityEditor.Handles.DrawWireDisc(marker, Vector3.up, r * 1.85f);
        }
        else
        {
            Gizmos.color = GizmoEmptyColor;
            Gizmos.DrawWireSphere(marker, 0.08f * scale);
        }
    }

    protected static void DrawRuntimeArrow(Vector3 origin, Vector3 dir, float length, Color color, bool selected)
    {
        if (dir.sqrMagnitude < 1e-8f || length < 1e-4f) return;

        dir = dir.normalized;
        var tip = origin + dir * length;
        var head = Mathf.Clamp(length * 0.3f, 0.16f, 0.3f);
        var right = Vector3.Cross(dir, Vector3.up);
        if (right.sqrMagnitude < 1e-6f)
            right = Vector3.Cross(dir, Vector3.right);
        right = right.normalized * (head * 0.6f);

        float width = selected ? 8f : 6f;
        var glow = color;
        glow.a = 0.3f;
        UnityEditor.Handles.color = glow;
        UnityEditor.Handles.DrawAAPolyLine(width + 6f, origin, tip);
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.DrawAAPolyLine(width, origin, tip);
        UnityEditor.Handles.DrawAAPolyLine(width, tip, tip - dir * head + right);
        UnityEditor.Handles.DrawAAPolyLine(width, tip, tip - dir * head - right);

        Gizmos.color = color;
        Gizmos.DrawSphere(tip, selected ? 0.07f : 0.055f);
    }

    protected static void DrawRuntimeIdleMark(Vector3 origin, Vector3 axis, bool selected)
    {
        float half = selected ? 0.28f : 0.2f;
        UnityEditor.Handles.color = GizmoIdleColor;
        UnityEditor.Handles.DrawAAPolyLine(3f, origin - axis * half, origin + axis * half);
        Gizmos.color = GizmoIdleColor;
        Gizmos.DrawWireSphere(origin, selected ? 0.045f : 0.035f);
    }

    private const float AutoBindMaxDistance = 8f;
    private const float AutoBindMinAlignment = 0.35f;

    [ContextMenu("长度/用轴向缩放写入长度")]
    private void ContextApplyForwardScaleToLength() => ApplyForwardScaleToLength();

    [ContextMenu("自动连接/正转下游")]
    protected void BindForwardNext()
    {
        var next = FindNeighbor(ForwardWorldAxis);
        var owner = ConveyorLineManager.FindOwner(this);
        int floor = owner != null ? owner.Floor : 1;
        EditorSetNeighbor(next, forward: true, writeOpposite: true, floor);
        LogBindResult("正转下游", next);
    }

    [ContextMenu("自动连接/反转下游")]
    protected void BindReverseNext()
    {
        var next = FindNeighbor(-ForwardWorldAxis);
        var owner = ConveyorLineManager.FindOwner(this);
        int floor = owner != null ? owner.Floor : 1;
        EditorSetNeighbor(next, forward: false, writeOpposite: true, floor);
        LogBindResult("反转下游", next);
    }

    [ContextMenu("自动连接/前后工位")]
    protected void BindForwardAndReverseNext()
    {
        BindForwardNext();
        BindReverseNext();
    }

    protected void ApplyNeighborLink(ConveyorPartMotion next, bool forward, bool writeOpposite)
    {
        UnityEditor.Undo.RecordObject(this, "Auto Bind Station Neighbor");
        if (forward)
            m_forwardNext = next;
        else
            m_reverseNext = next;
        UnityEditor.EditorUtility.SetDirty(this);

        if (!writeOpposite || next == null) return;

        UnityEditor.Undo.RecordObject(next, "Auto Bind Station Opposite");
        if (forward)
        {
            if (next.m_reverseNext == null || next.m_reverseNext == this)
                next.m_reverseNext = this;
        }
        else
        {
            if (next.m_forwardNext == null || next.m_forwardNext == this)
                next.m_forwardNext = this;
        }

        UnityEditor.EditorUtility.SetDirty(next);
    }

    protected ConveyorPartMotion FindNeighbor(Vector3 preferDir, IList<ConveyorPartMotion> candidates = null)
    {
        if (preferDir.sqrMagnitude < 1e-8f) return null;
        preferDir = preferDir.normalized;

        IList<ConveyorPartMotion> source = candidates;
        List<ConveyorPartMotion> owned = null;
        if (source == null)
        {
            var owner = ConveyorLineManager.FindOwner(this);
            if (owner != null)
            {
                owned = new List<ConveyorPartMotion>(64);
                owner.CollectStations(owned);
                source = owned;
            }
            else
            {
                source = FindObjectsByType<ConveyorPartMotion>(FindObjectsSortMode.None);
            }
        }

        ConveyorPartMotion best = null;
        var bestScore = float.NegativeInfinity;

        for (int i = 0; i < source.Count; i++)
        {
            var station = source[i];
            if (station == null || station == this) continue;

            var offset = station.transform.position - transform.position;
            var dist = offset.magnitude;
            if (dist < 1e-4f || dist > AutoBindMaxDistance) continue;

            var alignment = Vector3.Dot(preferDir, offset / dist);
            if (alignment < AutoBindMinAlignment) continue;

            // 更近、更对齐优先
            var score = alignment / dist;
            if (score <= bestScore) continue;

            bestScore = score;
            best = station;
        }

        return best;
    }

    private void LogBindResult(string label, ConveyorPartMotion next)
    {
        if (next != null)
            Debug.Log($"[{name}] 自动连接{label} → {next.name}", this);
        else
            Debug.LogWarning($"[{name}] 未找到合适的{label}（方向对齐≥{AutoBindMinAlignment}，距离≤{AutoBindMaxDistance}m）", this);
    }
#endif
}
}
