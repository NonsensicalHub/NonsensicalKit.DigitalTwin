using System.Collections.Generic;
using NonsensicalKit.Tools;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 夹爪部件，默认使用夹紧松开两个传感器数据
    /// </summary>
    public class GripperPartMotion : PartMotionBase
    {
        public Transform m_ControlTarget1;
        public Transform m_ControlTarget2;
        public Transform m_Cylinder;
        public JointAxisType m_Type; //运动类型
        public Vector3 m_Part1State1; //传感器1对应的姿态
        public Vector3 m_Part1State2; //传感器2对应的姿态
        public Vector3 m_Part2State1; //传感器1对应的姿态
        public Vector3 m_Part2State2; //传感器2对应的姿态
        public float m_Time = 0.5f; //运动的时间

        private Tweener _tweener1;
        private Tweener _tweener2;

        private bool _first;
        private bool _check1;
        private bool _check2;

        protected override void Init()
        {
            base.Init();

            _first = true;
        }

        protected override void OnReceiveData(List<PointData> part)
        {
            if (_first)
            {
                _first = false;
                _check1 = bool.Parse(part[0].value);
                _check2 = bool.Parse(part[1].value);
                UpdateState(_check1, true);
                return;
            }

            if (_check1 && !bool.Parse(part[0].value))
            {
                UpdateState(false);
            }
            else if (_check2 && !bool.Parse(part[1].value))
            {
                UpdateState(true);
            }

            _check1 = bool.Parse(part[0].value);
            _check2 = bool.Parse(part[1].value);
            if (m_Cylinder != null)
            {
                m_Cylinder.gameObject.SetActive(bool.Parse(part[0].value));
            }
        }

        private void UpdateState(bool targetIsState1, bool immediately = false)
        {
            Vector3 targetState1 = targetIsState1 ? m_Part1State2 : m_Part1State1;
            Vector3 targetState2 = targetIsState1 ? m_Part2State2 : m_Part2State1;
            if (_tweener1 != null)
            {
                _tweener1.Abort();
                _tweener2.Abort();
            }

            switch (m_Type)
            {
                case JointAxisType.Rotation:
                    if (immediately)
                    {
                        m_ControlTarget1.localEulerAngles = targetState1;
                        m_ControlTarget2.localEulerAngles = targetState2;
                    }
                    else
                    {
                        _tweener1 = m_ControlTarget1.DoLocalRotate(targetState1, m_Time);
                        _tweener2 = m_ControlTarget2.DoLocalRotate(targetState2, m_Time);
                    }

                    break;
                case JointAxisType.Position:
                    if (immediately)
                    {
                        m_ControlTarget1.localPosition = targetState1;
                        m_ControlTarget2.localPosition = targetState2;
                    }
                    else
                    {
                        _tweener1 = m_ControlTarget1.DoLocalMove(targetState1, m_Time);
                        _tweener2 = m_ControlTarget2.DoLocalMove(targetState2, m_Time);
                    }

                    break;
            }
        }

        protected override PLCPartInfo GetInfo()
        {
            return new PLCPartInfo("双传感夹爪部件", m_partID,
                new List<PLCPointInfo>()
                {
                    new PLCPointInfo("传感1", PointDataType.Bool, false),
                    new PLCPointInfo("传感2", PointDataType.Bool, false),
                });
        }
    }
}
