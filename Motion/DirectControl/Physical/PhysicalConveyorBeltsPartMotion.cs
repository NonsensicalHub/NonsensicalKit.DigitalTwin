using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 支持单向或双向的半物理传送带
    /// </summary>
    public class PhysicalConveyorBeltsPartMotion : PartMotionBase
    {
        /// <summary>
        /// 方向1类型
        /// </summary>
        [SerializeField] private Dir m_dir1Type;

        /// <summary>
        /// 方向2类型
        /// </summary>
        [SerializeField] private Dir m_dir2Type;

        [SerializeField] private float m_conversionRate = 1; //转换率，当为1时，数据为0.1代表速度为0.1m/s

        [FormerlySerializedAs("m_Area")] [SerializeField]
        private PhysicalCollisionArea m_area;

        [SerializeField] private bool m_twoDir = true;

        protected readonly List<PhysicalMaterials> Materials = new();
        protected float Speed1;
        protected float Speed2;
        protected bool IsRunning;

        [Button]
        private void GetAreaInChildren()
        {
            m_area = GetComponentInChildren<PhysicalCollisionArea>();
        }

        protected virtual void Update()
        {
            if (IsRunning)
            {
                if (Speed1 == 0 && Speed2 == 0)
                {
                    return;
                }

                if (m_twoDir)
                {
                    foreach (var item in Materials)
                    {
                        item.Move(GetDir(m_dir1Type) * Speed1 + GetDir(m_dir2Type) * Speed2);
                    }
                }
                else
                {
                    foreach (var item in Materials)
                    {
                        item.Move(GetDir(m_dir1Type) * Speed1);
                    }
                }
            }
        }

        protected override void Init()
        {
            base.Init();

            m_area.OnMaterialsEnter.AddListener((hpm) => Materials.Add(hpm));
            m_area.OnMaterialsExit.AddListener((hpm) => Materials.Remove(hpm));
            IsRunning = true;
        }

        protected override void OnReceiveData(List<PointData> part)
        {
            //两个数据，分别代表两个方向的运行速度
            Speed1 = m_conversionRate * float.Parse(part[0].Value);
            if (m_twoDir)
            {
                Speed2 = m_conversionRate * float.Parse(part[1].Value);
            }
        }

        protected override void Dispose()
        {
            base.Dispose();
            IsRunning = false;
            m_area.OnMaterialsEnter.RemoveAllListeners();
            m_area.OnMaterialsExit.RemoveAllListeners();
            Materials.Clear();
        }

        private Vector3 GetDir(Dir dt)
        {
            switch (dt)
            {
                case Dir.X:
                    return transform.right;
                case Dir.Y:
                    return transform.up;
                case Dir.Z:
                    return transform.forward;
                case Dir.XI:
                    return -transform.right;
                case Dir.YI:
                    return -transform.up;
                case Dir.ZI:
                    return -transform.forward;
                default:
                    return Vector3.zero;
            }
        }

        protected override PartDataInfo GetInfo()
        {
            return new PartDataInfo("碰撞双向传送带", m_partID,
                new List<PointDataInfo>()
                {
                    new PointDataInfo("方向1速度", PointDataType.Float, false),
                    new PointDataInfo("方向2速度", PointDataType.Float, false)
                });
        }
    }
}
