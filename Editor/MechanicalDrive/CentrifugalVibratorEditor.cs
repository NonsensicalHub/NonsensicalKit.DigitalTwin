using NonsensicalKit.DigitalTwin.Motion;
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Editor.MechanicalDrive
{
    [CustomEditor(typeof(CentrifugalVibrator), true)]
    [CanEditMultipleObjects]
    public class CentrifugalVibratorEditor : EditorBase
    {
        #region Property and Field

        protected CentrifugalVibrator Script => target as CentrifugalVibrator;

        protected Vector3 StartPosition
        {
            get
            {
                if (Application.isPlaying)
                {
                    if (Script.transform.parent)
                        return Script.transform.parent.TransformPoint(Script.StartPosition);
                    else
                        return Script.StartPosition;
                }
                else
                    return Script.transform.position;
            }
        }

        #endregion

        protected void OnSceneGUI()
        {
            Handles.color = Blue;
            Handles.SphereHandleCap(0, StartPosition, Quaternion.identity, NodeSize, EventType.Repaint);
            Handles.SphereHandleCap(0, Script.transform.position, Quaternion.identity, NodeSize, EventType.Repaint);
            Handles.CircleHandleCap(0, StartPosition, Script.transform.rotation, Script.AmplitudeRadius, EventType.Repaint);

            DrawArrow(StartPosition, Script.transform.position, NodeSize, string.Empty, Blue);
            DrawArrow(StartPosition, Script.transform.forward, ArrowLength, NodeSize, "Axis", Blue);
        }
    }
}
