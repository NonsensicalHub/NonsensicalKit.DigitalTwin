using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.Editor.PLC
{
    /// <summary>
    /// 多传感器部件，一般用于料仓
    /// </summary>
    public class MultiSensorPartMotion : PartMotionBase
    {
        public GameObject[] m_ControlTargets;  //控制对象

        protected override void OnReceiveData(List<PLCPoint> part)
        {
            if (m_ControlTargets.Length != part.Count)
            {
                return;
            }

            for (int i = 0; i < m_ControlTargets.Length; i++)
            {
                m_ControlTargets[i].SetActive(bool.Parse(part[i].value));
            }
        }
        protected override PLCPartInfo GetInfo()
        {
            List<PLCPointInfo> v = new List<PLCPointInfo>();
            for (int i = 0; i < m_ControlTargets.Length; i++)
            {
                v.Add(new PLCPointInfo("传感" + i, PLCDataType.Bit, false));
            }
            return new PLCPartInfo("多传感显示部件", m_partID, v);
        }
    }
}
