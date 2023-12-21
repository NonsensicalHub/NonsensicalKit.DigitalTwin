using UnityEngine;

namespace NonsensicalKit.Editor.LogicNodeTreeSystem
{
    /// <summary>
    /// 用于某些虚拟节点(无对应的实体对象)的自动跳转
    /// </summary>
    public class AutoJumpNode : LogicNodeSwitchBase
    {
        [SerializeField] private string m_jumpTarget; //跳转的目标对象，为空时跳转到上一级


        protected override void Awake()
        {
            base.Awake();
        }

        protected override void OnSwitch(LogicNodeState lns)
        {
            if (_manager==null)
            {
                return;
            }
            if (lns.isSelect==false)
            {
                return;
            }
            if (string.IsNullOrEmpty(m_jumpTarget))
            {
                var node = _manager.GetNode(_nodeMono.NodeName);
                if (node != null && node.ParentNode != null)
                {
                    node = node.ParentNode;
                    if (node!=null)
                    {
                        _manager.OnSwitchEnd += () => _manager.SwitchNode(node.NodeName);
                    }
                }
            }
            else
            {

                _manager.OnSwitchEnd += () => _manager.SwitchNode(m_jumpTarget);
            }

        }
    }
}
