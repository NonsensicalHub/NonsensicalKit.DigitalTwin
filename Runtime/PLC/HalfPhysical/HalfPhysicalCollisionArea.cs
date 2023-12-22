using UnityEngine;
using UnityEngine.Events;

namespace NonsensicalKit.DigitalTwin.PLC
{
    public class HalfPhysicalCollisionArea : MonoBehaviour
    {
        public UnityEvent<HalfPhysicalMaterials> OnMaterialsEnter = new UnityEvent<HalfPhysicalMaterials>();
        public UnityEvent<HalfPhysicalMaterials> OnMaterialsExit = new UnityEvent<HalfPhysicalMaterials>();

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
            OnMaterialsEnter.RemoveAllListeners();
            OnMaterialsExit.RemoveAllListeners();
        }

        public void MaterialsEnter(HalfPhysicalMaterials materials)
        {
            OnMaterialsEnter?.Invoke(materials);
        }

        public void MaterialsExit(HalfPhysicalMaterials materials)
        {
            OnMaterialsExit?.Invoke(materials);
        }
    }
}
