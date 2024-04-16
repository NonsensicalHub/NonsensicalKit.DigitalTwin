using NonsensicalKit.Tools;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.PLC
{
    /// <summary>
    /// 使用int索引控制旋转部件对象，如刀库
    /// </summary>
    public class RotatePartMotion : PartMotionBase
    {
        /// <summary>
        /// 控制旋转的对象
        /// </summary>
        public Transform m_ControlTarget;

        /// <summary>
        /// 基础欧拉角
        /// </summary>
        public Vector3 m_BaseEuler;

        /// <summary>
        /// 方向类型
        /// </summary>
        public JointDirType m_DirType;

        /// <summary>
        /// 索引总数
        /// </summary>
        public int m_Count;

        private float _intervalAngle;

        private Tweenner _crtTweener;

        protected override void Init()
        {
            base.Init();
            _intervalAngle = 360 / m_Count;
        }

        protected override void OnReceiveData(List<PLCPoint> part)
        {
            if (_crtTweener != null)
            {
                _crtTweener.Abort();
            }
            int index = (int)long.Parse(part[0].value);
            Vector3 target = Vector3.zero;

            switch (m_DirType)
            {
                case JointDirType.X:
                    target = m_BaseEuler + new Vector3(index * _intervalAngle, 0, 0);
                    break;
                case JointDirType.Y:
                    target = m_BaseEuler + new Vector3(0, index * _intervalAngle, 0);
                    break;
                case JointDirType.Z:
                    target = m_BaseEuler + new Vector3(0, 0, index * _intervalAngle);
                    break;
                case JointDirType.IX:
                    target = m_BaseEuler - new Vector3(index * _intervalAngle, 0, 0);
                    break;
                case JointDirType.IY:
                    target = m_BaseEuler - new Vector3(0, index * _intervalAngle, 0);
                    break;
                case JointDirType.IZ:
                    target = m_BaseEuler - new Vector3(0, 0, index * _intervalAngle);
                    break;
            }
            _crtTweener = m_ControlTarget.DoLocalRotate(Quaternion.Euler(target), _intervalAngle).SetSpeedBased();

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
