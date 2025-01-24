using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    /// <summary>
    /// 驱动自动伸缩部件的可移动部件
    /// </summary>
    public class StretchDriver : MonoBehaviour
    {
        [SerializeField] private Axis m_axis;
        [SerializeField] private Stretcher[] m_left;
        [SerializeField] private Stretcher[] m_right;

        private float _startPos;

        private void Awake()
        {
            _startPos = GetPos();
        }

        private void Update()
        {
            var nowPos = GetPos();
            foreach (var c in m_left)
            {
                c.SetNewSize(nowPos - _startPos);
            }

            foreach (var c in m_right)
            {
                c.SetNewSize(_startPos - nowPos);
            }
        }

        private float GetPos()
        {
            switch (m_axis)
            {
                default:
                case Axis.X: return transform.localPosition.x;
                case Axis.Y: return transform.localPosition.y;
                case Axis.Z: return transform.localPosition.z;
            }
        }
    }
}
