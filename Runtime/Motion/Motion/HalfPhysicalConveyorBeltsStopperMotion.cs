using System.Collections.Generic;

namespace NonsensicalKit.DigitalTwin.Motion
{
    public class HalfPhysicalConveyorBeltsStopperMotion : PartMotionBase
    {
        public HalfPhysicalCollisionArea m_Area;

        public List<HalfPhysicalMaterials> Materialss { get; private set; } = new List<HalfPhysicalMaterials>();

        public bool Stopping { get; set; }


        protected override void Init()
        {
            base.Init();

            m_Area.m_OnMaterialsEnter.AddListener((hpm) => Materialss.Add(hpm));
            m_Area.m_OnMaterialsExit.AddListener((hpm) => Materialss.Remove(hpm));
        }

        protected override void OnReceiveData(List<PointData> part)
        {
            var b = bool.Parse(part[0].value);
            Stopping = b;
        }

        protected override PLCPartInfo GetInfo()
        {
            return null;
        }
    }
}
