using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    public class HalfPhysicalFixturePartMotion : PartMotionBase
    {
        [SerializeField] private HalfPhysicalCollisionArea m_area1;
        [SerializeField] private HalfPhysicalCollisionArea m_area2;
        [SerializeField] private Dir m_dir1Type;
        [SerializeField] private Dir m_dir2Type;
        [SerializeField] private float m_distance;

        private HalfPhysicalMaterials _touchMaterials;
        private bool _isClamping;

        private Vector3 _startPos1;
        private Vector3 _endPos1;
        private Vector3 _startPos2;
        private Vector3 _endPos2;

        protected override void Init()
        {
            base.Init();
            _startPos1 = m_area1.transform.localPosition;
            if (m_area1.transform.parent == null)
            {
                _endPos1 = _startPos1 + GetDir(m_dir1Type) * m_distance;
            }
            else
            {
                _endPos1 = _startPos1 + m_area1.transform.parent.InverseTransformVector(GetDir(m_dir1Type) * m_distance);
            }

            _startPos2 = m_area2.transform.localPosition;
            if (m_area2.transform.parent == null)
            {
                _endPos2 = _startPos2 + GetDir(m_dir2Type) * m_distance;
            }
            else
            {
                _endPos2 = _startPos2 + m_area2.transform.parent.InverseTransformVector(GetDir(m_dir2Type) * m_distance);
            }

            m_area1.OnMaterialsEnter.AddListener(OnFixtureEnter);
            m_area1.OnMaterialsExit.AddListener(OnFixtureExit);
            m_area2.OnMaterialsEnter.AddListener(OnFixtureEnter);
            m_area2.OnMaterialsExit.AddListener(OnFixtureExit);
        }

        protected override void OnReceiveData(List<PointData> part)
        {
            if (_isClamping != bool.Parse(part[0].Value))
            {
                _isClamping = bool.Parse(part[0].Value);
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
                m_area1.transform.localPosition = Vector3.Lerp(_startPos1, _endPos1, t);
                m_area2.transform.localPosition = Vector3.Lerp(_startPos2, _endPos2, t);
                yield return null;
                timer += Time.deltaTime;
            }

            float last = needClamp ? 1 : 0;
            m_area1.transform.localPosition = Vector3.Lerp(_startPos1, _endPos1, last);
            m_area2.transform.localPosition = Vector3.Lerp(_startPos2, _endPos2, last);
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
            return new PartDataInfo("碰撞夹具", m_partID,
                new List<PointDataInfo>()
                {
                    new PointDataInfo("是否加紧", PointDataType.Bool, false)
                });
        }
    }
}
