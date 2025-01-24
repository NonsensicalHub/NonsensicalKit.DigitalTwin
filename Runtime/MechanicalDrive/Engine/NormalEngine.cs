using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    public class NormalEngine : Engine
    {
        [SerializeField] private float m_defaultPower = 10; //对应运动类型每秒运动幅度
        [SerializeField] private bool m_autoRunning = true;
        [SerializeField] private bool m_useDamper;
        [SerializeField] private AnimationCurve m_accelerationCurve;
        [SerializeField] private AnimationCurve m_decelerationCurve;

        protected EngineState State = EngineState.Stationary;
        protected bool WaitStarting;
        protected bool WaitStopping;
        protected float Power;
        protected float Timer;

        protected virtual void Reset()
        {
            m_accelerationCurve = new AnimationCurve(new[] { new Keyframe(0, 0), new Keyframe(2, 1) });
            m_decelerationCurve = new AnimationCurve(new[] { new Keyframe(0, 1), new Keyframe(3, 0) });
        }

        private void Awake()
        {
            Power = m_defaultPower;
            if (m_autoRunning)
            {
                State = EngineState.FullSpeed;
            }
        }

        protected virtual void FixedUpdate()
        {
            switch (State)
            {
                case EngineState.Stationary:
                    enabled = false;
                    break;
                case EngineState.Accelerating:
                    Timer += Time.deltaTime;
                    Power = m_accelerationCurve.Evaluate(Timer) * m_defaultPower;
                    if (Timer >= m_accelerationCurve[m_accelerationCurve.length - 1].time)
                    {
                        Timer = 0;
                        if (WaitStopping)
                        {
                            WaitStopping = false;
                            State = EngineState.Decelerating;
                        }
                        else
                        {
                            State = EngineState.FullSpeed;
                        }
                    }

                    break;
                case EngineState.FullSpeed:
                    break;
                case EngineState.Decelerating:
                    Timer += Time.deltaTime;
                    Power = m_decelerationCurve.Evaluate(Timer) * m_defaultPower;
                    if (Timer >= m_decelerationCurve[m_decelerationCurve.length - 1].time)
                    {
                        Timer = 0;
                        Power = 0;
                        if (WaitStarting)
                        {
                            WaitStarting = false;
                            State = EngineState.Accelerating;
                        }
                        else
                        {
                            State = EngineState.Stationary;
                        }
                    }

                    break;
            }

            if (State != EngineState.Stationary)
            {
                Drive(Power * Time.fixedDeltaTime, m_driveType);
            }
        }

        public virtual void Starting()
        {
            if (State == EngineState.Stationary)
            {
                if (m_useDamper)
                {
                    Timer = 0;
                    State = EngineState.Accelerating;
                }
                else
                {
                    State = EngineState.FullSpeed;
                }

                enabled = true;
            }
            else if (State == EngineState.Decelerating)
            {
                WaitStarting = true;
            }
        }

        public virtual void Stopping()
        {
            if (State == EngineState.FullSpeed)
            {
                if (m_useDamper)
                {
                    Timer = 0;
                    State = EngineState.Decelerating;
                }
                else
                {
                    State = EngineState.Stationary;
                    enabled = false;
                }
            }
            else if (State == EngineState.Accelerating)
            {
                WaitStopping = true;
            }
        }

        public virtual void SetPower(float newPower)
        {
            m_defaultPower = newPower;
            Power = newPower;
        }
    }
}
