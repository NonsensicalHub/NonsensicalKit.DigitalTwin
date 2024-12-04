using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    public class SameAngle : MonoBehaviour
    {
        [SerializeField] private Transform m_target;

        [SerializeField] private AxisSelect m_axisSelect = AxisSelect.X;

        private void Update()
        {
            var selfAngles = transform.localEulerAngles;
            var targetLocalEulerAngles = m_target.localEulerAngles;
            switch (m_axisSelect)
            {
                case AxisSelect.X: selfAngles.x = targetLocalEulerAngles.x; break;
                case AxisSelect.Y: selfAngles.y = targetLocalEulerAngles.y; break;
                case AxisSelect.Z: selfAngles.z = targetLocalEulerAngles.z; break;
                case AxisSelect.XY:
                {
                    selfAngles.x = targetLocalEulerAngles.x;
                    selfAngles.y = targetLocalEulerAngles.y;
                    break;
                }
                case AxisSelect.XZ:
                {
                    selfAngles.x = targetLocalEulerAngles.x;
                    selfAngles.z = targetLocalEulerAngles.z;
                    break;
                }
                case AxisSelect.YZ:
                {
                    selfAngles.y = targetLocalEulerAngles.y;
                    selfAngles.z = targetLocalEulerAngles.z;
                    break;
                }
                case AxisSelect.All:
                {
                    selfAngles.x = targetLocalEulerAngles.x;
                    selfAngles.y = targetLocalEulerAngles.y;
                    selfAngles.z = targetLocalEulerAngles.z;
                    break;
                }
                default:
                case AxisSelect.None:
                    break;
            }

            transform.localEulerAngles = selfAngles;
        }
    }
}
