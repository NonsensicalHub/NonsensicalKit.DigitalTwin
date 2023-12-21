using NonsensicalKit.Editor.Service.Config;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.Editor.LogicNodeTreeSystem
{
    /// <summary>
    /// 自定义序列化存储参考： https://docs.unity3d.com/cn/current/Manual/script-Serialization-Custom.html
    /// </summary>
    [CreateAssetMenu(fileName = "LogicNodeTree", menuName = "ScriptableObjects/LogicNodeTreeConfigData")]
    public class LogicNodeTreeAsset : ConfigObject, ISerializationCallbackReceiver
    {
        public LogicNodeTreeConfigData ConfigData;

        public void OnBeforeSerialize()
        {
            if (ConfigData != null)
            {
                ConfigData.OnBeforeSerialize();
            }
        }

        public void OnAfterDeserialize()
        {
            ConfigData.OnAfterDeserialize();
        }

        public override ConfigData GetData()
        {
            return ConfigData;
        }

        public override void AfterSetData()
        {
            base.AfterSetData();
            ConfigData.OnAfterDeserialize();
        }

        public override void SetData(ConfigData cd)
        {
            if (CheckType<LogicNodeTreeConfigData>(cd))
            {
                ConfigData = cd as LogicNodeTreeConfigData;
            }
        }
    }

    [System.Serializable]
    public class LogicNodeTreeConfigData : ConfigData
    {
        [System.NonSerialized]
        public LogicNodeData Root;
        public List<SerializableNode> SerializedNodes = new List<SerializableNode>();

        public void OnBeforeSerialize()
        {
            if (Root == null)
            {
                Debug.Log("根节点为空");
                Root = new LogicNodeData("root");
            }
            SerializedNodes.Clear();
            AddNodeToSerializedNodes(Root);
        }

        public void OnAfterDeserialize()
        {
            if (SerializedNodes.Count > 0)
            {
                ReadNodeFromSerializedNodes(0, out Root);
            }
            else
            {
                Debug.Log("序列化链表为空");
                Root = new LogicNodeData("root");
            }
        }

        private void AddNodeToSerializedNodes(LogicNodeData n)
        {
            var serializedNode = new SerializableNode()
            {
                NodeName = n.NodeName,
                ChildCount = n.Children.Count,
            };

            SerializedNodes.Add(serializedNode);
            foreach (var child in n.Children)
                AddNodeToSerializedNodes(child);
        }

        private int ReadNodeFromSerializedNodes(int index, out LogicNodeData node)
        {
            var serializedNode = SerializedNodes[index];
            LogicNodeData newNode = new LogicNodeData()
            {
                NodeName = serializedNode.NodeName,
                Children = new List<LogicNodeData>()
            };

            for (int i = 0; i < serializedNode.ChildCount; i++)
            {
                LogicNodeData childNode;
                index = ReadNodeFromSerializedNodes(++index, out childNode);
                childNode.Parent = newNode;
                newNode.Children.Add(childNode);
            }
            node = newNode;
            return index;
        }
    }

    public class LogicNodeData : TreeData<LogicNodeData>
    {
        public string NodeName;     //节点名，ID
        public LogicNodeData Parent;
        public List<LogicNodeData> Children = new List<LogicNodeData>();

        public LogicNodeData()
        {
        }
        public LogicNodeData(string nodeName)
        {
            this.NodeName = nodeName;
        }

        public override List<LogicNodeData> GetChildren()
        {
            return Children;
        }
    }

    // 用于序列化的 Node 类。
    [System.Serializable]
    public struct SerializableNode
    {
        public string NodeName;     //节点名，ID
        public string AliasName;    //别名
        public int ChildCount;
    }

    public abstract class TreeData<T>
    {
        public bool IsFoldout = true;
        public abstract List<T> GetChildren();
    }
}
