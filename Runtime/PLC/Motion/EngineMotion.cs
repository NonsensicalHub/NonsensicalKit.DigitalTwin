using NonsensicalKit.DigitalTwin.MechanicalDrive;
using System.Collections.Generic;

namespace NonsensicalKit.DigitalTwin.PLC
{
    public class EngineMotion : PartMotionBase
    {
        public PLCEngine m_engine;  //控制对象

        private long _lastTick = -1;

        protected override void OnReceiveData(List<PLCPoint> part)
        {
            if (float.TryParse(part[0].value, out var v))
            {
                if (_lastTick == -1)
                {
                    _lastTick = part[0].ticks;
                }
                m_engine.ChangeValue(v, (part[0].ticks - _lastTick) * _magnification);
                _lastTick = part[0].ticks;
            }
        }

        protected override PLCPartInfo GetInfo()
        {
            List<PLCPointInfo> v = new List<PLCPointInfo>();
            v.Add(new PLCPointInfo("引擎值", PLCDataType.Real, false));
            return new PLCPartInfo("引擎", m_partID, v);
        }
    }
}
