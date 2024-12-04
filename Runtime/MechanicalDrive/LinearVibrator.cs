using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    public class LinearVibrator : Mechanism
    {
        [SerializeField] private float m_amplitudeRadius = 0.1f;
        [SerializeField] private float m_speed = 1f;

        [SerializeField] private Vector3 m_moveAxis = new Vector3(0, 0, 1);

        public Vector3 StartPosition => _startPosition;
        public Vector3 MoveAxis => m_moveAxis;
        public float AmplitudeRadius => m_amplitudeRadius;
        private Vector3 _startPosition;

        private float _currentOffset;

        /// <summary>
        /// Vibration direction.
        /// </summary>
        private int _direction = 1;

        private Vector3 _realAxis;

        private void Awake()
        {
            _startPosition = transform.localPosition;

            _realAxis = transform.localRotation * m_moveAxis;
        }

        public override void Drive(float power, DriveType driveType)
        {
            if (driveType == DriveType.Angular)
            {
                Debug.Log("此机械结构不支持角运动");
                return;
            }

            _currentOffset += power * _direction * m_speed;

            if (_currentOffset < -m_amplitudeRadius)
            {
                _direction *= -1;
                _currentOffset = (-m_amplitudeRadius - _currentOffset) + -m_amplitudeRadius;
            }
            else if (_currentOffset > m_amplitudeRadius)
            {
                _direction *= -1;
                _currentOffset = m_amplitudeRadius - (_currentOffset - m_amplitudeRadius);
            }

            transform.localPosition = _startPosition + _realAxis * _currentOffset;
        }
    }
}
