using NonsensicalKit.Core.Service;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.LogicNodeTreeSystem
{
    public class LogicNodeConfigurator : MonoBehaviour
    {
        [SerializeField] private LogicNodeTreeAsset m_config;

        private void Awake()
        {
            ServiceCore.Get<LogicNodeManager>().InitConfig(m_config.ConfigData);
        }
    }
}
