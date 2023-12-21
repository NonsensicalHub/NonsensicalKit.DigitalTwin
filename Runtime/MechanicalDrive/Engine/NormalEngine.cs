using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.Editor.MechanicalDrive
{
    public class NormalEngine : Engine
    {
        [SerializeField] private float m_defaultPower = 10; //对应运动类型每秒运动幅度
        [SerializeField] private bool m_autoRunning = true;
        [SerializeField] private bool m_useDamper = false;
        [SerializeField] private AnimationCurve m_accelerationCurve;
        [SerializeField] private AnimationCurve m_decelerationCurve;

        protected EngineState _state = EngineState.Stationary;
        protected bool _waitStarting;
        protected bool _waitStoping;
        protected float _power;
        protected float _timer = 0;

        protected virtual void Reset()
        {
            m_accelerationCurve = new AnimationCurve(new Keyframe[] { new Keyframe(0, 0), new Keyframe(2, 1) });
            m_decelerationCurve = new AnimationCurve(new Keyframe[] { new Keyframe(0, 1), new Keyframe(3, 0) });
        }

        private void Awake()
        {
            _power = m_defaultPower;
            if (m_autoRunning)
            {
                _state = EngineState.FullSpeed;
            }
        }

        protected virtual void FixedUpdate()
        {
            switch (_state)
            {
                case EngineState.Stationary:
                    enabled = false;
                    break;
                case EngineState.Accelerating:
                    _timer += Time.deltaTime;
                    _power = m_accelerationCurve.Evaluate(_timer) * m_defaultPower;
                    if (_timer >= m_accelerationCurve[m_accelerationCurve.length - 1].time)
                    {
                        _timer = 0;
                        if (_waitStoping)
                        {
                            _waitStoping = false;
                            _state = EngineState.Decelerating;
                        }
                        else
                        {
                            _state = EngineState.FullSpeed;
                        }
                    }
                    break;
                case EngineState.FullSpeed:
                    break;
                case EngineState.Decelerating:
                    _timer += Time.deltaTime;
                    _power = m_decelerationCurve.Evaluate(_timer) * m_defaultPower;
                    if (_timer >= m_decelerationCurve[m_decelerationCurve.length - 1].time)
                    {
                        _timer = 0;
                        _power = 0;
                        if (_waitStarting)
                        {
                            _waitStarting = false;
                            _state = EngineState.Accelerating;
                        }
                        else
                        {
                            _state = EngineState.Stationary;
                        }
                    }
                    break;
                default:
                    break;
            }

            if (_state != EngineState.Stationary)
            {
                Drive(_power * Time.fixedDeltaTime, m_driveType);
            }
        }
        public virtual void Starting()
        {
            if (_state == EngineState.Stationary)
            {
                if (m_useDamper)
                {
                    _timer = 0;
                    _state = EngineState.Accelerating;
                }
                else
                {
                    _state = EngineState.FullSpeed;
                }
                enabled = true;
            }
            else if (_state == EngineState.Decelerating)
            {
                _waitStarting = true;
            }
        }

        public virtual void Stopping()
        {
            if (_state == EngineState.FullSpeed)
            {
                if (m_useDamper)
                {
                    _timer = 0;
                    _state = EngineState.Decelerating;
                }
                else
                {
                    _state = EngineState.Stationary;
                    enabled = false;
                }
            }
            else if (_state == EngineState.Accelerating)
            {
                _waitStoping = true;
            }
        }

        public virtual void SetPower(float newPower)
        {
            m_defaultPower = newPower;
            _power = newPower;
        }
    }
}