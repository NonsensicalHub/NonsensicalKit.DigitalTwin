using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    public class Sucker : MonoBehaviour, IHoldMaterial
    {
        [SerializeField] private Transform m_suckPos;

        public Transform HoldPoint => m_suckPos;

        private bool _sucking;
        private bool _suckingMaterial;
        private RigidbodyMaterial _touchRigidbodyMaterial;

        private void OnTriggerEnter(Collider other)
        {
            if (_touchRigidbodyMaterial == null)
            {
                if (other.gameObject.TryGetComponent<RigidbodyMaterial>(out var v))
                {
                    _touchRigidbodyMaterial = v;
                    if (_sucking && !v.Holding)
                    {
                        _suckingMaterial = true;
                        _touchRigidbodyMaterial.Hold(this);
                    }
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!_suckingMaterial && _touchRigidbodyMaterial != null)
            {
                if (other.gameObject.TryGetComponent<RigidbodyMaterial>(out var v))
                {
                    if (_touchRigidbodyMaterial == v)
                    {
                        _touchRigidbodyMaterial = null;
                    }
                }
            }
        }

        public void Switch(bool value)
        {
            if (_sucking != value)
            {
                if (value)
                {
                    On();
                }
                else
                {
                    Off();
                }
            }
        }

        public void On()
        {
            if (!_sucking)
            {
                _sucking = true;
                if (_touchRigidbodyMaterial != null)
                {
                    if (!_touchRigidbodyMaterial.Holding)
                    {
                        _suckingMaterial = true;
                        _touchRigidbodyMaterial.Hold(this);
                    }
                    else
                    {
                        _touchRigidbodyMaterial = null;
                    }
                }
            }
        }

        public void Off()
        {
            if (_sucking)
            {
                _sucking = false;
                if (_suckingMaterial)
                {
                    _suckingMaterial = false;
                    _touchRigidbodyMaterial.Free();
                    _touchRigidbodyMaterial = null;
                }
            }
        }
    }
}
