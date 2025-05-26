using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 人工放置物料部件
    /// </summary>
    public class ManualSetMaterialsPartMotion : PartMotionBase
    {
       [SerializeField] private HalfPhysicalMaterials m_materialsPrefab;

        protected override void Init()
        {
            base.Init();
            m_materialsPrefab.gameObject.SetActive(false);
        }

        protected override void OnReceiveData(List<PointData> part)
        {
            if (bool.Parse(part[0].Value))
            {
                var n = Instantiate(m_materialsPrefab);

                n.Init(transform);
            }
        }

        protected override PartDataInfo GetInfo()
        {
            return new PartDataInfo("模拟人工放置传感器", m_partID,
                new List<PointDataInfo>()
                {
                    new PointDataInfo("放置", PointDataType.Bool, false),
                });
        }
    }
}
