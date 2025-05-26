using System.Collections.Generic;
using NonsensicalKit.Tools;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 使用两个传感器值控制运动，通过离开的时机来及时同步
    /// </summary>
    public class Sensor2MovePartMotion : PartMotionBase
    {
        [SerializeField] private Transform m_controlTarget;
        [SerializeField] private JointAxisType m_type; //运动类型
        [SerializeField] private Vector3 m_state1; //传感器1对应的姿态
        [SerializeField] private Vector3 m_state2; //传感器2对应的姿态
        [SerializeField] private float m_time = 0.5f; //运动的时间

        private Tweener _tweener;

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
        }

        private void UpdateState(bool targetIsState1, bool immediately = false)
        {
            if (_tweener != null)
            {
                _tweener.Abort();
            }

            switch (m_type)
            {
                case JointAxisType.Rotation:
                    if (immediately)
                    {
                        m_controlTarget.localEulerAngles = targetIsState1 ? m_state1 : m_state2;
                    }
                    else
                    {
                        _tweener = m_controlTarget.DoLocalRotate(targetIsState1 ? m_state1 : m_state2, m_time);
                    }

                    break;
                case JointAxisType.Position:
                    if (immediately)
                    {
                        m_controlTarget.localPosition = targetIsState1 ? m_state1 : m_state2;
                    }
                    else
                    {
                        _tweener = m_controlTarget.DoLocalMove(targetIsState1 ? m_state1 : m_state2, m_time);
                    }

                    break;
            }
        }

        protected override PartDataInfo GetInfo()
        {
            return new PartDataInfo("双传感移动部件", m_partID,
                new List<PointDataInfo>()
                {
                    new PointDataInfo("传感1", PointDataType.Bool, false),
                    new PointDataInfo("传感2", PointDataType.Bool, false),
                });
        }
    }
}
