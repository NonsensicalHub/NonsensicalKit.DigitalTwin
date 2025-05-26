using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
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
