using System.Collections.Generic;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 人工放置物料部件
    /// </summary>
    public class ManualSetMaterialsPartMotion : PartMotionBase
    {
        public HalfPhysicalMaterials m_MaterialsPrefab;

        protected override void Init()
        {
            base.Init();
            m_MaterialsPrefab.gameObject.SetActive(false);
        }

        protected override void OnReceiveData(List<PointData> part)
        {
            if (bool.Parse(part[0].value))
            {
                var n = Instantiate(m_MaterialsPrefab);

                n.Init(transform);
            }
        }

        protected override PLCPartInfo GetInfo()
        {
            return new PLCPartInfo("模拟人工放置传感器", m_partID,
                new List<PLCPointInfo>()
                {
                    new PLCPointInfo("放置", PointDataType.Bool, false),
                });
        }
    }
}
