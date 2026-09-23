using System;
using NaughtyAttributes;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 整形机动作：数个对象各自沿指定轴在振幅范围内来回移动。
    /// 不直接读点位；由外部（如 <see cref="RunningStatusPartMotion"/> 的 UnityEvent）调用开关方法。
    /// </summary>
    public class ShaperAction : MonoBehaviour
    {
        /// <summary>振幅相对初始位置的行程范围。</summary>
        public enum AmplitudeSide
        {
            [Tooltip("正负两侧：offset ∈ [-amplitude, +amplitude]")]
            Both = 0,

            [Tooltip("仅正向：offset ∈ [0, +amplitude]")]
            Positive = 1,

            [Tooltip("仅负向：offset ∈ [-amplitude, 0]")]
            Negative = 2,
        }

        [Serializable]
        public class Oscillator
        {
            [Tooltip("往返运动的对象；为空则跳过")]
            public Transform target;

            [Tooltip("本地空间运动轴向")]
            public SignedAxis axis = SignedAxis.PositiveX;

            [Tooltip("振幅（相对初始位置的单侧行程，本地单位）")]
            public float amplitude = 0.05f;

            [Tooltip("振幅相对初始位置：± 双侧 / + 仅正向 / - 仅负向")]
            public AmplitudeSide amplitudeSide = AmplitudeSide.Both;

            [Tooltip("移动速度（本地单位/秒）")]
            public float speed = 0.2f;

            [Tooltip("初始相位偏移（秒）；用于错开多个对象的节拍")]
            public float phaseOffset;

            [Tooltip("启动时朝负向开始（否则朝正向）")]
            public bool startNegative;
        }

        [SerializeField] private Oscillator[] m_oscillators = Array.Empty<Oscillator>();

        [Header("停止")]
        [Tooltip("SetRunning(false) 后是否渐渐回到初始位置；关闭则停在当前位置")]
        [SerializeField] private bool m_smoothReturnOnStop;

        [Tooltip("回初始位速度（本地单位/秒）；≤0 时沿用各 Oscillator.speed")]
        [SerializeField] private float m_returnSpeed;

        private struct RuntimeState
        {
            public Vector3 startLocalPos;
            public Vector3 localAxis;
            public float offset;
            public int direction;
            public bool valid;
        }

        private RuntimeState[] _states = Array.Empty<RuntimeState>();
        private bool _running;
        private bool _returning;
        private bool _captured;

        public bool IsRunning => _running;
        public bool IsReturning => _returning;

        private void Awake()
        {
            CaptureStarts();
            ApplyPhaseOffsets();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            if (_running)
            {
                for (int i = 0; i < _states.Length; i++)
                    StepOscillator(i, dt);
                return;
            }

            if (_returning)
                StepReturn(dt);
        }

        /// <summary>开始往返运动。可挂到 RunningStatus.OnRunning。</summary>
        public void StartAction() => SetRunning(true);

        /// <summary>停止往返运动。可挂到 RunningStatus.OnStopped。</summary>
        public void StopAction() => SetRunning(false);

        /// <summary>按布尔开关。可挂到 RunningStatus.OnStatusChanged。</summary>
        public void SetRunning(bool running)
        {
            if (running)
            {
                _returning = false;
                _running = true;
                return;
            }

            _running = false;
            if (m_smoothReturnOnStop)
                _returning = HasNonZeroOffset();
            else
                _returning = false;
        }

        /// <summary>停在初始位置并清零行程。</summary>
        public void ResetPose()
        {
            EnsureCaptured();
            if (_states == null || m_oscillators == null)
                return;

            _returning = false;
            for (int i = 0; i < _states.Length; i++)
            {
                ref var state = ref _states[i];
                if (!state.valid)
                    continue;

                state.offset = 0f;
                state.direction = m_oscillators[i].startNegative ? -1 : 1;
                ApplyPose(i);
            }
        }

        private void EnsureCaptured()
        {
            if (!_captured)
                CaptureStarts();
        }

        private void CaptureStarts()
        {
            int count = m_oscillators != null ? m_oscillators.Length : 0;
            _states = new RuntimeState[count];

            for (int i = 0; i < count; i++)
            {
                var cfg = m_oscillators[i];
                if (cfg == null || cfg.target == null)
                {
                    _states[i] = default;
                    continue;
                }

                _states[i] = new RuntimeState
                {
                    startLocalPos = cfg.target.localPosition,
                    localAxis = SignedAxisUtil.ToVector(cfg.axis),
                    offset = 0f,
                    direction = cfg.startNegative ? -1 : 1,
                    valid = true,
                };
            }

            _captured = true;
        }

        private void ApplyPhaseOffsets()
        {
            if (m_oscillators == null)
                return;

            for (int i = 0; i < m_oscillators.Length; i++)
            {
                var cfg = m_oscillators[i];
                if (cfg == null || !_states[i].valid)
                    continue;

                if (cfg.phaseOffset > 0f && cfg.speed > 0f)
                    StepOscillator(i, cfg.phaseOffset);
                else
                    ApplyPose(i);
            }
        }

        private void StepOscillator(int index, float dt)
        {
            ref var state = ref _states[index];
            if (!state.valid)
                return;

            var cfg = m_oscillators[index];
            float amp = Mathf.Max(0f, cfg.amplitude);
            float speed = Mathf.Max(0f, cfg.speed);
            if (amp <= 0f || speed <= 0f)
                return;

            GetOffsetBounds(cfg.amplitudeSide, amp, out float minOffset, out float maxOffset);

            state.offset += state.direction * speed * dt;

            if (state.offset > maxOffset)
            {
                state.offset = maxOffset - (state.offset - maxOffset);
                state.direction = -1;
            }
            else if (state.offset < minOffset)
            {
                state.offset = minOffset - (state.offset - minOffset);
                state.direction = 1;
            }

            ApplyPose(index);
        }

        private void StepReturn(float dt)
        {
            bool anyMoving = false;
            for (int i = 0; i < _states.Length; i++)
            {
                ref var state = ref _states[i];
                if (!state.valid)
                    continue;

                if (Mathf.Abs(state.offset) <= 0.0001f)
                {
                    state.offset = 0f;
                    state.direction = m_oscillators[i].startNegative ? -1 : 1;
                    ApplyPose(i);
                    continue;
                }

                float speed = m_returnSpeed > 0f
                    ? m_returnSpeed
                    : Mathf.Max(0f, m_oscillators[i].speed);
                if (speed <= 0f)
                {
                    state.offset = 0f;
                    state.direction = m_oscillators[i].startNegative ? -1 : 1;
                    ApplyPose(i);
                    continue;
                }

                float step = speed * dt;
                if (Mathf.Abs(state.offset) <= step)
                {
                    state.offset = 0f;
                    state.direction = m_oscillators[i].startNegative ? -1 : 1;
                }
                else
                {
                    state.offset -= Mathf.Sign(state.offset) * step;
                    anyMoving = true;
                }

                ApplyPose(i);
            }

            if (!anyMoving)
                _returning = false;
        }

        private bool HasNonZeroOffset()
        {
            if (_states == null)
                return false;

            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i].valid && Mathf.Abs(_states[i].offset) > 0.0001f)
                    return true;
            }

            return false;
        }

        private static void GetOffsetBounds(AmplitudeSide side, float amp, out float minOffset, out float maxOffset)
        {
            switch (side)
            {
                case AmplitudeSide.Positive:
                    minOffset = 0f;
                    maxOffset = amp;
                    break;
                case AmplitudeSide.Negative:
                    minOffset = -amp;
                    maxOffset = 0f;
                    break;
                default:
                    minOffset = -amp;
                    maxOffset = amp;
                    break;
            }
        }

        private void ApplyPose(int index)
        {
            ref var state = ref _states[index];
            if (!state.valid)
                return;

            m_oscillators[index].target.localPosition =
                state.startLocalPos + state.localAxis * state.offset;
        }

        [Button("测试开始")]
        [ContextMenu("ShaperAction/测试开始")]
        private void TestStart() => StartAction();

        [Button("测试停止")]
        [ContextMenu("ShaperAction/测试停止")]
        private void TestStop() => StopAction();

        [Button("复位到初始位置")]
        [ContextMenu("ShaperAction/复位到初始位置")]
        private void TestReset() => ResetPose();
    }
}
