using NonsensicalKit.DigitalTwin.MechanicalDrive;
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Editor.MechanicalDrive
{
    [CustomEditor(typeof(LinearVibrator), true)]
    [CanEditMultipleObjects]
    public class LinearVibratorEditor : EditorBase
    {
        private LinearVibrator _script;

        private Vector3 StartPosition
        {
            get
            {
                if (Application.isPlaying && _script.transform.parent)
                {
                    return _script.transform.parent.TransformPoint(_script.StartPosition);
                }
                else
                {
                    return _script.transform.position;
                }
            }
        }

        private Vector3 MoveAxisInWorldSpace => _script.transform.TransformVector(_script.MoveAxis).normalized;

        private void OnEnable()
        {
            _script = target as LinearVibrator;
        }

        private void OnSceneGUI()
        {
            Handles.color = Blue;
            Handles.SphereHandleCap(0, StartPosition, Quaternion.identity, NodeSize, EventType.Repaint);
            Handles.SphereHandleCap(0, _script.transform.position, Quaternion.identity, NodeSize, EventType.Repaint);

            DrawArrow(StartPosition, MoveAxisInWorldSpace, -_script.AmplitudeRadius, NodeSize, string.Empty, Blue);
            DrawArrow(StartPosition, MoveAxisInWorldSpace, _script.AmplitudeRadius, NodeSize, string.Empty, Blue);
        }
    }
}
