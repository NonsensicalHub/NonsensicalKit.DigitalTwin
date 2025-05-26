using System.Collections.Generic;
using NonsensicalKit.Tools;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 使用int索引控制旋转部件对象，如刀库
    /// </summary>
    public class RotatePartMotion : PartMotionBase
    {
        /// <summary>
        /// 控制旋转的对象
        /// </summary>
        [SerializeField] private Transform m_controlTarget;

        /// <summary>
        /// 基础欧拉角
        /// </summary>
        [SerializeField] private Vector3 m_baseEuler;

        /// <summary>
        /// 方向类型
        /// </summary>
        [SerializeField] private JointDirType m_dirType;

        /// <summary>
        /// 索引总数
        /// </summary>
        [SerializeField] private int m_count;

        private float _intervalAngle;

        private Tweener _crtTweener;

        protected override void Init()
        {
            base.Init();
            _intervalAngle = 360f / m_count;
        }

        protected override void OnReceiveData(List<PointData> part)
        {
            if (_crtTweener != null)
            {
                _crtTweener.Abort();
            }

            int index = (int)long.Parse(part[0].Value);
            Vector3 target = Vector3.zero;

            switch (m_dirType)
            {
                case JointDirType.X:
                    target = m_baseEuler + new Vector3(index * _intervalAngle, 0, 0);
                    break;
                case JointDirType.Y:
                    target = m_baseEuler + new Vector3(0, index * _intervalAngle, 0);
                    break;
                case JointDirType.Z:
                    target = m_baseEuler + new Vector3(0, 0, index * _intervalAngle);
                    break;
                case JointDirType.IX:
                    target = m_baseEuler - new Vector3(index * _intervalAngle, 0, 0);
                    break;
                case JointDirType.IY:
                    target = m_baseEuler - new Vector3(0, index * _intervalAngle, 0);
                    break;
                case JointDirType.IZ:
                    target = m_baseEuler - new Vector3(0, 0, index * _intervalAngle);
                    break;
            }

            _crtTweener = m_controlTarget.DoLocalRotate(Quaternion.Euler(target), _intervalAngle).SetSpeedBased();
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
