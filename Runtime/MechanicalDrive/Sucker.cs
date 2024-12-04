using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    public class Sucker : MonoBehaviour, IHoldMaterial
    {
        [SerializeField] private Transform m_suckPos;

        public Transform HoldPoint => m_suckPos;

        private bool _sucking;
        private bool _suckingMaterial;
        private Material _touchMaterial;

        private void OnTriggerEnter(Collider other)
        {
            if (_touchMaterial == null)
            {
                if (other.gameObject.TryGetComponent<Material>(out var v))
                {
                    _touchMaterial = v;
                    if (_sucking && !v.Holding)
                    {
                        _suckingMaterial = true;
                        _touchMaterial.Hold(this);
                    }
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!_suckingMaterial && _touchMaterial != null)
            {
                if (other.gameObject.TryGetComponent<Material>(out var v))
                {
                    if (_touchMaterial == v)
                    {
                        _touchMaterial = null;
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
                if (_touchMaterial != null)
                {
                    if (!_touchMaterial.Holding)
                    {
                        _suckingMaterial = true;
                        _touchMaterial.Hold(this);
                    }
                    else
                    {
                        _touchMaterial = null;
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
                    _touchMaterial.Free();
                    _touchMaterial = null;
                }
            }
        }
    }
}
