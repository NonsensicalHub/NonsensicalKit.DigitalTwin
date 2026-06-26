using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    public class PhysicalConveyorBeltsStopperMotion : PartMotionBase
    {
        [SerializeField] private PhysicalCollisionArea m_area;

        public List<PhysicalMaterials> Materials { get; private set; } = new List<PhysicalMaterials>();

        public bool Stopping { get; set; }

        protected override void Init()
        {
            base.Init();

            m_area.OnMaterialsEnter.AddListener((hpm) => Materials.Add(hpm));
            m_area.OnMaterialsExit.AddListener((hpm) => Materials.Remove(hpm));
        }

        protected override void OnReceiveData(List<PointData> part)
        {
            var b = bool.Parse(part[0].Value);
            Stopping = b;
        }

        protected override PartDataInfo GetInfo()
        {
            return null;
        }
    }
}
