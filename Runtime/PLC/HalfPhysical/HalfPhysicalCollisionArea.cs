using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace NonsensicalKit.DigitalTwin.PLC
{
    public class HalfPhysicalCollisionArea : MonoBehaviour
    {
        [FormerlySerializedAs("OnMaterialsEnter")]
        public UnityEvent<HalfPhysicalMaterials> m_OnMaterialsEnter = new();

        [FormerlySerializedAs("OnMaterialsExit")]
        public UnityEvent<HalfPhysicalMaterials> m_OnMaterialsExit = new();

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
            m_OnMaterialsEnter.RemoveAllListeners();
            m_OnMaterialsExit.RemoveAllListeners();
        }

        public void MaterialsEnter(HalfPhysicalMaterials materials)
        {
            m_OnMaterialsEnter?.Invoke(materials);
        }

        public void MaterialsExit(HalfPhysicalMaterials materials)
        {
            m_OnMaterialsExit?.Invoke(materials);
        }
    }
}
