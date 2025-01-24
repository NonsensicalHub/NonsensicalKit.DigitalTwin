using NonsensicalKit.DigitalTwin.MechanicalDrive;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Editor.MechanicalDrive
{
    public class AnchorEditor : EditorWindow
    {
        #region Property and Field

        protected static AnchorEditor Instance;
        public static bool IsOpen { protected set; get; }

        protected static Vector2 ScrollPos;
        protected const float LeftAlign = 150;
        protected const float Paragraph = 2.5f;

        protected static Chain TargetChain;

        public static Transform Center { protected set; get; }
        public static float Radius { protected set; get; }
        public static float From { protected set; get; }
        public static float To { protected set; get; }
        public static int CountC { protected set; get; }

        public static bool IsCircularSettingsReasonable => Center && Radius > 0 && From < To && CountC > 0;

        public static Transform Start { protected set; get; }
        public static Transform End { protected set; get; }
        public static int CountL { protected set; get; }

        public static bool IsLinearSettingsReasonable => Start && End && CountL > 0;

        protected static string Prefix = "Anchor";
        protected const string RendererName = "AnchorRenderer";
        protected static float Size = 0.05f;

        #endregion

        #region Private Method

        [MenuItem("NonsensicalKit/Anchor Editor")]
        private static void ShowEditor()
        {
            TargetChain = GetChainFromSelection();
            ShowEditorWindow();
        }

        #endregion

        #region protected Method

        protected static void ShowEditorWindow()
        {
            Instance = GetWindow<AnchorEditor>("Anchor Editor", true);
            Instance.autoRepaintOnSceneChange = true;
            Instance.Show();
            IsOpen = true;
        }

        protected static Chain GetChainFromSelection()
        {
            if (Selection.activeGameObject)
                return Selection.activeGameObject.GetComponent<Chain>();
            else
                return null;
        }

        protected void OnSelectionChange()
        {
            var chain = GetChainFromSelection();
            if (TargetChain == chain)
                return;
            TargetChain = chain;
            Repaint();
        }

        protected void OnGUI()
        {
            if (TargetChain)
            {
                if (TargetChain.AnchorRoot)
                {
                    ScrollPos = EditorGUILayout.BeginScrollView(ScrollPos);

                    #region Circular Anchor Creater

                    GUILayout.BeginVertical("Circular Anchor Creater", "Window", GUILayout.Height(140));

                    EditorGUI.BeginChangeCheck();
                    Center = (Transform)EditorGUILayout.ObjectField("Center", Center, typeof(Transform), true);
                    Radius = EditorGUILayout.FloatField("Radius", Radius);
                    From = EditorGUILayout.FloatField("From", From);
                    To = EditorGUILayout.FloatField("To", To);
                    CountC = EditorGUILayout.IntField("Count", CountC);

                    GUILayout.BeginHorizontal();
                    GUILayout.Space(LeftAlign);
                    if (GUILayout.Button("Create"))
                    {
                        if (IsCircularSettingsReasonable)
                            CreateCircularAnchors();
                        else
                            ShowNotification(new GUIContent("The parameter settings of circular anchor creater is not reasonable."));
                    }

                    if (GUILayout.Button("Reset"))
                        ResetCircularAnchorCreator();
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();

                    #endregion

                    #region Linear Anchor Creater

                    GUILayout.BeginVertical("Linear Anchor Creater", "Window", GUILayout.Height(105));

                    EditorGUI.BeginChangeCheck();
                    Start = (Transform)EditorGUILayout.ObjectField("Start", Start, typeof(Transform), true);
                    End = (Transform)EditorGUILayout.ObjectField("End", End, typeof(Transform), true);
                    CountL = EditorGUILayout.IntField("Count", CountL);

                    GUILayout.BeginHorizontal();
                    GUILayout.Space(LeftAlign);
                    if (GUILayout.Button("Create"))
                    {
                        if (IsLinearSettingsReasonable)
                            CreateLinearAnchors();
                        else
                            ShowNotification(new GUIContent("The parameter settings of linear anchor creater is not reasonable."));
                    }

                    if (GUILayout.Button("Reset"))
                        ResetLinearAnchorCreator();
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();

                    #endregion

                    #region Unified Anchor Manager

                    GUILayout.BeginVertical("Unify Anchor Manager", "Window");
                    Prefix = EditorGUILayout.TextField("Prefix", Prefix);

                    GUILayout.BeginHorizontal();
                    GUILayout.Space(LeftAlign);
                    if (GUILayout.Button("Rename"))
                    {
                        if (Prefix.Trim() == string.Empty)
                            ShowNotification(new GUIContent("The value of prefix cannot be empty."));
                        else
                            RenameAnchors();
                    }

                    GUILayout.EndHorizontal();

                    GUILayout.Space(Paragraph);
                    Size = EditorGUILayout.FloatField("Renderer", Size);

                    GUILayout.BeginHorizontal();
                    GUILayout.Space(LeftAlign);
                    if (GUILayout.Button("Attach"))
                    {
                        RemoveAnchorRenderer();
                        AttachAnchorRenderer();
                    }

                    if (GUILayout.Button("Remove"))
                        RemoveAnchorRenderer();
                    GUILayout.EndHorizontal();

                    GUILayout.Space(Paragraph);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Anchors", GUILayout.Width(LeftAlign - 4));
                    if (GUILayout.Button("Delete"))
                    {
                        var delete = EditorUtility.DisplayDialog(
                            "Delete Anchors",
                            "This operate will delete all of the chain anchors.\nAre you sure continue to do this?",
                            "Yes",
                            "Cancel");
                        if (delete)
                            DeleteAnchors();
                    }

                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();

                    #endregion

                    EditorGUILayout.EndScrollView();
                }
                else
                    EditorGUILayout.HelpBox("The anchor root of chain has not been assigned.", MessageType.Error);
            }
            else
                EditorGUILayout.HelpBox("No chain object is selected.", MessageType.Info);
        }

        protected void OnDestroy()
        {
            TargetChain = null;
            IsOpen = false;
        }

        protected void CreateCircularAnchors()
        {
            var space = (To - From) / (CountC == 1 ? 1 : CountC - 1);
            for (int i = 0; i < CountC; i++)
            {
                var direction = Quaternion.AngleAxis(From + space * i, TargetChain.AnchorRoot.forward) * Vector3.up;
                var tangent = -Vector3.Cross(direction, TargetChain.AnchorRoot.forward);
                var centerPosition = Center.position + direction * Radius;
                CreateAnchor("CircularAnchor" + " (" + i + ")", centerPosition, centerPosition + tangent, direction, Center.GetSiblingIndex());
            }

            ResetCircularAnchorCreator();
            RefreshChainCurve();
            EditorSceneManager.MarkAllScenesDirty();
        }

        protected void ResetCircularAnchorCreator()
        {
            Center = null;
            Radius = From = To = CountC = 0;
        }

        protected void CreateLinearAnchors()
        {
            var direction = (End.position - Start.position).normalized;
            var space = Vector3.Distance(Start.position, End.position) / (CountL + 1);
            for (int i = 0; i < CountL; i++)
            {
                CreateAnchor("LinearAnchor" + " (" + i + ")", Start.position + direction * (space * (i + 1)),
                    End.position, Vector3.Cross(direction, TargetChain.AnchorRoot.forward), End.GetSiblingIndex());
            }

            ResetLinearAnchorCreator();
            RefreshChainCurve();
            EditorSceneManager.MarkAllScenesDirty();
        }

        protected void ResetLinearAnchorCreator()
        {
            Start = End = null;
            CountL = 0;
        }

        protected void CreateAnchor(string anchorName, Vector3 centerPosition, Vector3 lookAtPos, Vector3 worldUp, int siblingIndex)
        {
            var newAnchor = new GameObject(anchorName).transform;
            newAnchor.position = centerPosition;
            newAnchor.LookAt(lookAtPos, worldUp);
            newAnchor.parent = TargetChain.AnchorRoot;
            newAnchor.SetSiblingIndex(siblingIndex);
            AttachRenderer(newAnchor);
        }

        protected void RefreshChainCurve()
        {
            if (TargetChain.AnchorRoot.childCount >= 2)
                TargetChain.CreateCurve();
        }

        protected void RenameAnchors()
        {
            for (int i = 0; i < TargetChain.AnchorRoot.childCount; i++)
            {
                TargetChain.AnchorRoot.GetChild(i).name = Prefix.Trim() + " (" + i + ")";
            }

            EditorSceneManager.MarkAllScenesDirty();
        }

        protected void AttachAnchorRenderer()
        {
            foreach (Transform anchor in TargetChain.AnchorRoot)
            {
                AttachRenderer(anchor);
            }

            EditorSceneManager.MarkAllScenesDirty();
        }

        protected void AttachRenderer(Transform anchor)
        {
            var renderer = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            DestroyImmediate(renderer.GetComponent<Collider>());
            renderer.name = RendererName;
            renderer.parent = anchor;
            renderer.localPosition = Vector3.zero;
            renderer.localRotation = Quaternion.identity;
            renderer.localScale = Vector3.one * Size;
        }

        protected void RemoveAnchorRenderer()
        {
            foreach (Transform anchor in TargetChain.AnchorRoot)
            {
                var renderer = anchor.Find(RendererName);
                if (renderer)
                    DestroyImmediate(renderer.gameObject);
            }

            EditorSceneManager.MarkAllScenesDirty();
        }

        protected void DeleteAnchors()
        {
            while (TargetChain.AnchorRoot.childCount > 0)
            {
                DestroyImmediate(TargetChain.AnchorRoot.GetChild(0).gameObject);
            }

            EditorSceneManager.MarkAllScenesDirty();
        }

        #endregion

        #region Public Method

        public static void ShowEditor(Chain chain)
        {
            TargetChain = chain;
            ShowEditorWindow();
        }

        #endregion
    }
}
