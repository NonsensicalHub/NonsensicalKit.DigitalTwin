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
        [Serializable]
        public class Oscillator
        {
            [Tooltip("往返运动的对象；为空则跳过")]
            public Transform target;

            [Tooltip("本地空间运动轴向")]
            public SignedAxis axis = SignedAxis.PositiveX;

            [Tooltip("振幅（相对初始位置的单侧行程，本地单位）")]
            public float amplitude = 0.05f;

            [Tooltip("移动速度（本地单位/秒）")]
            public float speed = 0.2f;

            [Tooltip("初始相位偏移（秒）；用于错开多个对象的节拍")]
            public float phaseOffset;

            [Tooltip("启动时朝负向开始（否则朝正向）")]
            public bool startNegative;
        }

        [SerializeField] private Oscillator[] m_oscillators = Array.Empty<Oscillator>();

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
        private bool _captured;

        public bool IsRunning => _running;

        private void Awake()
        {
            CaptureStarts();
            ApplyPhaseOffsets();
        }

        private void Update()
        {
            if (!_running)
                return;

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            for (int i = 0; i < _states.Length; i++)
                StepOscillator(i, dt);
        }

        /// <summary>开始往返运动。可挂到 RunningStatus.OnRunning。</summary>
        public void StartAction() => SetRunning(true);

        /// <summary>停止往返运动，保持当前位置。可挂到 RunningStatus.OnStopped。</summary>
        public void StopAction() => SetRunning(false);

        /// <summary>按布尔开关。可挂到 RunningStatus.OnStatusChanged。</summary>
        public void SetRunning(bool running) => _running = running;

        /// <summary>停在初始位置并清零行程。</summary>
        public void ResetPose()
        {
            EnsureCaptured();
            if (_states == null || m_oscillators == null)
                return;

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

            state.offset += state.direction * speed * dt;

            if (state.offset > amp)
            {
                state.offset = amp - (state.offset - amp);
                state.direction = -1;
            }
            else if (state.offset < -amp)
            {
                state.offset = -amp - (state.offset + amp);
                state.direction = 1;
            }

            ApplyPose(index);
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
