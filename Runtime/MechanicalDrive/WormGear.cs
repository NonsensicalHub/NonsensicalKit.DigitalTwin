using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    public class WormGear : Mechanism
    {
        #region Property and Field
        /// <summary>
        /// Worm shaft.
        /// </summary>
        public Gear worm;

        /// <summary>
        /// Count of worm threads.
        /// </summary>
        public int threads = 1;

        /// <summary>
        /// Worm gear.
        /// </summary>
        public Gear gear;

        /// <summary>
        /// Count of gear Teeth.
        /// </summary>
        public int teeth = 36;
        #endregion

        #region Public Method
        /// <summary>
        /// Drive worm and gear.
        /// </summary>
        /// <param name="velocity">Worm linear velocity.</param>
        public override void Drive(float velocity, DriveType driveType)
        {
            var wormSpeed = velocity / worm.GearRadius;
            worm.transform.Rotate(Vector3.forward, wormSpeed, Space.Self);
            gear.transform.Rotate(Vector3.forward, wormSpeed * threads / teeth, Space.Self);
        }
        #endregion
    }
}
