using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    public class Synchronizer : Mechanism
    {
        [SerializeField] private Mechanism[] m_mechanisms;
        [FormerlySerializedAs("m_PowerRadius")] [SerializeField] private float m_powerRadius = 1;

        public override void Drive(float power, DriveType driveType)
        {
            float lastPower = power * m_powerRadius;
            foreach (var mechanism in m_mechanisms)
            {
                mechanism.Drive(lastPower, driveType);
            }
        }
    }
}
