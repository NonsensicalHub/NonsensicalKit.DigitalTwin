using System;
using System.Collections.Generic;
using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    public class PhysicalMaterials : MonoBehaviour
    {
        [SerializeField] private bool m_isKinematic;

        public Action<PhysicalMaterials> Recycle;

        public readonly HashSet<PhysicalCollisionArea> Areas = new();

        private bool _isRunning;

        private Rigidbody _rb;

        private int _moveCount;
        private Vector3 _offset;

        private bool _isFixed;

        private void Awake()
        {
            if (TryGetComponent(out _rb) == false)
            {
                _rb = gameObject.AddComponent<Rigidbody>();
                if (m_isKinematic)
                {
                    _rb.useGravity = false;
                    _rb.isKinematic = true;
                }
            }

            if (GetComponent<Collider>() == null)
            {
                gameObject.AddComponent<BoxCollider>();
            }

            _isRunning = true;
        }

        private void Start()
        {
            _rb.MovePosition(transform.position);
        }

        private void Update()
        {
            if (!_isFixed && _moveCount > 0)
            {
                _rb.MovePosition(transform.position + _offset / _moveCount * Time.deltaTime);
                _moveCount = 0;
                _offset = Vector3.zero;
            }
        }

        private void OnDestroy()
        {
            _isRunning = false;
        }

        public void Init(Transform target)
        {
            transform.SetPositionAndRotation(target.position, target.rotation);
            gameObject.SetActive(true);
        }

        public void Fixed(bool isFixed)
        {
            this._isFixed = isFixed;
        }

        public void Move(Vector3 offset)
        {
            _moveCount++;
            this._offset += offset;
        }


        public void Over(bool needRecycle = true)
        {
            foreach (var area in Areas)
            {
                area.MaterialsExit(this);
            }

            Areas.Clear();
            if (needRecycle)
            {
                RecycleMaterials();
            }
        }

        private void RecycleMaterials()
        {
            if (Recycle == null)
            {
                if (this!=null)
                {
                    Destroy(gameObject);
                }
            }
            else
            {
                Recycle(this);
            }
        }

        private void Enter(PhysicalCollisionArea area)
        {
            Areas.Add(area);
            area.MaterialsEnter(this);
        }

        private void Exit(PhysicalCollisionArea area)
        {
            if (Areas.Contains(area))
            {
                Areas.Remove(area);
            }

            area.MaterialsExit(this);
        }

        private void OnCollisionEnter(Collision other)
        {
            if (_isRunning)
            {
                if (other.transform.TryGetComponent<PhysicalCollisionArea>(out var area))
                {
                    Enter(area);
                }
            }
        }

        private void OnCollisionExit(Collision other)
        {
            if (_isRunning)
            {
                if (other.transform.TryGetComponent<PhysicalCollisionArea>(out var area))
                {
                    Exit(area);
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_isRunning)
            {
                if (other.transform.TryGetComponent<PhysicalCollisionArea>(out var area))
                {
                    Enter(area);
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (_isRunning)
            {
                if (other.transform.TryGetComponent<PhysicalCollisionArea>(out var area))
                {
                    Exit(area);
                }
            }
        }
    }
}
