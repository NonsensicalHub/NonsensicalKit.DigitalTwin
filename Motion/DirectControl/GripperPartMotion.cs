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
        [SerializeField] private Transform m_controlTarget1;
        [SerializeField] private Transform m_controlTarget2;
        [SerializeField] private Transform m_cylinder;
        [SerializeField] private JointAxisType m_type; //运动类型
        [SerializeField] private Vector3 m_part1State1; //传感器1对应的姿态
        [SerializeField] private Vector3 m_part1State2; //传感器2对应的姿态
        [SerializeField] private Vector3 m_part2State1; //传感器1对应的姿态
        [SerializeField] private Vector3 m_part2State2; //传感器2对应的姿态
        [SerializeField] private float m_time = 0.5f; //运动的时间

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
                _check1 = bool.Parse(part[0].Value);
                _check2 = bool.Parse(part[1].Value);
                UpdateState(_check1, true);
                return;
            }

            if (_check1 && !bool.Parse(part[0].Value))
            {
                UpdateState(false);
            }
            else if (_check2 && !bool.Parse(part[1].Value))
            {
                UpdateState(true);
            }

            _check1 = bool.Parse(part[0].Value);
            _check2 = bool.Parse(part[1].Value);
            if (m_cylinder != null)
            {
                m_cylinder.gameObject.SetActive(bool.Parse(part[0].Value));
            }
        }

        private void UpdateState(bool targetIsState1, bool immediately = false)
        {
            Vector3 targetState1 = targetIsState1 ? m_part1State2 : m_part1State1;
            Vector3 targetState2 = targetIsState1 ? m_part2State2 : m_part2State1;
            if (_tweener1 != null)
            {
                _tweener1.Abort();
                _tweener2.Abort();
            }

            switch (m_type)
            {
                case JointAxisType.Rotation:
                    if (immediately)
                    {
                        m_controlTarget1.localEulerAngles = targetState1;
                        m_controlTarget2.localEulerAngles = targetState2;
                    }
                    else
                    {
                        _tweener1 = m_controlTarget1.DoLocalRotate(targetState1, m_time);
                        _tweener2 = m_controlTarget2.DoLocalRotate(targetState2, m_time);
                    }

                    break;
                case JointAxisType.Position:
                    if (immediately)
                    {
                        m_controlTarget1.localPosition = targetState1;
                        m_controlTarget2.localPosition = targetState2;
                    }
                    else
                    {
                        _tweener1 = m_controlTarget1.DoLocalMove(targetState1, m_time);
                        _tweener2 = m_controlTarget2.DoLocalMove(targetState2, m_time);
                    }

                    break;
            }
        }

        protected override PartDataInfo GetInfo()
        {
            return new PartDataInfo("双传感夹爪部件", m_partID,
                new List<PointDataInfo>()
                {
                    new PointDataInfo("传感1", PointDataType.Bool, false),
                    new PointDataInfo("传感2", PointDataType.Bool, false),
                });
        }
    }
}
