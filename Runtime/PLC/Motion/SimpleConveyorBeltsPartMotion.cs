using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.PLC
{
    public class SimpleConveyorBeltsPartMotion : PartMotionBase
    {
        /// <summary>
        /// 方向1类型
        /// </summary>
        public MoveDir m_DirType;

        public float m_ConversionRate = 1;//转换率，当为1时，数据为0.1代表速度为0.1m/s

        public HalfPhysicalCollisionArea m_Area;

        private List<HalfPhysicalMaterials> _materials = new List<HalfPhysicalMaterials>();
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
                    item.Move(GetDir(m_DirType) * _speed);
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
            m_Area.OnMaterialsEnter.AddListener((hpm) => _materials.Add(hpm));
            m_Area.OnMaterialsExit.AddListener((hpm) => _materials.Remove(hpm));
            _isRunning = true;
        }

        protected override void Dispose()
        {
            base.Dispose();
            _isRunning = false;
            _materials.Clear();
        }

        protected override void OnReceiveData(List<PLCPoint> part)
        {
            _speed = m_ConversionRate * float.Parse(part[0].value);
        }

        private Vector3 GetDir(MoveDir dt)
        {
            switch (dt)
            {
                case MoveDir.X:
                    return transform.right;
                case MoveDir.Y:
                    return transform.up;
                case MoveDir.Z:
                    return transform.forward;
                case MoveDir.XI:
                    return -transform.right;
                case MoveDir.YI:
                    return -transform.up;
                case MoveDir.ZI:
                    return -transform.forward;
                default:
                    return Vector3.zero;
            }
        }
        protected override PLCPartInfo GetInfo()
        {
            return new PLCPartInfo("单方向传送带部件", m_partID,
                new List<PLCPointInfo>() {
                new PLCPointInfo("速度",PLCDataType.Real,false),
                });
        }
    }
}
