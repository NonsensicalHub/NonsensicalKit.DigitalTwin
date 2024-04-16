using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    public enum EngineState
    {
        /// <summary>
        /// 静止
        /// </summary>
        Stationary = 0,
        /// <summary>
        /// 减速
        /// </summary>
        Decelerating,
        /// <summary>
        /// 加速
        /// </summary>
        Accelerating,
        /// <summary>
        /// 全速
        /// </summary>
        FullSpeed
    }

    public abstract class Engine : Mechanism
    {
        [SerializeField] protected Mechanism[] m_mechanisms;
        [SerializeField] protected DriveType m_driveType = DriveType.Linear;

        public override void Drive(float power, DriveType driveType)
        {
            float lastPower = power;
            foreach (var mechanism in m_mechanisms)
            {
                mechanism.Drive(lastPower, driveType);
            }
        }
    }
}
