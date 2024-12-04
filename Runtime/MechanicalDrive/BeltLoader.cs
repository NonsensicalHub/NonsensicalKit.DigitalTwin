using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    public class BeltLoader : MonoBehaviour
    {
        [SerializeField] private AxisDir m_axisDir;
        [SerializeField] private float m_speedScale = 1;

        [ReorderableList]
        private HashSet<Material> _materials = new();

        public void Move(float value)
        {
            foreach (var item in _materials)
            {
                Vector3 dir;
                switch (m_axisDir)
                {
                    default:
                    case AxisDir.X: dir = transform.right; break;
                    case AxisDir.Y: dir = transform.up; break;
                    case AxisDir.Z: dir = transform.forward; break;
                    case AxisDir.IX: dir = -transform.right; break;
                    case AxisDir.IY: dir = -transform.up; break;
                    case AxisDir.IZ: dir = -transform.forward; break;
                }

                item.Move(dir * (value * m_speedScale));
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.transform.TryGetComponent<Material>(out var v))
            {
                _materials.Add(v);
            }
        }

        private void OnCollisionExit(Collision collision)
        {
            if (collision.transform.TryGetComponent<Material>(out var v))
            {
                if (_materials.Contains(v))
                {
                    _materials.Remove(v);
                }
            }
        }
    }
}
