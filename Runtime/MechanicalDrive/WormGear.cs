using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    public class WormGear : Mechanism
    {
        [FormerlySerializedAs("worm")] [SerializeField]
        private Gear m_worm;

        [FormerlySerializedAs("threads")] [SerializeField]
        private int m_threads = 1;

        [FormerlySerializedAs("gear")] [SerializeField]
        private Gear m_gear;

        [FormerlySerializedAs("teeth")] [SerializeField]
        private int m_teeth = 36;

        public override void Drive(float velocity, DriveType driveType)
        {
            var wormSpeed = velocity / m_worm.GearRadius;
            m_worm.transform.Rotate(Vector3.forward, wormSpeed, Space.Self);
            m_gear.transform.Rotate(Vector3.forward, wormSpeed * m_threads / m_teeth, Space.Self);
        }
    }
}
