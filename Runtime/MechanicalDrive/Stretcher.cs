using System;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    /// <summary>
    /// 自动伸缩的部件
    /// </summary>
    public class Stretcher : MonoBehaviour
    {
        [SerializeField] private float m_originSize = 1;
        [SerializeField] private Axis m_axis;

        public void SetNewSize(float changed)
        {
            switch (m_axis)
            {
                default:
                case Axis.X: transform.localScale = new Vector3((m_originSize + changed) / m_originSize, 1, 1); break;
                case Axis.Y: transform.localScale = new Vector3(1, (m_originSize + changed) / m_originSize, 1); break;
                case Axis.Z: transform.localScale = new Vector3(1, 1, (m_originSize + changed) / m_originSize); break;
            }
        }
    }
}
