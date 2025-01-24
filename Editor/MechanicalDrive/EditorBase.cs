using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Editor.MechanicalDrive
{
    public class EditorBase : UnityEditor.Editor
    {
        protected Color Blue = new Color(0, 1, 1, 1);
        protected Color Green = new Color(0, 1, 0, 1);

        protected float NodeSize = 0.05f;
        protected float ArrowLength = 0.75f;
        protected float LineLength = 10;

        protected void DrawArrow(Vector3 start, Vector3 end, float size, string text, Color color)
        {
            var gC = GUI.color;
            var hC = Handles.color;

            GUI.color = color;
            Handles.color = color;

            Handles.DrawLine(start, end);
            Handles.SphereHandleCap(0, end, Quaternion.identity, size, EventType.Repaint);
            Handles.Label(end, text);

            GUI.color = gC;
            Handles.color = hC;
        }

        protected void DrawArrow(Vector3 start, Vector3 direction, float length, float size, string text, Color color)
        {
            var end = start + direction.normalized * length;
            DrawArrow(start, end, size, text, color);
        }
    }
}
