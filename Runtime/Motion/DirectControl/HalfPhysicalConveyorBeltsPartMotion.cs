using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.DigitalTwin.Motion
{
    /// <summary>
    /// 支持单向或双向的半物理传送带
    /// </summary>
    public class HalfPhysicalConveyorBeltsPartMotion : PartMotionBase
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

        public HalfPhysicalCollisionArea m_Area;

        [FormerlySerializedAs("m_TwoDir")] [SerializeField]
        private bool m_twoDir = true;

        private readonly List<HalfPhysicalMaterials> _materials = new();
        private float _speed1;
        private float _speed2;
        private bool _isRunning;

        private void Update()
        {
            if (_isRunning)
            {
                if (_speed1 == 0 && _speed2 == 0)
                {
                    return;
                }

                if (m_twoDir)
                {
                    foreach (var item in _materials)
                    {
                        item.Move(GetDir(m_dir1Type) * _speed1 + GetDir(m_dir2Type) * _speed2);
                    }
                }
                else
                {
                    foreach (var item in _materials)
                    {
                        item.Move(GetDir(m_dir1Type) * _speed1);
                    }
                }
            }
        }

        protected override void Init()
        {
            base.Init();

            m_Area.OnMaterialsEnter.AddListener((hpm) => _materials.Add(hpm));
            m_Area.OnMaterialsExit.AddListener((hpm) => _materials.Remove(hpm));
            _isRunning = true;
        }

        protected override void OnReceiveData(List<PointData> part)
        {
            //两个数据，分别代表两个方向的运行速度
            _speed1 = m_conversionRate * float.Parse(part[0].Value);
            if (m_twoDir)
            {
                _speed2 = m_conversionRate * float.Parse(part[1].Value);
            }
        }

        protected override void Dispose()
        {
            base.Dispose();
            _isRunning = false;
            _materials.Clear();
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
