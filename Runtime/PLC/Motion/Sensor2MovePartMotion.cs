using NonsensicalKit.Tools;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.Editor.PLC
{
    /// <summary>
    /// 使用两个传感器值控制运动，通过离开的时机来及时同步
    /// </summary>
    public  class Sensor2MovePartMotion : PartMotionBase
    {
        public Transform m_ControlTarget;
        public JointAxisType m_Type;       //运动类型
        public Vector3 m_State1;      //传感器1对应的姿态
        public Vector3 m_State2;      //传感器2对应的姿态
        public float m_Time = 0.5f;          //运动的时间

        private Tweenner _tweenner;

        private bool _first;
        private bool _check1;
        private bool _check2;

        protected override void Init()
        {
            base.Init();

            _first = true;
        }

        protected override void OnReceiveData(List<PLCPoint> part)
        {
            if (_first)
            {
                _first = false;
                _check1 = bool.Parse(part[0].value);
                _check2 = bool.Parse(part[1].value);
                UpdateState(_check1 ? true : false, true);
                return;
            }

            if (_check1 && !bool.Parse(part[0].value)   )
            {
                UpdateState(false);
            }
            else if (_check2 && !bool.Parse(part[1].value)  )
            {
                UpdateState(true);
            }
            _check1 = bool.Parse(part[0].value);
            _check2 = bool.Parse(part[1].value) ;
        }

        private void UpdateState(bool targetIsState1, bool immediately = false)
        {
            if (_tweenner != null)
            {
                _tweenner.Abort();
            }
            switch (m_Type)
            {
                case JointAxisType.Rotation:
                    if (immediately)
                    {
                        m_ControlTarget.localEulerAngles = targetIsState1 ? m_State1 : m_State2;
                    }
                    else
                    {
                        _tweenner = m_ControlTarget.DoLocalRotate(targetIsState1 ? m_State1 : m_State2, m_Time);
                    }
                    break;
                case JointAxisType.Position:
                    if (immediately)
                    {
                        m_ControlTarget.localPosition = targetIsState1 ? m_State1 : m_State2;
                    }
                    else
                    {
                        _tweenner = m_ControlTarget.DoLocalMove(targetIsState1 ? m_State1 : m_State2, m_Time);
                    }
                    break;
            }
        }

        protected override PLCPartInfo GetInfo()
        {
            return new PLCPartInfo("双传感移动部件", m_partID,
                new List<PLCPointInfo>() {
                new PLCPointInfo("传感1",PLCDataType.Bit,false),
                new PLCPointInfo("传感2",PLCDataType.Bit,false),
                });
        }
    }
}
