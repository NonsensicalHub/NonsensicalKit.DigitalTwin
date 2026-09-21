using System.Collections.Generic;
using System.Globalization;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Events;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 读取布尔运行状态点位，状态变化时触发 UnityEvent。
    /// 可在 Inspector 中绑定任意动作组件的开关方法（如整形机、振动、指示灯等）。
    /// </summary>
    public class RunningStatusPartMotion : PartMotionBase
    {
        private const string DefaultRunningPoint = "Running";

        [Header("点位")]
        [Tooltip("运行状态点位码；为空则使用首个点位")]
        [SerializeField] private string m_runningPointCode = DefaultRunningPoint;

        [Header("事件")]
        [Tooltip("状态变化时调用，参数为当前是否运行")]
        [SerializeField] private UnityEvent<bool> m_onStatusChanged = new();

        [Tooltip("变为运行时调用")]
        [SerializeField] private UnityEvent m_onRunning = new();

        [Tooltip("变为停止时调用")]
        [SerializeField] private UnityEvent m_onStopped = new();

        [Header("调试")]
        [SerializeField] private bool m_testRunning;

        private bool _running;
        private bool _hasValue;

        public bool IsRunning => _running;
        public UnityEvent<bool> OnStatusChanged => m_onStatusChanged;
        public UnityEvent OnRunning => m_onRunning;
        public UnityEvent OnStopped => m_onStopped;

        protected override void OnReceiveData(List<PointData> part)
        {
            if (part == null || part.Count == 0)
                return;

            bool next = ReadBool(part, m_runningPointCode, 0, _running);
            ApplyStatus(next);
        }

        /// <summary>外部或调试用：直接设置运行状态并触发事件。</summary>
        public void SetRunning(bool running) => ApplyStatus(running);

        private void ApplyStatus(bool running)
        {
            if (_hasValue && _running == running)
                return;

            _hasValue = true;
            _running = running;
            m_testRunning = running;

            m_onStatusChanged?.Invoke(running);
            if (running)
                m_onRunning?.Invoke();
            else
                m_onStopped?.Invoke();
        }

        protected override PartDataInfo GetInfo() =>
            new PartDataInfo("运行状态", m_partID,
                new List<PointDataInfo>
                {
                    new PointDataInfo("运行状态", PointDataType.Bool, false),
                });

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
            if (string.IsNullOrEmpty(raw))
                return fallback;

            if (bool.TryParse(raw, out bool b))
                return b;

            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                return n != 0;

            return fallback;
        }

        [Button("测试设为运行")]
        [ContextMenu("RunningStatus/测试设为运行")]
        private void TestStart() => SetRunning(true);

        [Button("测试设为停止")]
        [ContextMenu("RunningStatus/测试设为停止")]
        private void TestStop() => SetRunning(false);

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying && m_testRunning != _running)
                SetRunning(m_testRunning);
        }
#endif
    }
}
