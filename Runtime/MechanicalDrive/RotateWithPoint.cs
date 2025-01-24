using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    public class RotateWithPoint : MonoBehaviour
    {
        [SerializeField] private Transform m_point;


        [SerializeField] private Axis m_axis = Axis.X;

        private Vector3 PlaneNormal
        {
            get
            {
                switch (m_axis)
                {
                    default:
                    case Axis.X: return transform.right;
                    case Axis.Y: return transform.up;
                    case Axis.Z: return transform.forward;
                }
            }
        }

        private Vector3 PlaneAngleBase
        {
            get
            {
                if (transform.parent is not null)
                {
                    switch (m_axis)
                    {
                        default:
                        case Axis.X: return transform.parent.TransformVector(Vector3.forward);
                        case Axis.Y: return transform.parent.TransformVector(Vector3.right);
                        case Axis.Z: return transform.parent.TransformVector(Vector3.up);
                    }
                }
                else
                {
                    switch (m_axis)
                    {
                        default:
                        case Axis.X: return Vector3.forward;
                        case Axis.Y: return Vector3.right;
                        case Axis.Z: return Vector3.up;
                    }
                }
            }
        }

        private float CurrentValue
        {
            get
            {
                switch (m_axis)
                {
                    default:
                    case Axis.X: return -transform.localEulerAngles.x;
                    case Axis.Y: return -transform.localEulerAngles.y;
                    case Axis.Z: return -transform.localEulerAngles.z;
                }
            }
            set
            {
                var angles = transform.localEulerAngles;
                switch (m_axis)
                {
                    default:
                    case Axis.X: angles.x = value; break;
                    case Axis.Y: angles.y = value; break;
                    case Axis.Z: angles.z = value; break;
                }

                transform.localEulerAngles = angles;
            }
        }

        private float _startAngle;
        private float _startValue;

        private void Awake()
        {
            _startValue = CurrentValue;
            _startAngle = GetAngle();
        }

        private void Update()
        {
            var crtAngle = GetAngle();
            CurrentValue = _startValue + crtAngle - _startAngle;
        }

        private float GetAngle()
        {
            var plane = new Plane(PlaneNormal, transform.position);
            var p = plane.ClosestPointOnPlane(m_point.position);

            return Vector3.SignedAngle(PlaneAngleBase, p - transform.position, PlaneNormal);
        }
    }
}
