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

        private Vector3 _startPosition
        {
            get
            {
                if (Application.isPlaying&& _script.transform.parent)
                {
                    return _script.transform.parent.TransformPoint(_script.StartPosition);
                }
                else
                {
                    return _script.transform.position;
                }
            }
        }

        private Vector3 _moveAxisInWorldSpace
        {
            get
            {
                return _script.transform.TransformVector(_script.MoveAxis).normalized;
            }
        }

        private void OnEnable()
        {
            _script= target as LinearVibrator; ;
        }

        private void OnSceneGUI()
        {
            Handles.color = blue;
            Handles.SphereHandleCap(0, _startPosition, Quaternion.identity, nodeSize, EventType.Repaint);
            Handles.SphereHandleCap(0, _script.transform.position, Quaternion.identity, nodeSize, EventType.Repaint);

            DrawArrow(_startPosition, _moveAxisInWorldSpace, -_script.AmplitudeRadius, nodeSize, string.Empty, blue);
            DrawArrow(_startPosition, _moveAxisInWorldSpace, _script.AmplitudeRadius, nodeSize, string.Empty, blue);
        }
    }
}