using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.Editor.PLC
{
    /// <summary>
    /// 传感器控制，只用于判断是否有对象
    /// </summary>
    public  class SensorPartMotion : PartMotionBase
    {
        public GameObject m_ControlTarget;  //控制对象
        public bool m_inverse;

        protected override void OnReceiveData(List<PLCPoint> part)
        {
            var b = bool.Parse(part[0].value);
            m_ControlTarget.SetActive(m_inverse?!b:b);
        }
        protected override PLCPartInfo GetInfo()
        {
            return new PLCPartInfo("传感器部件", m_partID,
                new List<PLCPointInfo>() {
                new PLCPointInfo("传感",PLCDataType.Bit,false),
                });
        }
    }
}
