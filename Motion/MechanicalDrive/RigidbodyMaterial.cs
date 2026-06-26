using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Motion
{
    public interface IHoldMaterial
    {
        public Transform HoldPoint { get; }
    }

    [RequireComponent(typeof(Rigidbody))]
    public class RigidbodyMaterial : MonoBehaviour
    {
        [SerializeField] private Transform[] m_pos;
        [SerializeField] private bool m_initStand;

        public bool Holding => _hold != null;

        private Rigidbody _rb;
        private Vector3 _startPos;
        private Quaternion _startRot;

        private IHoldMaterial _hold;
        private bool _canMove;

        protected void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (m_initStand)
            {
                Stand();
            }
            else
            {
                Free();
            }
        }

        private void Start()
        {
            _startPos = transform.position;
            _startRot = transform.rotation;
            gameObject.SetActive(false);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.TryGetComponent<DestroySpace>(out _))
            {
                OnReset();
                gameObject.SetActive(false);
            }
        }

        public void Move(Vector3 offset)
        {
            if (_canMove && !Holding)
            {
                if (_rb.isKinematic)
                {
                    _rb.MovePosition(transform.position + offset);
                }
                else
                {
                    SetVelocity(offset / Time.deltaTime);
                }
            }
        }

        public void SetPos(int index)
        {
            transform.position = m_pos[index].position;

            gameObject.SetActive(true);
        }

        public void SetPos(int index, bool resetRotation )
        {
            transform.position = m_pos[index].position;
            if (resetRotation)
            {
                transform.rotation = Quaternion.identity;
            }

            gameObject.SetActive(true);
        }

        public void OnReset()
        {
            gameObject.SetActive(true);
            SetVelocity(Vector3.zero);
            transform.position = _startPos;
            transform.rotation = _startRot;
            transform.SetParent(null);
        }

        public void Free()
        {
            _canMove = true;
            _hold = null;
            _rb.isKinematic = false;
            _rb.angularVelocity = Vector3.zero;
            transform.SetParent(null);
        }

        /// <summary>
        /// 停留在原地
        /// </summary>
        public void Stand()
        {
            _canMove = false;
            _hold = null;
            _rb.isKinematic = true;
            transform.SetParent(null);
            gameObject.SetActive(true);
        }

        public void StandWith(Transform parent)
        {
            _canMove = false;
            _hold = null;
            _rb.isKinematic = true;
            transform.SetParent(parent);
            gameObject.SetActive(true);
        }

        public void Hold(IHoldMaterial iHold)
        {
            _canMove = false;
            _hold = iHold;
            _rb.isKinematic = true;
            transform.SetParent(iHold.HoldPoint);
        }

        private void SetVelocity(Vector3 speed)
        {
#if UNITY_6000_0_OR_NEWER
            _rb.linearVelocity = speed;
#else
            _rb.velocity = speed;
#endif
        }
    }
}
