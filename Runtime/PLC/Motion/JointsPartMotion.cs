using System.Collections.Generic;

namespace NonsensicalKit.Editor.PLC
{
    /// <summary>
    /// 多轴运动部件运动和行为基类
    /// </summary>
    public  class JointsPartMotion : PartMotionBase
    {
        public JointSetting[] m_Joints;
        public bool m_UseLong;

        protected long _lastTicks;
        protected JointController _controller;

        protected override void Init()
        {
            base.Init();
            _controller = gameObject.AddComponent<JointController>();

            _controller.Joints = m_Joints;
        }

        protected override void Dispose()
        {
            base.Dispose();
            Destroy(_controller);
        }

        protected override void OnReceiveData(List<PLCPoint> part)
        {
            if (!_controller)
            {
                return;
            }

            if (part.Count != m_Joints.Length)
            {
                return;
            }
            long time = 0;
            if (_lastTicks != 0)
            {
                time = part[0].ticks - _lastTicks;
            }
            _lastTicks = part[0].ticks;
            float[] values = new float[m_Joints.Length];
            if (m_UseLong)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = long.Parse(part[i].value);
                }
            }
            else
            {
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = float.Parse(part[i].value);
                }
            }
            _controller.ChangeState(new ActionData(values, time * _magnification));
        }

        protected override PLCPartInfo GetInfo()
        {
            PLCDataType type = PLCDataType.Real;
            if (m_UseLong)
            {
                type = PLCDataType.DInt;
            }
            List<PLCPointInfo> v = new List<PLCPointInfo>();
            for (int i = 0; i < m_Joints.Length; i++)
            {
                v.Add(new PLCPointInfo("轴" + i, type, false));
            }
            return new PLCPartInfo("多轴运动部件", m_partID, v);
        }
    }
}
