using UnityEngine;

namespace NonsensicalKit.Editor.MechanicalDrive
{
    public class Synchronizer :Mechanism
    {
        [SerializeField] private Mechanism[] m_mechanisms;
        [SerializeField] private float m_PowerRadius = 1;

        /// <summary>
        /// Drive synchronizer's mechanisms.
        /// </summary>
        /// <param name="velocity">Linear velocity.</param>
        public override void Drive(float power, DriveType driveType)
        {
            float lastPower = power * m_PowerRadius;
            foreach (var mechanism in m_mechanisms)
            {
                mechanism.Drive(lastPower, driveType);
            }
        }
    }
}