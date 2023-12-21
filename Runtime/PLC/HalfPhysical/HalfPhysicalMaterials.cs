using UnityEngine;

namespace NonsensicalKit.Editor.PLC
{
    public class HalfPhysicalMaterials : MonoBehaviour
    {
        private bool isRunning;

        private Rigidbody rb;

        private int moveCount;
        private Vector3 offset;

        private bool isFixed;

        private void Awake()
        {
            if (TryGetComponent<Rigidbody>(out rb) == false)
            {
                rb = gameObject.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.isKinematic = true;
            }
            if (GetComponent<Collider>() == null)
            {
                gameObject.AddComponent<BoxCollider>();
            }
            isRunning = true;
        }

        private void Start()
        {
            rb.MovePosition(transform.position);
        }
        private void Update()
        {
            if (!isFixed && moveCount > 0)
            {
                transform.position += offset / moveCount * Time.deltaTime;
                moveCount = 0;
                offset = Vector3.zero;
            }
        }


        private void OnDestroy()
        {
            isRunning = false;
        }

        public void Init(Transform target)
        {
            transform.SetPositionAndRotation(target.position, target.rotation);
            gameObject.SetActive(true);
        }

        public void Fixed(bool isFixed)
        {
            this.isFixed = isFixed;
        }

        public void Move(Vector3 offset)
        {
            moveCount++;
            this.offset += offset;
        }

        protected virtual void OnCollisionEnter(Collision collision)
        {
            if (isRunning)
            {
                if (collision.transform.TryGetComponent<HalfPhysicalCollisionArea>(out var hpc))
                {
                    hpc.MaterialsEnter(this);
                }
            }
        }
        protected virtual void OnCollisionExit(Collision collision)
        {
            if (isRunning)
            {
                if (collision.transform.TryGetComponent<HalfPhysicalCollisionArea>(out var hpc))
                {
                    hpc.MaterialsExit(this);
                }
            }
        }
    }
}
