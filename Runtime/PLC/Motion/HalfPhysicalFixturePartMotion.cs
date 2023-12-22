using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.PLC
{
    public class HalfPhysicalFixturePartMotion : PartMotionBase
    {
        public HalfPhysicalCollisionArea m_Area1;
        public HalfPhysicalCollisionArea m_Area2;

        public MoveDir m_Dir1Type;
        public MoveDir m_Dir2Type;

        public float m_Distance;

        private HalfPhysicalMaterials _touchMaterials;
        private bool _isClamping;

        private Vector3 _startPos1;
        private Vector3 _endPos1;
        private Vector3 _startPos2;
        private Vector3 _endPos2;

        protected override void Init()
        {
            base.Init();
            _startPos1 = m_Area1.transform.localPosition;
            if (m_Area1.transform.parent == null)
            {
                _endPos1 = _startPos1 + GetDir(m_Dir1Type) * m_Distance;
            }
            else
            {
                _endPos1 = _startPos1 + m_Area1.transform.parent.InverseTransformVector(GetDir(m_Dir1Type) * m_Distance);
            }
            _startPos2 = m_Area2.transform.localPosition;
            if (m_Area2.transform.parent == null)
            {
                _endPos2 = _startPos2 + GetDir(m_Dir2Type) * m_Distance;
            }
            else
            {
                _endPos2 = _startPos2 + m_Area2.transform.parent.InverseTransformVector(GetDir(m_Dir2Type) * m_Distance);
            }
            m_Area1.OnMaterialsEnter.AddListener(OnFixtureEnter);
            m_Area1.OnMaterialsExit.AddListener(OnFixtureExit);
            m_Area2.OnMaterialsEnter.AddListener(OnFixtureEnter);
            m_Area2.OnMaterialsExit.AddListener(OnFixtureExit);
        }

        protected override void Dispose()
        {
            base.Dispose();
        }

        protected override void OnReceiveData(List<PLCPoint> part)
        {
            if (_isClamping !=bool.Parse( part[0].value))
            {
                _isClamping = bool.Parse(part[0].value);
                if (_touchMaterials != null)
                {
                    if (_isClamping)
                    {
                        Clamp(_touchMaterials);
                    }
                    else
                    {
                        Release(_touchMaterials);
                    }
                }
                StopAllCoroutines();
                StartCoroutine(Running(_isClamping));
            }
        }

        private IEnumerator Running(bool needClamp)
        {
            float timer = 0;

            while (timer < 1)
            {
                float t = needClamp ? timer : (1 - timer);
                m_Area1.transform.localPosition = Vector3.Lerp(_startPos1, _endPos1, t);
                m_Area2.transform.localPosition = Vector3.Lerp(_startPos2, _endPos2, t);
                yield return null;
                timer += Time.deltaTime;
            }

            float last = needClamp ? 1 : 0;
            m_Area1.transform.localPosition = Vector3.Lerp(_startPos1, _endPos1, last);
            m_Area2.transform.localPosition = Vector3.Lerp(_startPos2, _endPos2, last);
        }

        private void OnFixtureEnter(HalfPhysicalMaterials hpm)
        {
            if (_touchMaterials == null)
            {
                _touchMaterials = hpm;

                if (_isClamping)
                {
                    Clamp(hpm);
                }
            }
        }

        private void OnFixtureExit(HalfPhysicalMaterials hpm)
        {
            if (!_isClamping && hpm == _touchMaterials)
            {
                _touchMaterials = null;
            }
        }

        private void Clamp(HalfPhysicalMaterials hpm)
        {
            hpm.transform.SetParent(transform);
            hpm.Fixed(true);
        }

        private void Release(HalfPhysicalMaterials hpm)
        {
            hpm.transform.SetParent(null);
            hpm.Fixed(false);
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
            return new PLCPartInfo("碰撞夹具", m_partID,
                new List<PLCPointInfo>() {
                new PLCPointInfo("是否加紧",PLCDataType.Bit,false)
                });
        }
    }
}
