using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    public class Gear : Mechanism
    {
        /// <summary>
        /// 齿轮半径
        /// </summary>
        [SerializeField] private float m_gearRadius = 1f;

        /// <summary>
        /// 同轴对象，角速度相同
        /// </summary>
        [SerializeField] private Mechanism[] m_coaxialObjects;

        /// <summary>
        /// 啮合对象，线速度相同
        /// </summary>
        [SerializeField] private Mechanism[] m_engageObjects;

        [SerializeField] private Vector3 m_rotateAxis = new Vector3(0, 0, 1);

        public float GearRadius => m_gearRadius;
        public Vector3 RotateAxis => m_rotateAxis;

        public override void Drive(float power, DriveType driveType)
        {
            switch (driveType)
            {
                case DriveType.Linear:
                    LinearDrive(power);
                    break;
                case DriveType.Angular:
                    AngularDrive(power);
                    break;
            }
        }

        private void LinearDrive(float power)
        {
            transform.Rotate(m_rotateAxis, power * Mathf.Rad2Deg / m_gearRadius, Space.Self);

            foreach (var item in m_coaxialObjects)
            {
                item.Drive(power * Mathf.Rad2Deg / m_gearRadius, DriveType.Angular);
            }

            foreach (var item in m_engageObjects)
            {
                item.Drive(power, DriveType.Linear);
            }
        }

        private void AngularDrive(float power)
        {
            transform.Rotate(m_rotateAxis, power, Space.Self);

            foreach (var item in m_coaxialObjects)
            {
                item.Drive(power, DriveType.Angular);
            }

            foreach (var item in m_engageObjects)
            {
                item.Drive(power * Mathf.Deg2Rad * m_gearRadius, DriveType.Linear);
            }
        }
    }
}
