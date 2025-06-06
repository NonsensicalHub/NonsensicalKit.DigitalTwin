using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    public class CentrifugalVibrator : Mechanism
    {
        [SerializeField] private float m_amplitudeRadius = 0.1f;
        [SerializeField] private float m_speed = 1f;

        public Vector3 StartPosition { private set; get; }
        public float AmplitudeRadius => m_amplitudeRadius;
        private float _currentAngle;

        private void Awake()
        {
            StartPosition = transform.localPosition;
        }

        public override void Drive(float power, DriveType driveType)
        {
            switch (driveType)
            {
                case DriveType.Linear:
                    _currentAngle += power * Mathf.Rad2Deg / m_amplitudeRadius * m_speed;
                    break;
                case DriveType.Angular:
                    _currentAngle += power * m_speed;
                    break;
                default:
                    break;
            }

            var direction = Quaternion.AngleAxis(_currentAngle, transform.forward) * transform.right;
            transform.localPosition = StartPosition + GetLocalDirection(direction) * m_amplitudeRadius;
        }

        private Vector3 GetLocalDirection(Vector3 direction)
        {
            if (transform.parent)
                return transform.parent.InverseTransformVector(direction);
            else
                return direction;
        }
    }
}
