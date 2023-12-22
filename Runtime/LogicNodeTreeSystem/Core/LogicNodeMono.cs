using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace NonsensicalKit.DigitalTwin.LogicNodeTreeSystem
{
    public class LogicNodeMono : NonsensicalMono
    {
        [Serializable]
        public class NodeSwitchEvent : UnityEvent<LogicNodeState> { }

        [SerializeField] private string m_nodeName;

        [FormerlySerializedAs("onSwitch")]
        [SerializeField]
        private NodeSwitchEvent m_OnSwitch = new NodeSwitchEvent();

        public string NodeName => m_nodeName;

        private LogicNodeManager _manager;

        public NodeSwitchEvent OnSwitch
        {
            get { return m_OnSwitch; }
            set { m_OnSwitch = value; }
        }

        public LogicNodeState NodeState { get; private set; }

        private void Awake()
        {
            ServiceCore.SafeGet<LogicNodeManager>(OnGetService);
        }

        private  void OnGetService(LogicNodeManager service)
        {
            NodeState = new LogicNodeState();

            Subscribe<LogicNode>((int)LogicNodeEnum.SwitchNode, OnSwitchNode);
            _manager = service;
            _manager.UpdateState(m_nodeName, NodeState);
            m_OnSwitch.Invoke(NodeState);
        }

        private void OnSwitchNode(LogicNode sn)
        {
            if (_manager.UpdateState(m_nodeName, NodeState))
            {
                m_OnSwitch.Invoke(NodeState);
            }
        }

#if UNITY_EDITOR
        [ContextMenu("设置节点名称为物体名称")]
        private void SetGameObjectNameToNodeName()
        {
            m_nodeName = gameObject.name;
        }
#endif
    }

    public class LogicNodeState
    {
        /// <summary>
        /// 是否被选中
        /// </summary>
        public bool isSelect;
        /// <summary>
        /// 是否选中了父节点
        /// </summary>
        public bool parentSelect;
    }
}
