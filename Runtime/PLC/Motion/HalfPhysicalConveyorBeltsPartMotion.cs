using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.DigitalTwin.PLC
{
    public enum MoveDir
    {
        X,
        Y,
        Z,
        XI,
        YI,
        ZI,
    }

    public class HalfPhysicalConveyorBeltsPartMotion : PartMotionBase
    {
        /// <summary>
        /// 方向1类型
        /// </summary>
        public MoveDir m_Dir1Type;

        /// <summary>
        /// 方向2类型
        /// </summary>
        public MoveDir m_Dir2Type;

        public float m_ConversionRate = 1; //转换率，当为1时，数据为0.1代表速度为0.1m/s

        public HalfPhysicalCollisionArea m_Area;
        [FormerlySerializedAs("m_twoDir")] public bool m_TwoDir = true;

        private List<HalfPhysicalMaterials> _materialss = new();
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

                if (m_TwoDir)
                {
                    foreach (var item in _materialss)
                    {
                        item.Move(GetDir(m_Dir1Type) * _speed1 + GetDir(m_Dir2Type) * _speed2);
                    }
                }
                else
                {
                    foreach (var item in _materialss)
                    {
                        item.Move(GetDir(m_Dir1Type) * _speed1);
                    }
                }
            }
        }

        protected override void Init()
        {
            base.Init();

            m_Area.m_OnMaterialsEnter.AddListener((hpm) => _materialss.Add(hpm));
            m_Area.m_OnMaterialsExit.AddListener((hpm) => _materialss.Remove(hpm));
            _isRunning = true;
        }

        protected override void OnReceiveData(List<PLCPoint> part)
        {
            //两个数据，分别代表两个方向的运行速度
            _speed1 = m_ConversionRate * float.Parse(part[0].value);
            if (m_TwoDir)
            {
                _speed2 = m_ConversionRate * float.Parse(part[1].value);
            }
        }

        protected override void Dispose()
        {
            base.Dispose();
            _isRunning = false;
            _materialss.Clear();
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
            return new PLCPartInfo("碰撞双向传送带", m_partID,
                new List<PLCPointInfo>()
                {
                    new PLCPointInfo("方向1速度", PLCDataType.Float, false),
                    new PLCPointInfo("方向2速度", PLCDataType.Float, false)
                });
        }
    }
}
