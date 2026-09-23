using NaughtyAttributes;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 悬臂式缠膜机,货不动,悬臂带动薄膜卷旋转缠膜
    /// 缠膜机动作：转臂绕货、滑架升降、膜卷自转，并在货物表面生成螺旋膜带。
    /// 不读点位；由 <see cref="RunningStatusPartMotion"/> 的 UnityEvent 调用开关。
    /// 运行时按一轮时长循环；停止时瞬间回到初始姿态并清空膜。
    /// </summary>
    public class FilmWrapAction : MonoBehaviour
    {
        private const string FilmShaderName = "NonsensicalKit/DigitalTwin/FilmStretch";

        [Header("包裹目标")]
        [Tooltip("货物根节点；螺旋与膜带都相对它")]
        [SerializeField] private Transform m_cargo;
        [Tooltip("货物本地立方体尺寸")]
        [SerializeField] private Vector3 m_cuboidSize = new Vector3(1f, 1.2f, 1f);
        [SerializeField] private Vector3 m_cuboidCenterLocal;
        [SerializeField] [Min(0.005f)] private float m_surfaceOffset = 0.04f;
        [SerializeField] [Min(0f)] private float m_cornerRadius = 0.04f;

        [Header("螺旋")]
        [Tooltip("转臂绕行半径（货物本地水平面）")]
        [SerializeField] private float m_orbitRadius = 1.45f;
        [Tooltip("相对货物原点的起始高度")]
        [SerializeField] private float m_bottomY = 0.2f;
        [Tooltip("相对货物原点的最高高度")]
        [SerializeField] private float m_topY = 1.55f;
        [SerializeField] [Range(1f, 12f)] private float m_revolutions = 6f;
        [Tooltip("先升到顶再略降，否则匀速上升")]
        [SerializeField] private bool m_upThenSlightDown = true;
        [Tooltip("自下而上 / 自上而下")]
        [SerializeField] private FilmWrapVerticalDirection m_verticalDirection =
            FilmWrapVerticalDirection.BottomToTop;
        [SerializeField] [Range(16, 128)] private int m_samplesPerRevolution = 64;

        [Header("机构")]
        [Tooltip("转臂枢轴；运行时朝向喷嘴，停止时恢复初始旋转")]
        [SerializeField] private Transform m_armPivot;
        [Tooltip("升降滑架；若是转臂子节点则只改本地 Y")]
        [SerializeField] private Transform m_carriage;
        [Tooltip("膜卷；运行时绕本地 X 自转")]
        [SerializeField] private Transform m_filmRoll;
        [Tooltip("出膜点；为空则不画出膜条")]
        [SerializeField] private Transform m_nozzleTip;

        [Header("膜带")]
        [SerializeField] private float m_filmWidth = 0.28f;
        [SerializeField] private bool m_showFeedStrip = true;
        [SerializeField] private Vector3 m_feedStartLocalOffset = new Vector3(-0.12f, 0f, -0.05f);
        [Tooltip("为空则运行时使用 DigitalTwin/FilmStretch")]
        [SerializeField] private Material m_filmMaterial;

        [Header("播放")]
        [Tooltip("一轮缠膜耗时（秒），到顶后从头循环")]
        [SerializeField] private float m_duration = 14f;

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
        private Pose _armPose;
        private Pose _carriagePose;
        private Pose _filmRollPose;
        private bool _inited;
        private bool _running;
        private float _progress;

        public bool IsRunning => _running;
        public float Progress => _progress;

        private void Awake()
        {
            EnsureInit();
        }

        private void OnDisable()
        {
            if (_inited)
                SnapToDefault();
        }

        private void OnDestroy()
        {
            _ribbon.Dispose();
            if (_runtimeMaterial != null)
                Destroy(_runtimeMaterial);
        }

        private void Update()
        {
            if (!_running)
                return;

            float duration = Mathf.Max(0.01f, m_duration);
            _progress += Time.deltaTime / duration;
            if (_progress >= 1f)
                _progress %= 1f;

            ApplyProgress(_progress);
        }

        /// <summary>开始循环缠膜。可挂到 RunningStatus.OnRunning。</summary>
        public void StartAction() => SetRunning(true);

        /// <summary>停止并瞬间回到默认状态。可挂到 RunningStatus.OnStopped。</summary>
        public void StopAction() => SetRunning(false);

        /// <summary>按布尔开关。可挂到 RunningStatus.OnStatusChanged。</summary>
        public void SetRunning(bool running)
        {
            if (!Application.isPlaying)
                return;

            EnsureInit();
            if (_running == running)
                return;

            _running = running;
            if (!running)
            {
                SnapToDefault();
                return;
            }

            _progress = 0f;
            SyncPath();
            ApplyProgress(0f);
        }

        /// <summary>瞬间回到初始姿态并清空膜。不改变当前运行开关。</summary>
        public void ResetPose()
        {
            EnsureInit();
            _progress = 0f;
            RestorePoses();
            _ribbon.Clear();
        }

        [Button("从货物包围盒拟合尺寸")]
        [ContextMenu("FilmWrap/从货物包围盒拟合尺寸")]
        public void FitCuboidFromCargo()
        {
            CopyPathSettings();
            _path.AutoFitFromCargoBounds();
            m_cuboidSize = _path.CuboidSize;
            m_cuboidCenterLocal = _path.CuboidCenterLocal;
            _path.Invalidate();
        }

        private void EnsureInit()
        {
            if (_inited)
                return;

            CapturePose(m_armPivot, ref _armPose);
            CapturePose(m_carriage, ref _carriagePose);
            CapturePose(m_filmRoll, ref _filmRollPose);
            EnsureRibbonHost();
            _inited = true;
        }

        private void EnsureRibbonHost()
        {
            if (_ribbonHost == null)
            {
                var host = new GameObject("FilmRibbon");
                host.transform.SetParent(transform, false);
                _ribbonHost = host.transform;
            }

            _ribbon.Host = _ribbonHost;
            _ribbon.Path = _path;
            EnsureMaterial();
            _ribbon.EnsureMeshes();
            _ribbon.SetVisible(false);
        }

        private void EnsureMaterial()
        {
            Material mat = m_filmMaterial;
            if (mat == null)
            {
                if (_runtimeMaterial == null)
                {
                    Shader shader = Shader.Find(FilmShaderName);
                    if (shader == null)
                        shader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (shader == null)
                        shader = Shader.Find("Sprites/Default");
                    if (shader != null)
                    {
                        _runtimeMaterial = new Material(shader)
                        {
                            name = "FilmStretch (Runtime)",
                            hideFlags = HideFlags.HideAndDontSave
                        };
                        if (_runtimeMaterial.HasProperty("_BaseColor"))
                            _runtimeMaterial.SetColor("_BaseColor", new Color(0.55f, 0.78f, 0.95f, 0.28f));
                    }
                }

                mat = _runtimeMaterial;
            }

            _ribbon.Material = mat;
        }

        private void SyncPath()
        {
            CopyPathSettings();
            _path.Invalidate();

            _ribbon.Path = _path;
            _ribbon.NozzleTip = m_nozzleTip;
            _ribbon.FilmWidth = Mathf.Max(0.02f, m_filmWidth);
            _ribbon.ShowFeedStrip = m_showFeedStrip;
            _ribbon.FeedStartLocalOffset = m_feedStartLocalOffset;
            EnsureMaterial();
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

        private void ApplyProgress(float progress)
        {
            if (m_cargo == null)
                return;

            _path.EnsureBaked();
            FilmWrapSample sample = _path.Evaluate(progress);
            ApplyMechanism(sample, progress);
            _ribbon.BuildUntil(progress);
        }

        private void ApplyMechanism(in FilmWrapSample sample, float progress)
        {
            Vector3 center = m_cargo != null ? m_cargo.position : Vector3.zero;
            Vector3 up = m_cargo != null ? m_cargo.up : Vector3.up;

            if (m_armPivot != null)
            {
                Vector3 flat = sample.nozzleWorld - center;
                flat -= up * Vector3.Dot(flat, up);
                if (flat.sqrMagnitude > 1e-6f)
                    m_armPivot.rotation = Quaternion.LookRotation(flat, up);
            }

            if (m_carriage != null)
            {
                if (m_armPivot != null && m_carriage.IsChildOf(m_armPivot))
                {
                    Vector3 lp = m_carriage.localPosition;
                    lp.y = Vector3.Dot(sample.nozzleWorld - m_armPivot.position, m_armPivot.up);
                    m_carriage.localPosition = lp;
                }
                else
                {
                    m_carriage.position = sample.nozzleWorld;
                }
            }

            if (m_filmRoll != null)
            {
                float radius = Mathf.Max(0.01f, m_orbitRadius);
                m_filmRoll.localRotation = Quaternion.Euler(progress * 360f * radius * 3f, 0f, 0f);
            }
        }

        private void SnapToDefault()
        {
            _running = false;
            _progress = 0f;
            RestorePoses();
            _ribbon.Clear();
        }

        private void RestorePoses()
        {
            RestorePose(m_armPivot, _armPose);
            RestorePose(m_carriage, _carriagePose);
            RestorePose(m_filmRoll, _filmRollPose);
        }

        private static void CapturePose(Transform target, ref Pose pose)
        {
            pose.valid = target != null;
            if (!pose.valid)
                return;
            pose.localPosition = target.localPosition;
            pose.localRotation = target.localRotation;
        }

        private static void RestorePose(Transform target, Pose pose)
        {
            if (!pose.valid || target == null)
                return;
            target.localPosition = pose.localPosition;
            target.localRotation = pose.localRotation;
        }

        [Button("测试开始")]
        [ContextMenu("FilmWrap/测试开始")]
        private void TestStart() => StartAction();

        [Button("测试停止")]
        [ContextMenu("FilmWrap/测试停止")]
        private void TestStop() => StopAction();

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_surfaceOffset = Mathf.Max(0.005f, m_surfaceOffset);
            m_cornerRadius = Mathf.Max(0f, m_cornerRadius);
            m_cuboidSize.x = Mathf.Max(0.01f, Mathf.Abs(m_cuboidSize.x));
            m_cuboidSize.y = Mathf.Max(0.01f, Mathf.Abs(m_cuboidSize.y));
            m_cuboidSize.z = Mathf.Max(0.01f, Mathf.Abs(m_cuboidSize.z));
            m_samplesPerRevolution = Mathf.Clamp(m_samplesPerRevolution, 16, 128);
            m_revolutions = Mathf.Clamp(m_revolutions, 1f, 12f);
            m_filmWidth = Mathf.Max(0.02f, m_filmWidth);
            m_duration = Mathf.Max(0.01f, m_duration);
            if (Application.isPlaying && _inited)
                SyncPath();
        }

        private void OnDrawGizmosSelected()
        {
            if (m_cargo == null)
                return;

            Vector3 size = m_cuboidSize;
            size.x = Mathf.Max(0.01f, Mathf.Abs(size.x));
            size.y = Mathf.Max(0.01f, Mathf.Abs(size.y));
            size.z = Mathf.Max(0.01f, Mathf.Abs(size.z));
            Vector3 expanded = size + Vector3.one * (m_surfaceOffset * 2f);
            Gizmos.matrix = m_cargo.localToWorldMatrix * Matrix4x4.TRS(m_cuboidCenterLocal, Quaternion.identity, Vector3.one);
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.color = new Color(0.2f, 1f, 0.45f, 0.7f);
            Gizmos.DrawWireCube(Vector3.zero, expanded);
            Gizmos.matrix = Matrix4x4.identity;
        }
#endif
    }
}
