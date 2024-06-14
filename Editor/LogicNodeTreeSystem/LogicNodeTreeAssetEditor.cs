using NonsensicalKit.DigitalTwin.LogicNodeTreeSystem;
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Editor.LogicNodeTreeSystem
{
    [CustomEditor(typeof(LogicNodeTreeAsset))]
    public class LogicNodeTreeAssetEditor : UnityEditor.Editor
    {
        private SerializedProperty _idProperty;
        private LogicNodeTreeAsset _asset;
        private NodeTreeEdit<LogicNodeData> _nodeTreeEdit;

        private void OnEnable()
        {
            _asset = (LogicNodeTreeAsset)target;
            if (_asset == null)
            {
                return;
            }
            if (_asset.GetData() == null)
            {
                return;
            }
            var v = (_asset.GetData() as LogicNodeTreeConfigData).Root;
            if (v != null)
            {
                _nodeTreeEdit = new NodeTreeEdit<LogicNodeData>((_asset.GetData() as LogicNodeTreeConfigData).Root);

                _nodeTreeEdit.GetHeadString += GetHeaderString;
                _nodeTreeEdit.OnDrawElement += DrawElement;
                _nodeTreeEdit.OnAddElement += ChildAdd;
                _nodeTreeEdit.OnRemoveElement = ChildRemove;
                _nodeTreeEdit.OnRemoveSelf = RemoveSelf;
                _nodeTreeEdit.ElementHeight = 42;

                _idProperty = serializedObject.FindProperty("ConfigData.ConfigID");
            }
        }

        public override void OnInspectorGUI()
        {
            if ((_asset.GetData() as LogicNodeTreeConfigData).Root == null)
            {
                return;
            }

            serializedObject.Update();
            if (_idProperty != null)
            {
                EditorGUILayout.PropertyField(_idProperty);
            }

            serializedObject.ApplyModifiedProperties();

            if (_nodeTreeEdit != null)
            {
                Rect rect = EditorGUI.IndentedRect(GUILayoutUtility.GetRect(0f, _nodeTreeEdit.GetTotalHeight()));

                if (_nodeTreeEdit.DrawNodeTree(rect))
                {
                    //重要！！！！！！这函数卡了我一天
                    EditorUtility.SetDirty(_asset);
                }
            }
        }

        private void ChildAdd(LogicNodeData node)
        {
            var v = new LogicNodeData("newNode");
            v.Parent = node;
            node.Children.Add(v);
        }

        private void ChildRemove(LogicNodeData node)
        {
            node.Children.RemoveAt(node.Children.Count - 1);
        }

        private void RemoveSelf(LogicNodeData node)
        {
            if (node.Parent != null)
            {
                node.Parent.Children.Remove(node);
            }
        }

        private string GetHeaderString(LogicNodeData node)
        {
            return node.NodeID;
        }

        private void DrawElement(Rect rect, LogicNodeData node)
        {
            float width = rect.width * 0.5f;
            float height = (rect.height - 2) * 0.5f;
            Rect newRect = new Rect(rect.x, rect.y, width, height);
            GUI.Label(newRect, "NodeID");
            newRect.x += width;
            node.NodeID = GUI.TextField(newRect, node.NodeID);
            newRect.y += height + 2;
            newRect.x -= width;
        }
    }
}
