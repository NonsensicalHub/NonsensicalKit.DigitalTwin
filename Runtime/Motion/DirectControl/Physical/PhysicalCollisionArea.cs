using System.Collections.Generic;
using NonsensicalKit.Tools;
using UnityEngine;
using UnityEngine.Events;

namespace NonsensicalKit.DigitalTwin.Motion
{
    public class PhysicalCollisionArea : MonoBehaviour
    {
        [SerializeField] private UnityEvent<PhysicalMaterials> m_onMaterialsEnter = new();

        [SerializeField] private UnityEvent<PhysicalMaterials> m_onMaterialsExit = new();

        public UnityEvent<PhysicalMaterials> OnMaterialsEnter => m_onMaterialsEnter;

        public UnityEvent<PhysicalMaterials> OnMaterialsExit => m_onMaterialsExit;

        public readonly List<PhysicalMaterials> Materials = new();

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

        public void MaterialsEnter(PhysicalMaterials materials)
        {
            Materials.Add(materials);
            m_onMaterialsEnter?.Invoke(materials);
        }

        public void MaterialsExit(PhysicalMaterials materials)
        {
            if (Materials.Contains(materials))
            {
                Materials.Remove(materials);
            }

            m_onMaterialsExit?.Invoke(materials);
        }

    }
}
