using NonsensicalKit.Core.Service;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.LogicNodeTreeSystem
{
    public class LogicNodeSwitcher : MonoBehaviour
    {
        [SerializeField] private string m_targetNodeName;

        private LogicNodeManager _manager;
        private void Awake()
        {
            ServiceCore.SafeGet<LogicNodeManager>(OnGetManager);
        }

        public void Switch()
        {
            if (_manager != null)
            {
                _manager.SwitchNode(m_targetNodeName);
            }
        }

        private void OnGetManager(LogicNodeManager manager)
        {
            _manager = manager;
        }
    }
}
