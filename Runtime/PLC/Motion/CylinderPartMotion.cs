using NonsensicalKit.Tools;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.PLC
{
    /// <summary>
    /// 气缸运动，一个信号可能操控多个气缸
    /// 需要注意的时，默认将初始状态作为气缸未运行时状态
    /// </summary>
    public class CylinderPartMotion : PartMotionBase
    {
        public Transform[] m_ControlTarget;       //操作对象
        public Vector3[] m_TargetLocalPosition;   //气缸开启时的目标位置
        public Vector3[] m_TargetLocalEuler;      //气缸开启时的目标欧拉角
        public float m_TimeRequired = 0.5f;     //完成气缸运动所需时间

        private bool _crtState;
        private Vector3[] _originPos;
        private Quaternion[] _originRot;
        private Quaternion[] _targetRot;

        protected override void Init()
        {
            base.Init();

            var length = m_ControlTarget.Length;
            _originPos = new Vector3[length];
            _originRot = new Quaternion[length];
            _targetRot = new Quaternion[length];
            for (int i = 0; i < length; i++)
            {
                _originPos[i] = m_ControlTarget[i].localPosition;
                _originRot[i] = m_ControlTarget[i].localRotation;
                _targetRot[i] = Quaternion.Euler(m_TargetLocalEuler[i]);
            }
        }

        protected override void OnReceiveData(List<PLCPoint> part)
        {
            if (_crtState != bool.Parse(part[0].value))
            {
                _crtState = bool.Parse(part[0].value);
                if (_crtState)
                {
                    for (int i = 0; i < m_ControlTarget.Length; i++)
                    {
                        m_ControlTarget[i].DoLocalMove(m_TargetLocalPosition[i], m_TimeRequired);
                        m_ControlTarget[i].DoLocalRotate(_targetRot[i], m_TimeRequired);
                    }   
                }
                else
                {
                    for (int i = 0; i < m_ControlTarget.Length; i++)
                    {
                        m_ControlTarget[i].DoLocalMove(_originPos[i], m_TimeRequired);
                        m_ControlTarget[i].DoLocalRotate(_originRot[i], m_TimeRequired);
                    }
                }
            }
        }

        protected override PLCPartInfo GetInfo()
        {
            return new PLCPartInfo("等角度旋转部件", m_partID,
                new List<PLCPointInfo>() {
                new PLCPointInfo("角度索引",PLCDataType.Int,false),
                });
        }
    }
}
