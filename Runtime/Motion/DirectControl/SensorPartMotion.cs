using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 传感器控制，只用于判断是否有对象
    /// </summary>
    public class SensorPartMotion : PartMotionBase
    {
        [SerializeField] private GameObject m_controlTarget; //控制对象
        [SerializeField] private bool m_inverse;

        protected override void OnReceiveData(List<PointData> part)
        {
            var b = bool.Parse(part[0].Value);
            m_controlTarget.SetActive(m_inverse ? !b : b);
        }

        protected override PartDataInfo GetInfo()
        {
            return new PartDataInfo("传感器部件", m_partID,
                new List<PointDataInfo>()
                {
                    new PointDataInfo("传感", PointDataType.Bool, false),
                });
        }
    }
}
