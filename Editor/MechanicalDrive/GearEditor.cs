using NonsensicalKit.DigitalTwin.MechanicalDrive;
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Editor.MechanicalDrive
{
    [CustomEditor(typeof(Gear), true)]
    [CanEditMultipleObjects]
    public class GearEditor : EditorBase
    {
        protected Gear Script => target as Gear;

        protected void OnSceneGUI()
        {
            Handles.color = Blue;
            Vector3 rotateAxis = Script.transform.TransformVector(Script.RotateAxis);
            var fuckQ = Quaternion.FromToRotation(Script.transform.forward, rotateAxis);
            Handles.SphereHandleCap(0, Script.transform.position, Quaternion.identity, NodeSize, EventType.Repaint);
            Handles.CircleHandleCap(0, Script.transform.position, fuckQ * Script.transform.rotation, Script.GearRadius, EventType.Repaint);
            DrawArrow(Script.transform.position, rotateAxis, ArrowLength, NodeSize, "Axis", Blue);
        }
    }
}
