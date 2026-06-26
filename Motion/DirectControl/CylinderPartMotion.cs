using System.Collections.Generic;
using NonsensicalKit.Tools;
using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 气缸运动，一个信号可能操控多个气缸
    /// 需要注意的时，默认将初始状态作为气缸未运行时状态
    /// </summary>
    public class CylinderPartMotion : PartMotionBase
    {
        [SerializeField] private Transform[] m_controlTarget; //操作对象
         [SerializeField] private Vector3[] m_targetLocalPosition; //气缸开启时的目标位置
         [SerializeField] private Vector3[] m_targetLocalEuler; //气缸开启时的目标欧拉角
        [SerializeField] private float m_timeRequired = 0.5f; //完成气缸运动所需时间

        private bool _crtState;
        private Vector3[] _originPos;
        private Quaternion[] _originRot;
        private Quaternion[] _targetRot;

        protected override void Init()
        {
            base.Init();

            var length = m_controlTarget.Length;
            _originPos = new Vector3[length];
            _originRot = new Quaternion[length];
            _targetRot = new Quaternion[length];
            for (int i = 0; i < length; i++)
            {
                _originPos[i] = m_controlTarget[i].localPosition;
                _originRot[i] = m_controlTarget[i].localRotation;
                _targetRot[i] = Quaternion.Euler(m_targetLocalEuler[i]);
            }
        }

        protected override void OnReceiveData(List<PointData> part)
        {
            if (_crtState != bool.Parse(part[0].Value))
            {
                _crtState = bool.Parse(part[0].Value);
                if (_crtState)
                {
                    for (int i = 0; i < m_controlTarget.Length; i++)
                    {
                        m_controlTarget[i].DoLocalMove(m_targetLocalPosition[i], m_timeRequired);
                        m_controlTarget[i].DoLocalRotate(_targetRot[i], m_timeRequired);
                    }
                }
                else
                {
                    for (int i = 0; i < m_controlTarget.Length; i++)
                    {
                        m_controlTarget[i].DoLocalMove(_originPos[i], m_timeRequired);
                        m_controlTarget[i].DoLocalRotate(_originRot[i], m_timeRequired);
                    }
                }
            }
        }

        protected override PartDataInfo GetInfo()
        {
            return new PartDataInfo("等角度旋转部件", m_partID,
                new List<PointDataInfo>()
                {
                    new PointDataInfo("角度索引", PointDataType.Int, false),
                });
        }
    }
}
