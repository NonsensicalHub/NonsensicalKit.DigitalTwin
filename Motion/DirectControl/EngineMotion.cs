using System.Collections.Generic;
using NonsensicalKit.DigitalTwin.Motion;
using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.DigitalTwin.Motion
{
    public class EngineMotion : PartMotionBase
    {
        [SerializeField]private MotionEngine m_engine; //控制对象

        private long _lastTick = -1;

        protected override void OnReceiveData(List<PointData> part)
        {
            if (float.TryParse(part[0].Value, out var v))
            {
                if (_lastTick == -1)
                {
                    _lastTick = part[0].Ticks;
                }

                m_engine.ChangeValue(v, (part[0].Ticks - _lastTick) * Magnification);
                _lastTick = part[0].Ticks;
            }
        }

        protected override PartDataInfo GetInfo()
        {
            List<PointDataInfo> v = new List<PointDataInfo>();
            v.Add(new PointDataInfo("引擎值", PointDataType.Float, false));
            return new PartDataInfo("引擎", m_partID, v);
        }
    }
}
