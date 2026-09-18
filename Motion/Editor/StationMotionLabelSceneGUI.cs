using NonsensicalKit.DigitalTwin.Motion;
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Editor
{
    /// <summary>
    /// 在 Scene 视图为 <see cref="ConveyorPartMotion"/> 绘制节点名称标签。
    /// 受 <see cref="ConveyorLineManager"/>「显示 Gizmos」统一控制。
    /// </summary>
    [InitializeOnLoad]
    public static class StationMotionLabelSceneGUI
    {
        private const double RefreshIntervalSeconds = 0.5d;

        private static GUIStyle s_labelStyle;
        private static ConveyorPartMotion[] s_stations = System.Array.Empty<ConveyorPartMotion>();
        private static double s_nextRefreshTime;

        static StationMotionLabelSceneGUI()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.hierarchyChanged += InvalidateCache;
        }

        private static void InvalidateCache()
        {
            s_nextRefreshTime = 0d;
        }

        private static void RefreshStationsIfNeeded()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < s_nextRefreshTime && s_stations != null && s_stations.Length > 0)
                return;

            s_stations = Object.FindObjectsByType<ConveyorPartMotion>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (s_stations == null)
                s_stations = System.Array.Empty<ConveyorPartMotion>();
            s_nextRefreshTime = now + RefreshIntervalSeconds;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            var cam = sceneView != null ? sceneView.camera : null;
            if (cam == null)
                return;

            RefreshStationsIfNeeded();
            if (s_stations.Length == 0)
                return;

            Handles.BeginGUI();
            for (int i = 0; i < s_stations.Length; i++)
            {
                var station = s_stations[i];
                if (station == null || !station.ShowLabel)
                    continue;
                if (!ConveyorLineManager.ShouldDrawGizmos(station))
                    continue;

                DrawLabel(cam, station);
            }
            Handles.EndGUI();
        }

        private static void DrawLabel(Camera cam, ConveyorPartMotion station)
        {
            Vector3 world = station.LabelWorldPosition;
            Vector3 viewport = cam.WorldToViewportPoint(world);
            if (viewport.z <= 0f)
                return;

            Vector2 guiPos = HandleUtility.WorldToGUIPoint(world);
            EnsureStyle(station.LabelColor, station.FontSize);

            GUIContent content = new GUIContent(station.gameObject.name);
            Vector2 size = s_labelStyle.CalcSize(content);
            Rect rect = new Rect(guiPos.x - size.x * 0.5f, guiPos.y - size.y, size.x + 8f, size.y + 2f);
            GUI.Label(rect, content, s_labelStyle);
        }

        private static void EnsureStyle(Color color, int fontSize)
        {
            if (s_labelStyle == null)
            {
                s_labelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(4, 4, 1, 1)
                };
            }

            s_labelStyle.fontSize = fontSize;
            s_labelStyle.normal.textColor = color;
            s_labelStyle.hover.textColor = color;
            s_labelStyle.active.textColor = color;
        }
    }
}
