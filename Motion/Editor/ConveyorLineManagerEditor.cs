using System.Collections.Generic;
using NonsensicalKit.DigitalTwin.Motion;
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Editor
{

[CustomEditor(typeof(ConveyorLineManager))]
public sealed class ConveyorLineManagerEditor : UnityEditor.Editor
{
    private readonly List<ConveyorPartMotion> _stations = new List<ConveyorPartMotion>(128);
    private bool _previewFoldout;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        bool changed = EditorGUI.EndChangeCheck();
        if (serializedObject.ApplyModifiedProperties() || changed)
            SceneView.RepaintAll();

        var manager = (ConveyorLineManager)target;
        if (manager == null) return;

        manager.CollectStations(_stations);

        EditorGUILayout.Space(8f);
        EditorGUILayout.HelpBox(
            $"当前收集 {_stations.Count} 个工位（子物体 + 额外配置）。\n" +
            "提升机请放到「额外工位」，以便同时被多层管理。",
            MessageType.Info);

        _previewFoldout = EditorGUILayout.Foldout(_previewFoldout, $"本层工位列表（{_stations.Count}）", true);
        if (_previewFoldout)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                int previewMax = Mathf.Min(_stations.Count, 24);
                for (int i = 0; i < previewMax; i++)
                {
                    EditorGUILayout.ObjectField(_stations[i], typeof(ConveyorPartMotion), true);
                }
            }

            if (_stations.Count > 24)
                EditorGUILayout.LabelField($"… 另有 {_stations.Count - 24} 个", EditorStyles.miniLabel);
        }

        EditorGUILayout.Space(6f);
        GUI.backgroundColor = new Color(0.45f, 0.78f, 1f);
        if (GUILayout.Button("打开俯视方向配置", GUILayout.Height(32f)))
            StationDirectionWindow.Open(manager);
        GUI.backgroundColor = Color.white;
    }
}
}
