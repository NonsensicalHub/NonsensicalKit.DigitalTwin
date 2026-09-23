using System;
using System.Collections.Generic;
using System.Globalization;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Events;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 读取布尔运行状态点位，状态变化时触发 UnityEvent。
    /// 支持单信号或多信号 AND 判定；可在 Inspector 绑定动作组件开关方法（如整形机）。
    /// </summary>
    public class RunningStatusPartMotion : PartMotionBase
    {
        private const string DefaultRunningPoint = "Running";

        [Serializable]
        public class BoolSignalRule
        {
            [Tooltip("点位码，如 Running / AllowInfeed / RequestOutfeed")]
            public string pointCode;

            [Tooltip("期望为 true；false 表示对该点取反后再参与 AND")]
            public bool expectTrue = true;

            [Tooltip("找不到 PointCode 时的下标兜底；-1 表示不兜底")]
            public int fallbackIndex = -1;
        }

        [Header("开关")]
        [Tooltip("为 false 时强制判定为停止，不启动动作")]
        [SerializeField] public bool m_enableMotion = true;

        [Header("点位")]
        [Tooltip("单信号模式点位码；规则列表为空时使用。为空则使用首个点位")]
        [SerializeField] private string m_runningPointCode = DefaultRunningPoint;

        [Tooltip("多信号 AND 规则；为空时退回 m_runningPointCode 单信号判定")]
        [SerializeField] private BoolSignalRule[] m_signalRules = Array.Empty<BoolSignalRule>();

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
        public bool EnableMotion
        {
            get => m_enableMotion;
            set
            {
                if (m_enableMotion == value)
                    return;
                m_enableMotion = value;
                if (!value && _running)
                    ApplyStatus(false);
            }
        }

        public UnityEvent<bool> OnStatusChanged => m_onStatusChanged;
        public UnityEvent OnRunning => m_onRunning;
        public UnityEvent OnStopped => m_onStopped;

        protected override void OnReceiveData(List<PointData> part)
        {
            if (part == null || part.Count == 0)
                return;

            bool next = m_enableMotion && EvaluateRunning(part);
            ApplyStatus(next);
        }

        /// <summary>外部或调试用：直接设置运行状态并触发事件。</summary>
        public void SetRunning(bool running) => ApplyStatus(running);

        private bool EvaluateRunning(List<PointData> part)
        {
            if (m_signalRules == null || m_signalRules.Length == 0)
                return ReadBool(part, m_runningPointCode, 0, _running);

            for (int i = 0; i < m_signalRules.Length; i++)
            {
                var rule = m_signalRules[i];
                if (rule == null)
                    continue;

                bool value = ReadBool(part, rule.pointCode, rule.fallbackIndex, !rule.expectTrue);
                if (value != rule.expectTrue)
                    return false;
            }

            return true;
        }

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

        protected override PartDataInfo GetInfo()
        {
            var points = new List<PointDataInfo>();
            if (m_signalRules != null && m_signalRules.Length > 0)
            {
                for (int i = 0; i < m_signalRules.Length; i++)
                {
                    var rule = m_signalRules[i];
                    if (rule == null || string.IsNullOrEmpty(rule.pointCode))
                        continue;
                    points.Add(new PointDataInfo(rule.pointCode, PointDataType.Bool, false));
                }
            }

            if (points.Count == 0)
                points.Add(new PointDataInfo("运行状态", PointDataType.Bool, false));

            return new PartDataInfo("运行状态", m_partID, points);
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
