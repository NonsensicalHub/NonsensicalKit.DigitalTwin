using NonsensicalKit.DigitalTwin.MechanicalDrive;
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Editor.MechanicalDrive
{
    [CustomEditor(typeof(CentrifugalVibrator), true)]
    [CanEditMultipleObjects]
    public class CentrifugalVibratorEditor : EditorBase
    {
        #region Property and Field
        protected CentrifugalVibrator script { get { return target as CentrifugalVibrator; } }
        protected Vector3 startPosition
        {
            get
            {
                if (Application.isPlaying)
                {
                    if (script.transform.parent)
                        return script.transform.parent.TransformPoint(script.StartPosition);
                    else
                        return script.StartPosition;
                }
                else
                    return script.transform.position;
            }
        }
        #endregion

        protected void OnSceneGUI()
        {
            Handles.color = blue;
            Handles.SphereHandleCap(0, startPosition, Quaternion.identity, nodeSize, EventType.Repaint);
            Handles.SphereHandleCap(0, script.transform.position, Quaternion.identity, nodeSize, EventType.Repaint);
            Handles.CircleHandleCap(0, startPosition, script.transform.rotation, script.AmplitudeRadius, EventType.Repaint);

            DrawArrow(startPosition, script.transform.position, nodeSize, string.Empty, blue);
            DrawArrow(startPosition, script.transform.forward, arrowLength, nodeSize, "Axis", blue);
        }
    }
}
