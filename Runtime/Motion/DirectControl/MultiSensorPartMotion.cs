using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 多传感器部件，一般用于料仓
    /// </summary>
    public class MultiSensorPartMotion : PartMotionBase
    {
         [SerializeField] private GameObject[] m_controlTargets; //控制对象

        protected override void OnReceiveData(List<PointData> part)
        {
            if (m_controlTargets.Length != part.Count)
            {
                return;
            }

            for (int i = 0; i < m_controlTargets.Length; i++)
            {
                m_controlTargets[i].SetActive(bool.Parse(part[i].Value));
            }
        }

        protected override PartDataInfo GetInfo()
        {
            List<PointDataInfo> v = new List<PointDataInfo>();
            for (int i = 0; i < m_controlTargets.Length; i++)
            {
                v.Add(new PointDataInfo("传感" + i, PointDataType.Bool, false));
            }

            return new PartDataInfo("多传感显示部件", m_partID, v);
        }
    }
}
