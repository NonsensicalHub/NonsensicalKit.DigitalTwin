using NonsensicalKit.DigitalTwin.MechanicalDrive;
using System;
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Editor.MechanicalDrive
{
    [CustomEditor(typeof(Chain), true)]
    public class ChainEditor : EditorBase
    {
        #region Property and Field
        protected Chain script { get { return target as Chain; } }
        #endregion

        #region Protected Method
        protected void OnEnable()
        {
            if (script.AnchorRoot)
            {
                script.AnchorRoot.localPosition = Vector3.zero;
                script.AnchorRoot.localRotation = Quaternion.identity;
                if (script.AnchorRoot.childCount >= 2)
                    script.CreateCurve();
            }
            if (script.NodeRoot)
            {
                script.NodeRoot.localPosition = Vector3.zero;
                script.NodeRoot.localRotation = Quaternion.identity;
            }
        }

        protected void OnSceneGUI()
        {
            #region Coordinate System
            var horizontal = script.transform.right * lineLength;
            var vertical = script.transform.up * lineLength;

            Handles.color = blue;
            Handles.SphereHandleCap(0, script.transform.position, Quaternion.identity, nodeSize, EventType.Repaint);
            Handles.DrawLine(script.transform.position - horizontal, script.transform.position + horizontal);
            Handles.DrawLine(script.transform.position - vertical, script.transform.position + vertical);
            #endregion

            #region Anchors And Curve
            if (script.AnchorRoot)
            {
                foreach (Transform anchor in script.AnchorRoot)
                {
                    Handles.SphereHandleCap(0, anchor.position, Quaternion.identity, nodeSize, EventType.Repaint);
                }

                if (script.AnchorRoot.childCount >= 2)
                {
                    var maxTimer = script.Curve[script.Curve.Length - 1].Time;
                    for (float timer = 0; timer < maxTimer; timer += nodeSize)
                    {
                        Handles.DrawLine(script.AnchorRoot.TransformPoint(script.Curve.Evaluate(timer)),
                            script.AnchorRoot.TransformPoint(script.Curve.Evaluate(Mathf.Clamp(timer + nodeSize, 0, maxTimer))));
                    }
                }
            }
            #endregion

            if (AnchorEditor.isOpen)
            {
                #region Circular Settings
                if (AnchorEditor.isCircularSettingsReasonable)
                {
                    var from = Quaternion.AngleAxis(AnchorEditor.from, script.transform.forward) * Vector3.up;
                    var to = Quaternion.AngleAxis(AnchorEditor.to, script.transform.forward) * Vector3.up;
                    var angle = AnchorEditor.to - AnchorEditor.from;

                    Handles.color = green;
                    Handles.DrawWireArc(AnchorEditor.center.position, script.transform.forward, from, angle, AnchorEditor.radius);

                    DrawArrow(AnchorEditor.center.position, from, AnchorEditor.radius, nodeSize, string.Empty, green);
                    DrawArrow(AnchorEditor.center.position, to, AnchorEditor.radius, nodeSize, string.Empty, green);

                    if (AnchorEditor.countC > 2)
                    {
                        var space = angle / (AnchorEditor.countC - 1);
                        for (int i = 0; i < AnchorEditor.countC - 2; i++)
                        {
                            var direction = Quaternion.AngleAxis(AnchorEditor.from + space * (i + 1), script.transform.forward) * Vector3.up;
                            DrawArrow(AnchorEditor.center.position, direction.normalized, AnchorEditor.radius, nodeSize, string.Empty, green);
                        }
                    }
                }
                #endregion

                #region Linear Settings
                if (AnchorEditor.isLinearSettingsReasonable)
                {
                    var direction = (AnchorEditor.end.position - AnchorEditor.start.position).normalized;
                    var space = Vector3.Distance(AnchorEditor.start.position, AnchorEditor.end.position) / (AnchorEditor.countL + 1);

                    Handles.color = green;
                    Handles.DrawLine(AnchorEditor.start.position, AnchorEditor.end.position);
                    for (int i = 0; i < AnchorEditor.countL; i++)
                    {
                        Handles.SphereHandleCap(0, AnchorEditor.start.position + direction * space * (i + 1), Quaternion.identity, nodeSize, EventType.Repaint);
                    }
                }
                #endregion
            }
        }

        protected void EstimateCount()
        {
            var estimate = script.Curve[script.Curve.Length - 1].Time / script.Space;
            script.Count = (int)Math.Round(estimate, MidpointRounding.AwayFromZero);

            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        }

        protected void DeleteNodes()
        {
            while (script.NodeRoot.childCount > 0)
            {
                DestroyImmediate(script.NodeRoot.GetChild(0).gameObject);
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        }
        #endregion

        #region Public Method
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (script.AnchorRoot == null)
                return;
            script.AnchorRoot.localPosition = Vector3.zero;
            script.AnchorRoot.localRotation = Quaternion.identity;
            if (GUILayout.Button("Anchor Editor"))
                AnchorEditor.ShowEditor(script);

            if (script.AnchorRoot.childCount < 2)
                return;

            if (script.Curve == null)
                script.CreateCurve();

            if (script.NodeRoot == null || script.NodePrefab == null)
                return;

            GUILayout.BeginHorizontal("Node Editor", "Window", GUILayout.Height(45));
            if (GUILayout.Button("Estimate"))
                EstimateCount();
            if (GUILayout.Button("Create"))
            {
                DeleteNodes();
                script.CreateNodes();
            }
            if (GUILayout.Button("Delete"))
                DeleteNodes();
            GUILayout.EndHorizontal();
        }
        #endregion
    }
}
