using System.Collections.Generic;
using NonsensicalKit.DigitalTwin.MechanicalDrive;
using UnityEngine.Serialization;

namespace NonsensicalKit.DigitalTwin.Motion
{
    public class EngineMotion : PartMotionBase
    {
        [FormerlySerializedAs("m_engine")] public PLCEngine m_Engine; //控制对象

        private long _lastTick = -1;

        protected override void OnReceiveData(List<PointData> part)
        {
            if (float.TryParse(part[0].value, out var v))
            {
                if (_lastTick == -1)
                {
                    _lastTick = part[0].ticks;
                }

                m_Engine.ChangeValue(v, (part[0].ticks - _lastTick) * Magnification);
                _lastTick = part[0].ticks;
            }
        }

        protected override PLCPartInfo GetInfo()
        {
            List<PLCPointInfo> v = new List<PLCPointInfo>();
            v.Add(new PLCPointInfo("引擎值", PointDataType.Float, false));
            return new PLCPartInfo("引擎", m_partID, v);
        }
    }
}
