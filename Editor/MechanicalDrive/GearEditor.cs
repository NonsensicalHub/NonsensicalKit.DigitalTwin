using NonsensicalKit.DigitalTwin.MechanicalDrive;
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Editor.MechanicalDrive
{
    [CustomEditor(typeof(Gear), true)]
    [CanEditMultipleObjects]
    public class GearEditor : EditorBase
    {
        protected Gear script { get { return target as Gear; } }

        protected  void OnSceneGUI()
        {
            Handles.color = blue;
            Vector3 rotateAxis = script.transform.TransformVector(script.RotateAxis);
            var fuckQ = Quaternion.FromToRotation(script.transform.forward, rotateAxis);
            Handles.SphereHandleCap(0, script.transform.position, Quaternion.identity, nodeSize,EventType.Repaint);
            Handles.CircleHandleCap(0, script.transform.position, fuckQ* script.transform.rotation, script.GearRadius, EventType.Repaint);
            DrawArrow(script.transform.position, rotateAxis, arrowLength, nodeSize, "Axis", blue);
        }
    }
}