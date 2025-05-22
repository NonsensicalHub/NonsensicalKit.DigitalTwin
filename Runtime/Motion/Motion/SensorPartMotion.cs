using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 传感器控制，只用于判断是否有对象
    /// </summary>
    public class SensorPartMotion : PartMotionBase
    {
        public GameObject m_ControlTarget; //控制对象
        [FormerlySerializedAs("m_inverse")] public bool m_Inverse;

        protected override void OnReceiveData(List<PointData> part)
        {
            var b = bool.Parse(part[0].value);
            m_ControlTarget.SetActive(m_Inverse ? !b : b);
        }

        protected override PLCPartInfo GetInfo()
        {
            return new PLCPartInfo("传感器部件", m_partID,
                new List<PLCPointInfo>()
                {
                    new PLCPointInfo("传感", PointDataType.Bool, false),
                });
        }
    }
}
