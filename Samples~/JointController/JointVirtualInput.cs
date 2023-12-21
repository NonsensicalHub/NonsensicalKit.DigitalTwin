using NonsensicalKit.Editor;
using NonsensicalKit.Editor;
using UnityEngine;

namespace PLCKit
{
    public class JointVirtualInput : MonoBehaviour
    {
        [SerializeField] private JointController m_target;

        [SerializeField] private float m_interval = 1;

        [SerializeField] private float[] m_virtualValues;

        private float _timer;

        private void Awake()
        {
            if (!PlatformInfo.IsEditor)
            {
                Destroy(this);
                return;
            }
            if (m_target)
            {
                int num = m_target.Joints.Length;

                m_virtualValues = new float[num];

                for (int i = 0; i < num; i++)
                {
                    m_virtualValues[i] = m_target.Joints[i].InitialValue;
                }
            }
        }

        private void Update()
        {
            _timer += Time.deltaTime;

            if (_timer >= m_interval)
            {
                _timer -= 1;

                if (m_target)
                {
                    ActionData ad = new ActionData(m_virtualValues, m_interval);

                    m_target.ChangeState(ad);
                }
            }
        }
    }
}
