using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.LogicNodeTreeSystem
{
    /// <summary>
    /// 此节点控制的判断条件
    /// </summary>
    public enum ControlType
    {
        SelfSelect,     //自己是否被选中
        SelfUnselect,     //自己是否未被选中
        ParentSelect,   //自己或父节点是否被选中
        ChildSelect,    //自己或子节点是否被选中
        ParentOrChildSelect //自己或父节点或子节点被选中
    }

    /// <summary>
    /// 逻辑节点控制物体激活,挂载在需要控制的GameObject对象上
    /// </summary>
    public class LogicNodeControlActive : NonsensicalMono
    {
        [SerializeField] private ControlType m_controlType;
        [SerializeField] private string m_nodeName;
        [SerializeField] private List<string> m_spOn;
        [SerializeField] private List<string> m_spOff;

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
            if (m_spOn.Contains(node.NodeName))
            {
                _controlTarget.SetActive(true);
                return;
            }
            if (m_spOff.Contains(node.NodeName))
            {
                _controlTarget.SetActive(false);
                return;
            }

            switch (m_controlType)
            {
                case ControlType.SelfSelect:
                    _controlTarget.SetActive(node.NodeName == m_nodeName);
                    break;
                case ControlType.SelfUnselect:
                    _controlTarget.SetActive(node.NodeName != m_nodeName);
                    break;
                case ControlType.ParentSelect:
                    _controlTarget.SetActive(_manager.CheckStateWithParent(m_nodeName));
                    break;
                case ControlType.ChildSelect:
                    _controlTarget.SetActive(_manager.CheckStateWithChild(m_nodeName));
                    break;
                case ControlType.ParentOrChildSelect:
                    _controlTarget.SetActive(_manager.CheckStateWithParent(m_nodeName) || _manager.CheckStateWithChild(m_nodeName));
                    break;
            }
        }
    }
}
