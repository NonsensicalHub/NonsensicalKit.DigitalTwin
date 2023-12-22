using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using System.Collections.Generic;
using UnityEngine;


namespace NonsensicalKit.DigitalTwin.LogicNodeTreeSystem
{
    public class MulitNodeControlActive : NonsensicalMono
    {
        [SerializeField] private List<string> m_nodesName;

        private GameObject _controlTarget;

        private bool _isRunning;

        private LogicNodeManager _manager;

        private void Awake()
        {
            ServiceCore.SafeGet<LogicNodeManager>(OnGetService);
        }

        private void OnGetService(LogicNodeManager service)
        {
            _controlTarget = gameObject;
            _manager = service;
            Init();
        }

        private void OnEnable()
        {
            if (_isRunning && _manager.CrtSelectNode != null)
            {
                OnSwitchNode(_manager.CrtSelectNode);
            }
        }


        public void Close()
        {
            _isRunning = false;
            Unsubscribe<LogicNode>((int)LogicNodeEnum.SwitchNode, OnSwitchNode);
        }

        private void Init()
        {
            _isRunning = true;

            Subscribe<LogicNode>((int)LogicNodeEnum.SwitchNode, OnSwitchNode);
            if (_manager.CrtSelectNode != null)
            {
                OnSwitchNode(_manager.CrtSelectNode);
            }
        }

        private void OnSwitchNode(LogicNode node)
        {
        }
    }
}
