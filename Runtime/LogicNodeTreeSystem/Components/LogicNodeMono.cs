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
        [SerializeField] private string m_nodeName;

        [SerializeField] private UnityEvent m_NodeEnter = new UnityEvent();
        [SerializeField] private UnityEvent m_NodeExit = new UnityEvent();

        public string NodeName => m_nodeName;

        public UnityEvent OnNodeEnter
        {
            get { return m_NodeEnter; }
            set { m_NodeEnter = value; }
        }
        public UnityEvent OnNodeExit
        {
            get { return m_NodeExit; }
            set { m_NodeExit = value; }
        }

        private LogicNodeManager _manager;

        private void Awake()
        {
            ServiceCore.SafeGet<LogicNodeManager>(OnGetService);
        }

        private void OnGetService(LogicNodeManager service)
        {
            _manager = service;

            Subscribe((int)LogicNodeEnum.NodeEnter, OnSwitchEnter);
            Subscribe((int)LogicNodeEnum.NodeExit, OnSwitchExit);

        }

        private void OnSwitchEnter()
        {
            m_NodeEnter.Invoke();

        }
        private void OnSwitchExit()
        {
            m_NodeExit.Invoke();
        }

#if UNITY_EDITOR
        [ContextMenu("设置节点名称为物体名称")]
        private void SetGameObjectNameToNodeName()
        {
            m_nodeName = gameObject.name;
        }
#endif
    }
}
