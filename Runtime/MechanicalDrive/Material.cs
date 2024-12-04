using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    public interface IHoldMaterial
    {
        public Transform HoldPoint { get; }
    }

    public class Material : MonoBehaviour
    {
        [SerializeField] private Transform[] m_pos;

        public bool Holding => _hold != null;

        private Rigidbody _rb;
        private Vector3 _startPos;
        private Quaternion _startRot;

        private IHoldMaterial _hold;

        protected void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            Free();
        }

        private void Start()
        {
            _startPos = transform.position;
            _startRot = transform.rotation;
            gameObject.SetActive(false);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.TryGetComponent<DestroySpace>(out var v))
            {
                OnReset();
                gameObject.SetActive(false);
            }
        }

        public void Move(Vector3 offset)
        {
            if (!Holding)
            {
                if (_rb.isKinematic)
                {
                    _rb.MovePosition(transform.position + offset);
                }
                else
                {
                    _rb.velocity = offset / Time.deltaTime;
                }
            }
        }

        public void SetPos(int index)
        {
            transform.position = m_pos[index].position;
            gameObject.SetActive(true);
        }

        public void OnReset()
        {
            gameObject.SetActive(true);
            _rb.velocity = Vector3.zero;
            transform.position = _startPos;
            transform.rotation = _startRot;
            transform.parent = null;
        }

        public void Free()
        {
            _hold = null;
            _rb.isKinematic = false;
            transform.parent = null;
        }

        public void Hold(IHoldMaterial iHold)
        {
            _hold = iHold;
            _rb.isKinematic = true;
            var v = iHold.HoldPoint;
            transform.parent = v;
        }
    }
}
