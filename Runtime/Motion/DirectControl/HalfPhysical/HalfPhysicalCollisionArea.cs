using UnityEngine;
using UnityEngine.Events;

namespace NonsensicalKit.DigitalTwin.Motion
{
    public class HalfPhysicalCollisionArea : MonoBehaviour
    {
        [SerializeField] private UnityEvent<HalfPhysicalMaterials> m_onMaterialsEnter = new();

        [SerializeField] private UnityEvent<HalfPhysicalMaterials> m_onMaterialsExit = new();

        public UnityEvent<HalfPhysicalMaterials> OnMaterialsEnter => m_onMaterialsEnter;

        public UnityEvent<HalfPhysicalMaterials> OnMaterialsExit => m_onMaterialsExit;

        private void Awake()
        {
            if (GetComponent<Rigidbody>() == null)
            {
                var rb = gameObject.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.isKinematic = true;
            }
        }

        private void OnDestroy()
        {
            m_onMaterialsEnter.RemoveAllListeners();
            m_onMaterialsExit.RemoveAllListeners();
        }

        public void MaterialsEnter(HalfPhysicalMaterials materials)
        {
            m_onMaterialsEnter?.Invoke(materials);
        }

        public void MaterialsExit(HalfPhysicalMaterials materials)
        {
            m_onMaterialsExit?.Invoke(materials);
        }
    }
}
