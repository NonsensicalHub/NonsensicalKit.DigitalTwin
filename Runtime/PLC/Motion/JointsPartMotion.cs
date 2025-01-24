using System.Collections.Generic;

namespace NonsensicalKit.DigitalTwin.PLC
{
    /// <summary>
    /// 多轴运动部件运动和行为基类
    /// </summary>
    public class JointsPartMotion : PartMotionBase
    {
        public JointSetting[] m_Joints;
        public bool m_UseInt;

        protected long LastTicks;
        protected JointController Controller;

        protected override void Init()
        {
            base.Init();
            Controller = gameObject.AddComponent<JointController>();

            Controller.Joints = m_Joints;
        }

        protected override void Dispose()
        {
            base.Dispose();
            Destroy(Controller);
        }

        protected override void OnReceiveData(List<PLCPoint> part)
        {
            if (!Controller)
            {
                return;
            }

            if (part.Count != m_Joints.Length)
            {
                return;
            }

            long time = 0;
            if (LastTicks != 0)
            {
                time = part[0].ticks - LastTicks;
            }

            LastTicks = part[0].ticks;
            float[] values = new float[m_Joints.Length];
            if (m_UseInt)
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

            Controller.ChangeState(new ActionData(values, time * Magnification));
        }

        protected override PLCPartInfo GetInfo()
        {
            PLCDataType type = PLCDataType.Float;
            if (m_UseInt)
            {
                type = PLCDataType.Int;
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
