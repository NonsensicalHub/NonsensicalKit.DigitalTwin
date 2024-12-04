using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    public class FreezeRotate : MonoBehaviour
    {
        [SerializeField] private AxisSelect m_freezeAxisSelect;

        private Vector3 _startRotation;

        private void Awake()
        {
            _startRotation = transform.eulerAngles;
        }

        private void Update()
        {
            var rotation = transform.eulerAngles;
            switch (m_freezeAxisSelect)
            {
                case AxisSelect.X: rotation.x = _startRotation.x; break;
                case AxisSelect.Y: rotation.y = _startRotation.y; break;
                case AxisSelect.Z: rotation.z = _startRotation.z; break;
                case AxisSelect.XY:
                    rotation.x = _startRotation.x;
                    rotation.y = _startRotation.y;
                    break;
                case AxisSelect.XZ:
                    rotation.x = _startRotation.x;
                    rotation.z = _startRotation.z;
                    break;
                case AxisSelect.YZ:
                    rotation.y = _startRotation.y;
                    rotation.z = _startRotation.z;
                    break;
                case AxisSelect.All:
                    rotation.x = _startRotation.x;
                    rotation.y = _startRotation.y;
                    rotation.z = _startRotation.z;
                    break;
                case AxisSelect.None:
                default:
                    break;
            }

            transform.eulerAngles = rotation;
        }
    }
}
