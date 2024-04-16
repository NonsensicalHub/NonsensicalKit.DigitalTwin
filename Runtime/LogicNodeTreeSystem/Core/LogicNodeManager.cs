using NonsensicalKit.Core;
using NonsensicalKit.Core.Log;
using NonsensicalKit.Core.Service;
using System;
using System.Collections.Generic;

namespace NonsensicalKit.DigitalTwin.LogicNodeTreeSystem
{
    public class LogicNodeManager : NonsensicalMono, IMonoService
    {
        public Action OnSwitchEnd { get; set; }  //切换后调用一次，然后清空

        public LogicNode CrtSelectNode { get; private set; } //当前选择的节点

        public bool IsReady { get; set; }

        public Action InitCompleted { get; set; }

        private LogicNode _root;    //根节点
        private Dictionary<string, LogicNode> _dic = new Dictionary<string, LogicNode>();   //所有节点的字典，用于快速查找

        private string _switchBuffer;

        #region Public Mothod

        public void InitConfig(LogicNodeTreeConfigData configData)
        {
            configData.OnAfterDeserialize();
            BuildLogicNodeTree(configData.Root);
            BuildDictionary();
            if (string.IsNullOrEmpty(_switchBuffer) == false)
            {
                SwitchNode(_switchBuffer);
            }
            CrtSelectNode = _root;
            if (!IsReady)
            {
                IsReady = true;
                InitCompleted?.Invoke();
                InitCompleted = null;
            }
            else
            {
                Publish((int)LogicNodeEnum.SwitchNode, CrtSelectNode);
            }
        }

        public LogicNode GetNode(string nodeName)
        {
            if (_dic.ContainsKey(nodeName))
            {
                return _dic[nodeName];
            }
            else
            {
                LogCore.Debug("未找到节点：" + nodeName);
                return null;
            }
        }

        public void SwitchNode(string nodeName)
        {
            ;
            if (_dic.ContainsKey(nodeName))
            {
                SwitchNodeCheck(_dic[nodeName]);
            }
            else
            {
                _switchBuffer = nodeName;
            }
        }

        /// <summary>
        /// 返回上一级
        /// </summary>
        /// <param name="nodeName"></param>
        public bool ReturnPreviousLevel()
        {
            if (CrtSelectNode != null)
            {
                if (CrtSelectNode.ParentNode == null)
                {
                    LogCore.Debug("当前为顶节点，无法返回上一级节点");
                    return false;
                }
                SwitchNodeCheck(CrtSelectNode.ParentNode);

                return true;
            }
            else
            {
                LogCore.Warning("当前未选择节点");

                return false;
            }
        }

        /// <summary>
        /// 检测所选节点是否被选中
        /// </summary>
        /// <param name="nodeName"></param>
        /// <returns></returns>
        public bool CheckState(string nodeName)
        {
            if (CrtSelectNode != null && _dic.ContainsKey(nodeName))
            {
                LogicNode crt = _dic[nodeName];

                if (crt == CrtSelectNode)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检测所选节点或者其父节点是否
        /// </summary>
        /// <param name="nodeName"></param>
        /// <returns></returns>
        public bool CheckStateWithParent(string nodeName)
        {
            if (CrtSelectNode != null && _dic.ContainsKey(nodeName))
            {
                LogicNode crt = _dic[nodeName];

                while (crt != null)
                {
                    if (crt == CrtSelectNode)
                    {
                        return true;
                    }
                    else
                    {
                        crt = crt.ParentNode;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 检测所选节点或者其子节点是否
        /// </summary>
        /// <param name="nodeName"></param>
        /// <returns></returns>
        public bool CheckStateWithChild(string nodeName)
        {
            if (CrtSelectNode != null && _dic.ContainsKey(nodeName))
            {
                LogicNode checkNode = _dic[nodeName];

                Queue<LogicNode> nodes = new Queue<LogicNode>();

                nodes.Enqueue(checkNode);

                while (nodes.Count > 0)
                {
                    LogicNode crt = nodes.Dequeue();
                    if (crt == CrtSelectNode)
                    {
                        return true;
                    }
                    else
                    {
                        foreach (var item in crt.ChildNode)
                        {
                            nodes.Enqueue(item);
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 更新对应节点的状态并返回是否状态有修改的布尔值
        /// </summary>
        /// <param name="nodeName"></param>
        /// <param name="nodeState"></param>
        /// <returns></returns>
        public bool UpdateState(string nodeName, LogicNodeState nodeState)
        {
            bool originState1 = nodeState.isSelect;
            bool originState2 = nodeState.parentSelect;
            if (_dic.ContainsKey(nodeName))
            {
                LogicNode crt = _dic[nodeName];
                nodeState.isSelect = crt == CrtSelectNode;

                nodeState.parentSelect = false;
                while (crt != null)
                {
                    if (crt == CrtSelectNode)
                    {
                        nodeState.parentSelect = true;
                        break;
                    }
                    else
                    {
                        crt = crt.ParentNode;
                    }
                }
            }
            else
            {
                nodeState.isSelect = false;
                nodeState.parentSelect = false;
            }

            if (originState1 != nodeState.isSelect || originState2 != nodeState.parentSelect)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        #endregion


        #region Private Method


        /// <summary>
        /// 构建节点树
        /// </summary>
        /// <param name="datas"></param>
        private void BuildLogicNodeTree(LogicNodeData root)
        {
            Queue<LogicNode> sns = new Queue<LogicNode>();
            Queue<LogicNodeData> snds = new Queue<LogicNodeData>();

            this._root = new LogicNode(root.NodeName, null, new LogicNode[root.Children.Count]);

            sns.Enqueue(this._root);
            snds.Enqueue(root);

            while (sns.Count > 0)
            {
                LogicNode crtNode = sns.Dequeue();
                LogicNodeData crtNodeData = snds.Dequeue();

                for (int i = 0; i < crtNodeData.Children.Count; i++)
                {
                    int length = crtNodeData.Children[i].Children.Count;
                    var newNode = new LogicNode(crtNodeData.Children[i].NodeName, crtNode, new LogicNode[length]);
                    crtNode.ChildNode[i] = newNode;
                    if (length > 0)
                    {
                        sns.Enqueue(newNode);
                        snds.Enqueue(crtNodeData.Children[i]);
                    }
                }
            }
        }

        /// <summary>
        /// 构建字典
        /// </summary>
        private void BuildDictionary()
        {
            _dic = new Dictionary<string, LogicNode>();

            Queue<LogicNode> nodes = new Queue<LogicNode>();
            nodes.Enqueue(_root);

            while (nodes.Count > 0)
            {
                LogicNode crtSN = nodes.Dequeue();
                if (_dic.ContainsKey(crtSN.NodeName))
                {
                    LogCore.Warning($"节点名称重复:{crtSN.NodeName}");
                }
                else
                {
                    _dic.Add(crtSN.NodeName, crtSN);
                }

                foreach (var item in crtSN.ChildNode)
                {
                    nodes.Enqueue(item);
                }
            }
        }

        private void SwitchNodeCheck(LogicNode node)
        {
            if (node != CrtSelectNode)
            {
                LogCore.Debug("切换到节点:" + node.NodeName);
                CrtSelectNode = node;
                Publish((int)LogicNodeEnum.SwitchNode, CrtSelectNode);
                if (OnSwitchEnd != null)
                {
                    //使用临时变量存储并清除原始数据后再执行，防止出现循环调用
                    Action onSwitchEnd = OnSwitchEnd;
                    OnSwitchEnd = null;
                    onSwitchEnd?.Invoke();
                }
            }
        }
        #endregion
    }

    /// <summary>
    /// 树节点，存有父节点和子节点数组的信息
    /// </summary>
    public class LogicNode
    {
        public string NodeName;
        public LogicNode ParentNode;
        public LogicNode[] ChildNode;

        public LogicNode(string nodeName, LogicNode parentNode, LogicNode[] childNode)
        {
            NodeName = nodeName;
            ParentNode = parentNode;
            ChildNode = childNode;
        }
    }
}
