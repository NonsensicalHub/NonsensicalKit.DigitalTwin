using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 多轴运动部件运动和行为基类
    /// </summary>
    public class JointsPartMotion : PartMotionBase
    {
        [SerializeField] private JointSetting[] m_joints;
        [SerializeField] private bool m_useInt;

        private long _lastTicks;
        private JointController _controller;

        protected override void Init()
        {
            base.Init();
            _controller = gameObject.AddComponent<JointController>();

            _controller.Joints = m_joints;
        }

        protected override void Dispose()
        {
            base.Dispose();
            Destroy(_controller);
        }

        protected override void OnReceiveData(List<PointData> part)
        {
            if (!_controller)
            {
                return;
            }

            if (part.Count != m_joints.Length)
            {
                return;
            }

            long time = 0;
            if (_lastTicks != 0)
            {
                time = part[0].Ticks - _lastTicks;
            }

            _lastTicks = part[0].Ticks;
            float[] values = new float[m_joints.Length];
            if (m_useInt)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = long.Parse(part[i].Value);
                }
            }
            else
            {
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = float.Parse(part[i].Value);
                }
            }

            _controller.ChangeState(new ActionData(values, time * Magnification));
        }

        protected override PartDataInfo GetInfo()
        {
            PointDataType type = PointDataType.Float;
            if (m_useInt)
            {
                type = PointDataType.Int;
            }

            List<PointDataInfo> v = new List<PointDataInfo>();
            for (int i = 0; i < m_joints.Length; i++)
            {
                v.Add(new PointDataInfo("轴" + i, type, false));
            }

            return new PartDataInfo("多轴运动部件", m_partID, v);
        }
    }
}
