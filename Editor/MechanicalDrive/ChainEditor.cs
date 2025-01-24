using System;
using NonsensicalKit.DigitalTwin.MechanicalDrive;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Editor.MechanicalDrive
{
    [CustomEditor(typeof(Chain), true)]
    public class ChainEditor : EditorBase
    {
        #region Property and Field

        protected Chain Script => target as Chain;

        #endregion

        #region Protected Method

        protected void OnEnable()
        {
            if (Script.AnchorRoot)
            {
                Script.AnchorRoot.localPosition = Vector3.zero;
                Script.AnchorRoot.localRotation = Quaternion.identity;
                if (Script.AnchorRoot.childCount >= 2)
                    Script.CreateCurve();
            }

            if (Script.NodeRoot)
            {
                Script.NodeRoot.localPosition = Vector3.zero;
                Script.NodeRoot.localRotation = Quaternion.identity;
            }
        }

        protected void OnSceneGUI()
        {
            #region Coordinate System

            var horizontal = Script.transform.right * LineLength;
            var vertical = Script.transform.up * LineLength;

            Handles.color = Blue;
            Handles.SphereHandleCap(0, Script.transform.position, Quaternion.identity, NodeSize, EventType.Repaint);
            Handles.DrawLine(Script.transform.position - horizontal, Script.transform.position + horizontal);
            Handles.DrawLine(Script.transform.position - vertical, Script.transform.position + vertical);

            #endregion

            #region Anchors And Curve

            if (Script.AnchorRoot)
            {
                foreach (Transform anchor in Script.AnchorRoot)
                {
                    Handles.SphereHandleCap(0, anchor.position, Quaternion.identity, NodeSize, EventType.Repaint);
                }

                if (Script.AnchorRoot.childCount >= 2)
                {
                    var maxTimer = Script.Curve[Script.Curve.Length - 1].Time;
                    for (float timer = 0; timer < maxTimer; timer += NodeSize)
                    {
                        Handles.DrawLine(Script.AnchorRoot.TransformPoint(Script.Curve.Evaluate(timer)),
                            Script.AnchorRoot.TransformPoint(Script.Curve.Evaluate(Mathf.Clamp(timer + NodeSize, 0, maxTimer))));
                    }
                }
            }

            #endregion

            if (AnchorEditor.IsOpen)
            {
                #region Circular Settings

                if (AnchorEditor.IsCircularSettingsReasonable)
                {
                    var from = Quaternion.AngleAxis(AnchorEditor.From, Script.transform.forward) * Vector3.up;
                    var to = Quaternion.AngleAxis(AnchorEditor.To, Script.transform.forward) * Vector3.up;
                    var angle = AnchorEditor.To - AnchorEditor.From;

                    Handles.color = Green;
                    Handles.DrawWireArc(AnchorEditor.Center.position, Script.transform.forward, from, angle, AnchorEditor.Radius);

                    DrawArrow(AnchorEditor.Center.position, from, AnchorEditor.Radius, NodeSize, string.Empty, Green);
                    DrawArrow(AnchorEditor.Center.position, to, AnchorEditor.Radius, NodeSize, string.Empty, Green);

                    if (AnchorEditor.CountC > 2)
                    {
                        var space = angle / (AnchorEditor.CountC - 1);
                        for (int i = 0; i < AnchorEditor.CountC - 2; i++)
                        {
                            var direction = Quaternion.AngleAxis(AnchorEditor.From + space * (i + 1), Script.transform.forward) * Vector3.up;
                            DrawArrow(AnchorEditor.Center.position, direction.normalized, AnchorEditor.Radius, NodeSize, string.Empty, Green);
                        }
                    }
                }

                #endregion

                #region Linear Settings

                if (AnchorEditor.IsLinearSettingsReasonable)
                {
                    var direction = (AnchorEditor.End.position - AnchorEditor.Start.position).normalized;
                    var space = Vector3.Distance(AnchorEditor.Start.position, AnchorEditor.End.position) / (AnchorEditor.CountL + 1);

                    Handles.color = Green;
                    Handles.DrawLine(AnchorEditor.Start.position, AnchorEditor.End.position);
                    for (int i = 0; i < AnchorEditor.CountL; i++)
                    {
                        Handles.SphereHandleCap(0, AnchorEditor.Start.position + direction * (space * (i + 1)), Quaternion.identity, NodeSize,
                            EventType.Repaint);
                    }
                }

                #endregion
            }
        }

        protected void EstimateCount()
        {
            var estimate = Script.Curve[^1].Time / Script.Space;
            Script.Count = (int)Math.Round(estimate, MidpointRounding.AwayFromZero);

            EditorSceneManager.MarkAllScenesDirty();
        }

        protected void DeleteNodes()
        {
            while (Script.NodeRoot.childCount > 0)
            {
                DestroyImmediate(Script.NodeRoot.GetChild(0).gameObject);
            }

            EditorSceneManager.MarkAllScenesDirty();
        }

        #endregion

        #region Public Method

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (Script.AnchorRoot == null)
                return;
            Script.AnchorRoot.localPosition = Vector3.zero;
            Script.AnchorRoot.localRotation = Quaternion.identity;
            if (GUILayout.Button("Anchor Editor"))
                AnchorEditor.ShowEditor(Script);

            if (Script.AnchorRoot.childCount < 2)
                return;

            if (Script.Curve == null)
                Script.CreateCurve();

            if (Script.NodeRoot == null || Script.NodePrefab == null)
                return;

            GUILayout.BeginHorizontal("Node Editor", "Window", GUILayout.Height(45));
            if (GUILayout.Button("Estimate"))
                EstimateCount();
            if (GUILayout.Button("Create"))
            {
                DeleteNodes();
                Script.CreateNodes();
            }

            if (GUILayout.Button("Delete"))
                DeleteNodes();
            GUILayout.EndHorizontal();
        }

        #endregion
    }
}
