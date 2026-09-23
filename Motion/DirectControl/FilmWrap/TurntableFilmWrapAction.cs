using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 龙门式环轨缠绕机（货不动）：升降架只平移，旋转架公转，膜卷自转。
    /// 典型层级：升降架 → 绕中心旋转架 → 膜卷。路径与膜带复用 <see cref="FilmWrapPath"/> / <see cref="FilmWrapRibbon"/>。
    /// </summary>
    public class TurntableFilmWrapAction : MonoBehaviour
    {
        private const string FilmShaderName = "NonsensicalKit/DigitalTwin/FilmStretch";
        private const float AngleEpsilon = 0.05f;

        [Header("包裹目标")]
        [Label("货物根节点")]
        [Tooltip("货物固定不动；螺旋与膜带相对它计算")]
        [SerializeField] private Transform m_cargo;

        [Label("货物立方尺寸")]
        [SerializeField] private Vector3 m_cuboidSize = new Vector3(1f, 1.2f, 1f);

        [Label("立方中心(本地)")]
        [SerializeField] private Vector3 m_cuboidCenterLocal;

        [Label("表面外扩")]
        [SerializeField] [Min(0.005f)] private float m_surfaceOffset = 0.04f;

        [Label("拐角半径")]
        [SerializeField] [Min(0f)] private float m_cornerRadius = 0.04f;

        [Header("螺旋")]
        [Label("绕行半径")]
        [Tooltip("出膜点绕货物的水平半径（货物本地）")]
        [SerializeField] private float m_orbitRadius = 1.45f;

        [Label("起始高度")]
        [Tooltip("缠膜螺旋相对货物的起始高度（货物本地 Y），影响升降目标")]
        [SerializeField] private float m_bottomY = 0.2f;

        [Label("最高高度")]
        [Tooltip("缠膜螺旋相对货物的最高高度（货物本地 Y），影响升降目标")]
        [SerializeField] private float m_topY = 1.55f;

        [Label("缠绕圈数")]
        [SerializeField] [Range(1f, 12f)] private float m_revolutions = 6f;

        [Label("先到终点再微回")]
        [Tooltip("勾选后先走到终点高度再略回一点；关闭则匀速走完高度")]
        [SerializeField] private bool m_upThenSlightDown = true;

        [Label("缠膜垂直方向")]
        [Tooltip("自下而上 / 自上而下")]
        [SerializeField] private FilmWrapVerticalDirection m_verticalDirection = FilmWrapVerticalDirection.BottomToTop;

        [Label("每圈采样数")]
        [SerializeField] [Range(16, 128)] private int m_samplesPerRevolution = 64;

        [Header("机构")]
        [Label("升降架")]
        [Tooltip("只沿升降轴平移，不旋转。例：FWM_002")]
        [SerializeField] private Transform m_carriage;

        [Label("升降轴")]
        [SerializeField] private SignedAxis m_carriageAxis = SignedAxis.PositiveY;

        [Label("升降速度")]
        [Tooltip("本地单位/秒；从当前高度逐渐追上膜路径高度")]
        [SerializeField] private float m_liftSpeed = 0.8f;

        [Label("升降高度偏移")]
        [Tooltip("叠加在路径目标高度上（沿升降轴）。模型比缠膜点偏高填负值，偏低填正值")]
        [SerializeField] private float m_liftOffset;

        [Label("先升降再缠膜")]
        [Tooltip("勾选后：升降架先到达起始缠膜高度，再开始公转与出膜")]
        [SerializeField] private bool m_waitLiftBeforeWrap = true;

        [Label("升降到位阈值")]
        [Tooltip("与目标高度差小于该值视为到位")]
        [SerializeField] private float m_liftArriveEpsilon = 0.01f;

        [Label("绕中心旋转架")]
        [Tooltip("带着膜卷绕机器/货物中心公转，只改旋转。例：FWM_003")]
        [FormerlySerializedAs("m_turntable")]
        [SerializeField] private Transform m_orbitFrame;

        [Label("公转轴")]
        [FormerlySerializedAs("m_turntableAxis")]
        [SerializeField] private SignedAxis m_orbitAxis = SignedAxis.PositiveY;

        [Label("公转反向")]
        [SerializeField] private bool m_invertOrbit;

        [Label("膜卷")]
        [Tooltip("绕自身中心轴自转放膜。例：Capsule")]
        [SerializeField] private Transform m_filmRoll;

        [Label("膜卷自转轴")]
        [SerializeField] private SignedAxis m_filmRollAxis = SignedAxis.PositiveX;

        [Label("膜卷反向")]
        [SerializeField] private bool m_invertFilmRoll;

        [Label("出膜点")]
        [Tooltip("为空则不画出膜条；可与膜卷同节点")]
        [SerializeField] private Transform m_nozzleTip;

        [Header("膜带")]
        [Label("膜带宽")]
        [SerializeField] private float m_filmWidth = 0.28f;

        [Label("显示出膜条")]
        [SerializeField] private bool m_showFeedStrip = true;

        [Label("出膜起点偏移")]
        [SerializeField] private Vector3 m_feedStartLocalOffset = new Vector3(-0.12f, 0f, -0.05f);

        [Label("膜带材质")]
        [Tooltip("为空则运行时使用 DigitalTwin/FilmStretch")]
        [SerializeField] private Material m_filmMaterial;

        [Header("播放")]
        [Label("一轮时长(秒)")]
        [Tooltip("到顶后从头循环")]
        [SerializeField] private float m_duration = 14f;

        [Header("停止")]
        [Label("停止后渐退")]
        [Tooltip("SetRunning(false) 后机构渐渐回到初始姿态；关闭则瞬间复位")]
        [SerializeField] private bool m_smoothReturnOnStop = true;

        [Label("回位升降速度")]
        [Tooltip("本地单位/秒；≤0 时沿用升降速度")]
        [SerializeField] private float m_returnLiftSpeed;

        [Label("回位角速度")]
        [Tooltip("度/秒，用于旋转架与膜卷回正")]
        [SerializeField] private float m_returnAngularSpeed = 180f;

        private struct Pose
        {
            public Vector3 localPosition;
            public Quaternion localRotation;
            public bool valid;
        }

        private readonly FilmWrapPath _path = new FilmWrapPath();
        private readonly FilmWrapRibbon _ribbon = new FilmWrapRibbon();

        private Transform _ribbonHost;
        private Material _runtimeMaterial;
        private Pose _carriagePose;
        private Pose _orbitPose;
        private Pose _filmRollPose;
        private Vector3 _orbitAxisLocal;
        private Vector3 _rollAxisLocal;
        private Vector3 _liftAxisLocal;
        private float _orbitDegrees;
        private float _rollDegrees;
        private float _restLift;
        private float _currentLift;
        private bool _liftReady;
        private bool _wrapStarted;
        private bool _inited;
        private bool _running;
        private bool _returning;
        private float _progress;

        public bool IsRunning => _running;
        public bool IsReturning => _returning;
        public bool IsWrapStarted => _wrapStarted;
        public float Progress => _progress;

        private void Awake() => EnsureInit();

        private void OnDisable()
        {
            if (_inited)
                SnapIdle();
        }

        private void OnDestroy()
        {
            _ribbon.Dispose();
            if (_runtimeMaterial != null)
                Destroy(_runtimeMaterial);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            if (_returning)
            {
                if (StepReturn(dt))
                    SnapIdle();
                return;
            }

            if (!_running || m_cargo == null)
                return;

            if (!_wrapStarted)
            {
                bool arrived = MoveCarriage(_path.Evaluate(0f), m_liftSpeed, dt);
                if (!arrived && m_waitLiftBeforeWrap)
                    return;

                _wrapStarted = true;
                if (m_waitLiftBeforeWrap)
                    return;
            }

            _progress += dt / Mathf.Max(0.01f, m_duration);
            if (_progress >= 1f)
                _progress %= 1f;

            Drive(_progress, dt);
        }

        /// <summary>开始循环缠膜。可挂到 RunningStatus.OnRunning。</summary>
        public void StartAction() => SetRunning(true);

        /// <summary>停止。可挂到 RunningStatus.OnStopped。</summary>
        public void StopAction() => SetRunning(false);

        /// <summary>按布尔开关。可挂到 RunningStatus.OnStatusChanged。</summary>
        public void SetRunning(bool running)
        {
            if (!Application.isPlaying)
                return;

            EnsureInit();
            if (running)
            {
                if (_running && !_returning)
                    return;

                _returning = false;
                _running = true;
                _progress = 0f;
                _wrapStarted = !m_waitLiftBeforeWrap;
                SyncPath();
                ReadCurrentLift();
                return;
            }

            if (!_running && !_returning)
                return;

            _running = false;
            _wrapStarted = false;
            _ribbon.Clear();
            if (m_smoothReturnOnStop)
            {
                _returning = true;
                if (!_liftReady)
                    ReadCurrentLift();
            }
            else
            {
                SnapIdle();
            }
        }

        /// <summary>瞬间回到初始姿态并清空膜。</summary>
        public void ResetPose()
        {
            EnsureInit();
            SnapIdle();
        }

        [Button("从货物包围盒拟合尺寸")]
        [ContextMenu("TurntableFilmWrap/从货物包围盒拟合尺寸")]
        public void FitCuboidFromCargo()
        {
            CopyPathSettings();
            _path.AutoFitFromCargoBounds();
            m_cuboidSize = _path.CuboidSize;
            m_cuboidCenterLocal = _path.CuboidCenterLocal;
            _path.Invalidate();
        }

        private void Drive(float progress, float dt)
        {
            MoveCarriage(_path.Evaluate(progress), m_liftSpeed, dt);
            ApplySpin(m_orbitFrame, _orbitPose, _orbitAxisLocal, progress * _orbitDegrees);
            ApplySpin(m_filmRoll, _filmRollPose, _rollAxisLocal, progress * _rollDegrees);
            _ribbon.BuildUntil(progress);
        }

        private bool MoveCarriage(in FilmWrapSample sample, float speed, float dt)
        {
            if (!_carriagePose.valid || m_carriage == null)
                return true;

            if (!_liftReady)
                ReadCurrentLift();

            float target = MeasureLift(sample.nozzleWorld);
            if (speed <= 0f)
                _currentLift = target;
            else
                _currentLift = Mathf.MoveTowards(_currentLift, target, speed * dt);

            WriteCarriage(_currentLift);
            return Mathf.Abs(_currentLift - target) <= m_liftArriveEpsilon;
        }

        private float MeasureLift(Vector3 nozzleWorld)
        {
            Transform parent = m_carriage.parent;
            Vector3 axis = parent != null ? parent.TransformDirection(_liftAxisLocal) : _liftAxisLocal;
            Vector3 origin = parent != null ? parent.position : transform.position;
            return Vector3.Dot(nozzleWorld - origin, axis) + m_liftOffset;
        }

        private void ReadCurrentLift()
        {
            if (!_carriagePose.valid)
            {
                _liftReady = false;
                return;
            }

            _currentLift = SignedAxisUtil.GetComponent(m_carriage.localPosition, m_carriageAxis);
            _liftReady = true;
        }

        private void WriteCarriage(float lift)
        {
            if (m_carriage == null)
                return;

            m_carriage.localRotation = _carriagePose.localRotation;
            m_carriage.localPosition = SignedAxisUtil.WithComponent(_carriagePose.localPosition, m_carriageAxis, lift);
        }

        private bool StepReturn(float dt)
        {
            float liftSpeed = m_returnLiftSpeed > 0f ? m_returnLiftSpeed : m_liftSpeed;
            bool liftDone = true;
            if (_carriagePose.valid)
            {
                if (!_liftReady)
                    ReadCurrentLift();

                _currentLift = liftSpeed <= 0f
                    ? _restLift
                    : Mathf.MoveTowards(_currentLift, _restLift, liftSpeed * dt);
                WriteCarriage(_currentLift);
                liftDone = Mathf.Abs(_currentLift - _restLift) <= m_liftArriveEpsilon;
            }

            bool orbitDone = ReturnSpin(m_orbitFrame, _orbitPose, m_returnAngularSpeed, dt);
            bool rollDone = ReturnSpin(m_filmRoll, _filmRollPose, m_returnAngularSpeed, dt);
            return liftDone && orbitDone && rollDone;
        }

        private static void ApplySpin(Transform target, in Pose rest, Vector3 localAxis, float degrees)
        {
            if (!rest.valid || target == null)
                return;

            target.localPosition = rest.localPosition;
            target.localRotation = rest.localRotation * Quaternion.AngleAxis(degrees, localAxis);
        }

        private static bool ReturnSpin(Transform target, in Pose rest, float degPerSec, float dt)
        {
            if (!rest.valid || target == null)
                return true;

            target.localPosition = rest.localPosition;
            if (degPerSec <= 0f)
            {
                target.localRotation = rest.localRotation;
                return true;
            }

            Quaternion next = Quaternion.RotateTowards(target.localRotation, rest.localRotation, degPerSec * dt);
            target.localRotation = next;
            return Quaternion.Angle(next, rest.localRotation) <= AngleEpsilon;
        }

        private void SyncPath()
        {
            CopyPathSettings();
            _path.Invalidate();
            _path.EnsureBaked();

            float orbitSign = m_invertOrbit ? -1f : 1f;
            float rollSign = m_invertFilmRoll ? -1f : 1f;
            _orbitDegrees = orbitSign * Mathf.Max(1f, m_revolutions) * 360f;
            _rollDegrees = rollSign * 360f * Mathf.Max(0.01f, m_orbitRadius) * 3f;
            _orbitAxisLocal = SignedAxisUtil.ToVector(m_orbitAxis);
            _rollAxisLocal = SignedAxisUtil.ToVector(m_filmRollAxis);
            _liftAxisLocal = SignedAxisUtil.ToVector(m_carriageAxis);
            _restLift = _carriagePose.valid
                ? SignedAxisUtil.GetComponent(_carriagePose.localPosition, m_carriageAxis)
                : 0f;

            _ribbon.Path = _path;
            _ribbon.NozzleTip = m_nozzleTip;
            _ribbon.FilmWidth = Mathf.Max(0.02f, m_filmWidth);
            _ribbon.ShowFeedStrip = m_showFeedStrip;
            _ribbon.FeedStartLocalOffset = m_feedStartLocalOffset;
            _ribbon.Material = ResolveMaterial();
        }

        private void CopyPathSettings()
        {
            _path.Cargo = m_cargo;
            _path.CuboidSize = m_cuboidSize;
            _path.CuboidCenterLocal = m_cuboidCenterLocal;
            _path.SurfaceOffset = m_surfaceOffset;
            _path.CornerRadius = m_cornerRadius;
            _path.OrbitRadius = m_orbitRadius;
            _path.BottomY = m_bottomY;
            _path.TopY = m_topY;
            _path.Revolutions = m_revolutions;
            _path.UpThenSlightDown = m_upThenSlightDown;
            _path.VerticalDirection = m_verticalDirection;
            _path.SamplesPerRevolution = m_samplesPerRevolution;
        }

        private Material ResolveMaterial()
        {
            if (m_filmMaterial != null)
                return m_filmMaterial;

            if (_runtimeMaterial != null)
                return _runtimeMaterial;

            Shader shader = Shader.Find(FilmShaderName)
                            ?? Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Sprites/Default");
            if (shader == null)
                return null;

            _runtimeMaterial = new Material(shader)
            {
                name = "FilmStretch (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
            if (_runtimeMaterial.HasProperty("_BaseColor"))
                _runtimeMaterial.SetColor("_BaseColor", new Color(0.55f, 0.78f, 0.95f, 0.28f));
            return _runtimeMaterial;
        }

        private void EnsureInit()
        {
            if (_inited)
                return;

            Capture(m_carriage, ref _carriagePose);
            Capture(m_orbitFrame, ref _orbitPose);
            Capture(m_filmRoll, ref _filmRollPose);
            if (_ribbonHost == null)
            {
                var host = new GameObject("FilmRibbon");
                host.transform.SetParent(transform, false);
                _ribbonHost = host.transform;
            }

            _ribbon.Host = _ribbonHost;
            _ribbon.Path = _path;
            _ribbon.Material = ResolveMaterial();
            _ribbon.EnsureMeshes();
            _ribbon.SetVisible(false);
            _inited = true;
        }

        private void SnapIdle()
        {
            _running = false;
            _returning = false;
            _progress = 0f;
            _liftReady = false;
            _wrapStarted = false;
            Restore(m_carriage, _carriagePose);
            Restore(m_orbitFrame, _orbitPose);
            Restore(m_filmRoll, _filmRollPose);
            _ribbon.Clear();
        }

        private static void Capture(Transform target, ref Pose pose)
        {
            pose.valid = target != null;
            if (!pose.valid)
                return;
            pose.localPosition = target.localPosition;
            pose.localRotation = target.localRotation;
        }

        private static void Restore(Transform target, in Pose pose)
        {
            if (!pose.valid)
                return;
            target.localPosition = pose.localPosition;
            target.localRotation = pose.localRotation;
        }

        [Button("测试开始")]
        [ContextMenu("TurntableFilmWrap/测试开始")]
        private void TestStart() => StartAction();

        [Button("测试停止")]
        [ContextMenu("TurntableFilmWrap/测试停止")]
        private void TestStop() => StopAction();

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_surfaceOffset = Mathf.Max(0.005f, m_surfaceOffset);
            m_cornerRadius = Mathf.Max(0f, m_cornerRadius);
            m_cuboidSize = new Vector3(
                Mathf.Max(0.01f, Mathf.Abs(m_cuboidSize.x)),
                Mathf.Max(0.01f, Mathf.Abs(m_cuboidSize.y)),
                Mathf.Max(0.01f, Mathf.Abs(m_cuboidSize.z)));
            m_samplesPerRevolution = Mathf.Clamp(m_samplesPerRevolution, 16, 128);
            m_revolutions = Mathf.Clamp(m_revolutions, 1f, 12f);
            m_filmWidth = Mathf.Max(0.02f, m_filmWidth);
            m_duration = Mathf.Max(0.01f, m_duration);
            m_liftSpeed = Mathf.Max(0f, m_liftSpeed);
            m_liftArriveEpsilon = Mathf.Max(0.0001f, m_liftArriveEpsilon);
            m_returnLiftSpeed = Mathf.Max(0f, m_returnLiftSpeed);
            m_returnAngularSpeed = Mathf.Max(0f, m_returnAngularSpeed);
            if (Application.isPlaying && _inited && _running)
                SyncPath();
        }

        private void OnDrawGizmosSelected()
        {
            if (m_cargo == null)
                return;

            Vector3 size = new Vector3(
                Mathf.Max(0.01f, Mathf.Abs(m_cuboidSize.x)),
                Mathf.Max(0.01f, Mathf.Abs(m_cuboidSize.y)),
                Mathf.Max(0.01f, Mathf.Abs(m_cuboidSize.z)));
            Gizmos.matrix = m_cargo.localToWorldMatrix * Matrix4x4.TRS(m_cuboidCenterLocal, Quaternion.identity, Vector3.one);
            Gizmos.color = new Color(1f, 0.65f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.7f);
            Gizmos.DrawWireCube(Vector3.zero, size + Vector3.one * (m_surfaceOffset * 2f));
            Gizmos.matrix = Matrix4x4.identity;
        }
#endif
    }
}
