using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    public class SimpleConveyorBeltsPartMotion : PartMotionBase
    {
        [SerializeField] private Dir m_dirType;
        [SerializeField] private float m_conversionRate = 1; //转换率，当为1时，数据为0.1代表速度为0.1m/s
        [SerializeField] private PhysicalCollisionArea m_area;

        private readonly List<PhysicalMaterials> _materials = new();
        private float _speed;
        private bool _isRunning;

        private void Update()
        {
            if (_isRunning)
            {
                if (_speed == 0)
                {
                    return;
                }

                foreach (var item in _materials)
                {
                    item.Move(GetDir(m_dirType) * _speed);
                }
            }
        }

        protected override void Init()
        {
            base.Init();

            if (GetComponent<Rigidbody>() == null)
            {
                var rb = gameObject.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.isKinematic = true;
            }

            if (GetComponent<Collider>() == null)
            {
                gameObject.AddComponent<BoxCollider>();
            }

            m_area.OnMaterialsEnter.AddListener((hpm) => _materials.Add(hpm));
            m_area.OnMaterialsExit.AddListener((hpm) => _materials.Remove(hpm));
            _isRunning = true;
        }

        protected override void Dispose()
        {
            base.Dispose();
            _isRunning = false;
            _materials.Clear();
        }

        protected override void OnReceiveData(List<PointData> part)
        {
            _speed = m_conversionRate * float.Parse(part[0].Value);
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
            return new PartDataInfo("单方向传送带部件", m_partID,
                new List<PointDataInfo>()
                {
                    new PointDataInfo("速度", PointDataType.Float, false),
                });
        }
    }
}
