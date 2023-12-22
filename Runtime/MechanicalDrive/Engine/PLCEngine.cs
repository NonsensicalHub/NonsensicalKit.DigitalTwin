using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    public class PLCEngine : Engine
    {
        [SerializeField] private float m_initialValue = 0;
        [SerializeField] private float m_maxPower = 10;
        [SerializeField] private float m_radio = 1;

        private float _targetValue;
        private float _currentValue;

        private void Awake()
        {
            _targetValue = m_initialValue;
            _currentValue = m_initialValue;
            enabled = false;
        }

        private void FixedUpdate()
        {
            var offset = _targetValue - _currentValue;
            var distance = Mathf.Abs(offset);
            var maxDistance = m_maxPower * Time.fixedDeltaTime;
            if (distance > maxDistance)
            {
                var realOffset = offset > 0 ? maxDistance : -maxDistance;
                _currentValue += realOffset;
                Drive(realOffset * m_radio, m_driveType);
            }
            else
            {
                _currentValue = _targetValue;
                Drive(offset * m_radio, m_driveType);
                enabled = false;
            }
        }

        public void ChangeValue(string newValueStr)
        {
            if (float.TryParse(newValueStr,out var v))
            {
                ChangeValue(v);
            }
        }

        public void ChangeValue(float newValue)
        {
            if (newValue != _targetValue)
            {
                _targetValue = newValue;
                enabled = true;
            }
        }

        /// <summary>
        ///  
        /// </summary>
        /// <param name="newValue"></param>
        /// <param name="time">动作用时，单位秒</param>
        public void ChangeValue(float newValue,float time)
        {
            if (time==0)
            {
                ChangeValue(newValue);
                return;
            }
            if (newValue != _targetValue)
            {
                _targetValue = newValue;
                m_maxPower = Mathf.Abs (_targetValue-_currentValue)/time;
                enabled = true;
            }
        }
    }
}
