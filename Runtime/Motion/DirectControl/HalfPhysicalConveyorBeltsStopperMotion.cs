using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    public class HalfPhysicalConveyorBeltsStopperMotion : PartMotionBase
    {
        [SerializeField] private HalfPhysicalCollisionArea m_area;

        public List<HalfPhysicalMaterials> Materials { get; private set; } = new List<HalfPhysicalMaterials>();

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
