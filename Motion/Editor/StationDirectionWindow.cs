using System.Collections.Generic;
using NonsensicalKit.DigitalTwin.Motion;
using NonsensicalKit.DigitalTwin.Render;
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Editor
{

/// <summary>
/// 俯视（XZ）可视化配置输送工位正转轴向与上下游拓扑（含顶升移栽、提升机）。
/// 按 <see cref="ConveyorLineManager"/> 分层打开，避免三层工位叠在同一张图上。
/// </summary>
public sealed class StationDirectionWindow : EditorWindow
{
    private const float ConfigPanelWidth = 300f;
    private const float BaseNodeRadius = 9f;
    private const float CompassHitRadius = 18f;
    private const float DragThreshold = 4f;
    private const float ConnectSnapPadding = 22f;
    private const float EdgeHitWidth = 10f;
    private const float MinZoom = 0.05f;
    private const float MaxZoom = 400f;
    private const float FitPadding = 64f;
    private const float TargetNeighborPixels = 56f;
    private const float DirHandlePixels = 42f;
    private const float CompassHandlePixels = 64f;
    private const float FocusRadiusMeters = 28f;
    private const float SmartPairMaxDistance = 1.75f;
    private const float SemiTransparentDither = 0.35f;
    private const string PrefsViewYaw = "StationDirectionWindow.ViewYaw";
    private const string PrefsSmartOverwrite = "StationDirectionWindow.SmartOverwrite";
    private const string PrefsSmartPairJack = "StationDirectionWindow.SmartPairJack";
    private const string PrefsDisplayConveyorPart = "StationDirectionWindow.Display.ConveyorPart";
    private const string PrefsDisplayJack = "StationDirectionWindow.Display.Jack";
    private const string PrefsDisplayOther = "StationDirectionWindow.Display.Other";

    private static readonly int CanvasControlHint = "StationDirectionCanvas".GetHashCode();
    private static readonly Color CanvasBg = new Color(0.15f, 0.16f, 0.17f, 1f);
    private static readonly Color GridColor = new Color(1f, 1f, 1f, 0.045f);
    private static readonly Color PartFill = new Color(0.16f, 0.42f, 0.52f, 1f);
    private static readonly Color JackFill = new Color(0.55f, 0.38f, 0.16f, 1f);
    private static readonly Color LifterFill = new Color(0.62f, 0.52f, 0.18f, 1f);
    private static readonly Color OtherFill = new Color(0.32f, 0.32f, 0.34f, 1f);
    private static readonly Color SelectedFill = new Color(0.58f, 0.44f, 0.14f, 1f);
    private static readonly Color ForwardColor = new Color(0.25f, 0.92f, 0.48f, 0.95f);
    private static readonly Color ReverseColor = new Color(1f, 0.48f, 0.25f, 0.95f);
    private static readonly Color PairedColor = new Color(0.85f, 0.75f, 0.2f, 0.9f);
    private static readonly Color AxisOverlay = new Color(0.78f, 0.82f, 0.86f, 0.9f);
    private static readonly Color BoxSelectFill = new Color(0.35f, 0.65f, 1f, 0.12f);
    private static readonly Color BoxSelectBorder = new Color(0.45f, 0.78f, 1f, 0.95f);
    private static readonly Color MultiSelectedEdge = new Color(1f, 0.92f, 0.25f, 1f);
    private static readonly string[] DisplayStateLabels = { "正常显示", "半透明", "隐藏" };

    private enum ConnectMode
    {
        ForwardNext = 0,
        ReverseNext = 1,
        PairedStation = 2
    }

    private enum DragMode
    {
        None,
        Pan,
        Connect,
        BoxSelect
    }

    private enum TopologyEdgeKind
    {
        Neighbor = 0,
        Paired = 1
    }

    /// <summary>按设备类型控制场景/俯视图显示。</summary>
    private enum DeviceDisplayState
    {
        Normal = 0,
        SemiTransparent = 1,
        Hidden = 2
    }

    private struct TopologyEdge : System.IEquatable<TopologyEdge>
    {
        public ConveyorPartMotion A;
        public ConveyorPartMotion B;
        public TopologyEdgeKind Kind;
        /// <summary>邻居边中正转侧（A.ForwardNext==B 时为 A）。</summary>
        public ConveyorPartMotion ForwardSide;

        public bool IsValid => A != null && B != null && A != B;

        public long Key
        {
            get
            {
                if (!IsValid) return 0;
                int idA = A.GetInstanceID();
                int idB = B.GetInstanceID();
                if (idA > idB)
                {
                    int tmp = idA;
                    idA = idB;
                    idB = tmp;
                }

                return ((long)idA << 32) ^ (uint)idB ^ ((long)Kind << 60);
            }
        }

        public bool Equals(TopologyEdge other) => Key == other.Key && IsValid && other.IsValid;
        public override bool Equals(object obj) => obj is TopologyEdge other && Equals(other);
        public override int GetHashCode() => Key.GetHashCode();
    }

    private readonly List<ConveyorPartMotion> _stations = new List<ConveyorPartMotion>(128);
    private readonly List<TopologyEdge> _edges = new List<TopologyEdge>(128);
    private readonly List<TopologyEdge> _selectedEdges = new List<TopologyEdge>(32);
    private readonly HashSet<long> _selectedEdgeKeys = new HashSet<long>();
    private Vector2 _configScroll;
    private Vector2 _pan;
    private float _zoom = 18f;
    private float _viewYawDegrees;
    private bool _fitPending = true;
    private bool _showLabels = true;
    private bool _showTopology = true;
    private bool _showEdgeFlow = true;
    private bool _onlySsxTypes = true;
    private bool _smartOverwrite;
    private bool _smartPairJack = true;
    private string _lastSmartResult = string.Empty;
    private ConnectMode _connectMode = ConnectMode.ForwardNext;
    private DeviceDisplayState _displaySsxPart = DeviceDisplayState.Normal;
    private DeviceDisplayState _displayJack = DeviceDisplayState.Normal;
    private DeviceDisplayState _displayOther = DeviceDisplayState.Normal;
    private bool _displayDirty = true;

    private Rect _canvasRect;
    private ConveyorPartMotion _selected;
    private ConveyorPartMotion _hover;
    private ConveyorPartMotion _connectFrom;
    private TopologyEdge _selectedEdge;
    private TopologyEdge _hoverEdge;
    private bool _hasSelectedEdge;
    private bool _hasHoverEdge;
    private bool _boxSelectAdditive;
    private ConnectMode _activeConnectMode = ConnectMode.ForwardNext;
    private SerializedObject _selectedSO;
    private GUIStyle _labelStyle;
    private GUIStyle _axisStyle;

    private DragMode _dragMode;
    private Vector2 _dragStartLocal;
    private Vector2 _dragCurrentLocal;
    private bool _dragMoved;
    private ConveyorLineManager _manager;

    public static void Open(ConveyorLineManager manager, ConveyorPartMotion select = null)
    {
        if (manager == null)
        {
            EditorUtility.DisplayDialog(
                "俯视方向配置",
                "请先在对应层的根物体上添加 ConveyorLineManager，再打开俯视配置。",
                "确定");
            return;
        }

        var window = GetWindow<StationDirectionWindow>("俯视方向配置");
        window.minSize = new Vector2(880f, 420f);
        window.SetManager(manager);
        window.Show();
        window.Focus();
        if (select != null)
            window.SelectStation(select, syncEditorSelection: true);
    }

    [MenuItem("Tools/DigitalTwin/俯视方向配置", false, 60)]
    public static void OpenFromMenu()
    {
        var manager = ResolveManagerFromSelection();
        if (manager == null)
        {
            EditorUtility.DisplayDialog(
                "俯视方向配置",
                "请先选中一层的 ConveyorLineManager（或该层下的输送工位），再打开俯视配置。\n每层单独配置，提升机放到该层的「额外工位」。",
                "确定");
            return;
        }

        var station = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<ConveyorPartMotion>()
            : null;
        Open(manager, station);
    }

    [MenuItem("CONTEXT/ConveyorLineManager/打开俯视方向配置")]
    private static void OpenFromManagerContext(MenuCommand command)
    {
        Open(command.context as ConveyorLineManager);
    }

    [MenuItem("CONTEXT/ConveyorPartMotion/打开俯视方向配置")]
    private static void OpenFromStationContext(MenuCommand command)
    {
        var station = command.context as ConveyorPartMotion;
        var manager = ConveyorLineManager.FindOwner(station);
        if (manager == null)
        {
            EditorUtility.DisplayDialog(
                "俯视方向配置",
                "该工位不在任何输送线管理下。请先在对应层的根物体上添加 ConveyorLineManager。",
                "确定");
            return;
        }

        Open(manager, station);
    }

    private static ConveyorLineManager ResolveManagerFromSelection()
    {
        var go = Selection.activeGameObject;
        if (go == null) return null;
        return ConveyorLineManager.FindOwner(go);
    }

    private void OnEnable()
    {
        wantsMouseMove = true;
        titleContent = new GUIContent("俯视方向配置");
        Selection.selectionChanged += OnEditorSelectionChanged;
        Undo.undoRedoPerformed += OnUndoRedo;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        _viewYawDegrees = EditorPrefs.GetFloat(PrefsViewYaw, 0f);
        _smartOverwrite = EditorPrefs.GetBool(PrefsSmartOverwrite, false);
        _smartPairJack = EditorPrefs.GetBool(PrefsSmartPairJack, true);
        _displaySsxPart = LoadDisplayState(PrefsDisplayConveyorPart);
        _displayJack = LoadDisplayState(PrefsDisplayJack);
        _displayOther = LoadDisplayState(PrefsDisplayOther);
        _displayDirty = true;
        SyncFromEditorSelection();
        _fitPending = true;
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= OnEditorSelectionChanged;
        Undo.undoRedoPerformed -= OnUndoRedo;
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
    }

    private void OnFocus() => Repaint();

    private int CurrentFloor => _manager != null ? _manager.Floor : 1;

    private void SetManager(ConveyorLineManager manager)
    {
        _manager = manager;
        titleContent = new GUIContent(
            manager != null ? $"俯视方向配置 · {manager.LayerName}" : "俯视方向配置");
        _fitPending = true;
        _displayDirty = true;
        ClearEdgeSelection();
        Repaint();
    }

    private ConveyorPartMotion GetForwardNext(ConveyorPartMotion station) =>
        station != null ? station.EditorGetForwardNext(CurrentFloor) : null;

    private ConveyorPartMotion GetReverseNext(ConveyorPartMotion station) =>
        station != null ? station.EditorGetReverseNext(CurrentFloor) : null;

    private void OnEditorSelectionChanged()
    {
        SyncFromEditorSelection();
        Repaint();
    }

    private void OnUndoRedo()
    {
        RefreshSelectedSerializedObject();
        Repaint();
    }

    private void OnHierarchyChanged()
    {
        _fitPending = _stations.Count == 0;
        _displayDirty = true;
        Repaint();
    }

    private void OnGUI()
    {
        if (_manager == null)
        {
            DrawMissingManager();
            return;
        }

        if (_displayDirty)
        {
            ApplyAllDeviceDisplayStates();
            _displayDirty = false;
        }

        DrawToolbar();
        EditorGUILayout.BeginHorizontal();
        DrawConfigPanel();
        EditorGUILayout.BeginVertical();
        DrawHint();
        DrawCanvas();
        DrawStatusBar();
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawMissingManager()
    {
        EditorGUILayout.Space(12f);
        EditorGUILayout.HelpBox(
            "俯视方向配置按层打开。请指定一层的 ConveyorLineManager。\n每层默认收集子物体上的输送工位，提升机放到该层的「额外工位」。",
            MessageType.Warning);
        EditorGUI.BeginChangeCheck();
        var next = (ConveyorLineManager)EditorGUILayout.ObjectField(
            "输送线管理", _manager, typeof(ConveyorLineManager), true);
        if (EditorGUI.EndChangeCheck())
            SetManager(next);
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        EditorGUI.BeginChangeCheck();
        var nextManager = (ConveyorLineManager)EditorGUILayout.ObjectField(
            _manager, typeof(ConveyorLineManager), true, GUILayout.MinWidth(140f), GUILayout.MaxWidth(220f));
        if (EditorGUI.EndChangeCheck())
            SetManager(nextManager);

        GUILayout.Label($"L{CurrentFloor}", EditorStyles.toolbarButton, GUILayout.Width(36f));

        if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(44f)))
            Repaint();

        if (GUILayout.Button("适应全部", EditorStyles.toolbarButton, GUILayout.Width(68f)))
        {
            FitView(focusSelection: false);
            Repaint();
        }

        if (GUILayout.Button("聚焦选中", EditorStyles.toolbarButton, GUILayout.Width(68f)))
        {
            FitView(focusSelection: true);
            Repaint();
        }

        GUILayout.Space(8f);
        if (GUILayout.Button("↺90°", EditorStyles.toolbarButton, GUILayout.Width(44f)))
            RotateView(-90f);
        if (GUILayout.Button("↻90°", EditorStyles.toolbarButton, GUILayout.Width(44f)))
            RotateView(90f);

        EditorGUI.BeginChangeCheck();
        float yaw = EditorGUILayout.FloatField(_viewYawDegrees, GUILayout.Width(48f));
        if (EditorGUI.EndChangeCheck())
            SetViewYaw(yaw, keepCenter: true);

        DrawYawPreset(0f, "0°", 32f);
        DrawYawPreset(90f, "90°", 36f);
        DrawYawPreset(180f, "180°", 40f);
        DrawYawPreset(270f, "270°", 40f);

        GUILayout.Space(8f);
        _showLabels = GUILayout.Toggle(_showLabels, "名称", EditorStyles.toolbarButton, GUILayout.Width(44f));
        _showTopology = GUILayout.Toggle(_showTopology, "拓扑", EditorStyles.toolbarButton, GUILayout.Width(44f));
        _showEdgeFlow = GUILayout.Toggle(_showEdgeFlow, "流向", EditorStyles.toolbarButton, GUILayout.Width(44f));
        _onlySsxTypes = GUILayout.Toggle(_onlySsxTypes, "仅皮带", EditorStyles.toolbarButton, GUILayout.Width(56f));

        GUILayout.Space(8f);
        DrawConnectModeToggle(ConnectMode.ForwardNext, "拖:正转", 64f);
        DrawConnectModeToggle(ConnectMode.ReverseNext, "Shift反转", 72f);
        DrawConnectModeToggle(ConnectMode.PairedStation, "Ctrl配对", 68f);

        GUILayout.Space(8f);
        if (GUILayout.Button("智能连线", EditorStyles.toolbarButton, GUILayout.Width(72f)))
            RunSmartConnect();

        using (new EditorGUI.DisabledScope(!_hasSelectedEdge))
        {
            if (GUILayout.Button("删除连线", EditorStyles.toolbarButton, GUILayout.Width(68f)))
                DeleteSelectedEdges();
            if (GUILayout.Button("反转方向", EditorStyles.toolbarButton, GUILayout.Width(68f)))
                ReverseSelectedNeighborEdges();
        }

        GUILayout.FlexibleSpace();

        using (new EditorGUI.DisabledScope(_selected == null))
        {
            if (GUILayout.Button("自动连前后", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            {
                _selected.EditorTryAutoBindForward(true, _stations, CurrentFloor);
                _selected.EditorTryAutoBindReverse(true, _stations, CurrentFloor);
                RefreshSelectedSerializedObject();
            }

            if (GUILayout.Button("聚焦场景", EditorStyles.toolbarButton, GUILayout.Width(68f)))
                FrameSelectedInScene();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawYawPreset(float degrees, string label, float width)
    {
        bool on = Mathf.Abs(Mathf.DeltaAngle(_viewYawDegrees, degrees)) < 0.1f;
        bool next = GUILayout.Toggle(on, label, EditorStyles.toolbarButton, GUILayout.Width(width));
        if (next && !on)
            SetViewYaw(degrees, keepCenter: true);
    }

    private void DrawConnectModeToggle(ConnectMode mode, string label, float width)
    {
        bool on = _connectMode == mode;
        bool next = GUILayout.Toggle(on, label, EditorStyles.toolbarButton, GUILayout.Width(width));
        if (next && !on)
            _connectMode = mode;
    }

    private void DrawHint()
    {
        EditorGUILayout.LabelField(
            $"当前层 {_manager.LayerName}（PLC {CurrentFloor}） · 拖工位连线 · 空白处框选连线 · Shift加选 · 点选后可反转/删除 · Shift反转连 · Ctrl配对 · 中键平移",
            EditorStyles.miniLabel);
    }

    private void DrawStatusBar()
    {
        string sel = _selected != null ? $" · 选中 {_selected.name}" : string.Empty;
        string hover = _hover != null ? $" · 悬停 {_hover.name}" : string.Empty;
        string edge = _selectedEdges.Count > 1
            ? $" · 选中 {_selectedEdges.Count} 条连线"
            : _hasSelectedEdge && _selectedEdge.IsValid
                ? $" · 选中连线 {DescribeEdgeDirection(_selectedEdge)}"
                : string.Empty;
        string connect = _connectFrom != null && _dragMode == DragMode.Connect && _dragMoved
            ? $" · 连线 {_connectFrom.name}→{GetConnectModeLabel(_activeConnectMode)}"
            : string.Empty;
        string box = _dragMode == DragMode.BoxSelect && _dragMoved
            ? " · 框选中…"
            : string.Empty;
        string smart = string.IsNullOrEmpty(_lastSmartResult) ? string.Empty : $" · {_lastSmartResult}";
        EditorGUILayout.LabelField(
            $"{(_manager != null ? _manager.LayerName : "未指定层")} · 楼层 {CurrentFloor} · 工位 {_stations.Count} · 旋转 {_viewYawDegrees:0}° · 缩放 {_zoom:0.0} px/m · {_connectMode}{sel}{hover}{connect}{edge}{box}{smart}",
            EditorStyles.miniLabel);
    }

    private void DrawConfigPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(ConfigPanelWidth));
        _configScroll = EditorGUILayout.BeginScrollView(_configScroll);

        EditorGUILayout.LabelField("选中对象", EditorStyles.boldLabel);
        if (_selectedEdges.Count > 0)
        {
            DrawEdgeSelectionPanel();
        }
        else if (_selected == null)
        {
            EditorGUILayout.HelpBox(
                "拖拽连线：从工位按住拖到另一工位。\n空白处拖拽框选多条连线，可批量反转方向或删除。\nShift 点选/框选 = 加选。\nShift=反转连 · Ctrl=配对。",
                MessageType.Info);
        }
        else
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField("对象", _selected, typeof(ConveyorPartMotion), true);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.LabelField("类型", GetStationTypeLabel(_selected));

            RefreshSelectedSerializedObject();
            if (_selectedSO != null)
            {
                _selectedSO.Update();
                EditorGUILayout.PropertyField(_selectedSO.FindProperty("m_forwardAxis"), new GUIContent("正转轴向"));
                EditorGUILayout.PropertyField(
                    _selectedSO.FindProperty("m_useForwardScaleAsLength"),
                    new GUIContent("轴向缩放作长度", "用正转轴向对应的 localScale 作为有效长度"));
                using (new EditorGUI.DisabledScope(_selected.UseForwardScaleAsLength))
                {
                    EditorGUILayout.PropertyField(
                        _selectedSO.FindProperty("m_length"),
                        new GUIContent("长度(m)"));
                }

                EditorGUILayout.LabelField("当前有效长度", $"{_selected.Length:F3} m");
                if (GUILayout.Button("用当前轴向缩放写入长度"))
                    _selected.EditorApplyForwardScaleToLength();

                if (_selected is LifterPartMotion)
                {
                    EditorGUILayout.HelpBox($"提升机连线写入逻辑楼层 {CurrentFloor}（一/二/三楼）的皮带拓扑；运行时 CurrentFloor 点位是高度值 0/50/100。", MessageType.None);
                    DrawLifterFloorNeighborFields((LifterPartMotion)_selected);
                }
                else
                {
                    EditorGUILayout.PropertyField(_selectedSO.FindProperty("m_forwardNext"), new GUIContent("正转下游"));
                    EditorGUILayout.PropertyField(_selectedSO.FindProperty("m_reverseNext"), new GUIContent("反转下游"));
                }

                if (_selected is JackTransferPartMotion)
                    EditorGUILayout.PropertyField(_selectedSO.FindProperty("m_pairedStation"), new GUIContent("配对输送线"));
                _selectedSO.ApplyModifiedProperties();
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("快速对齐世界方向（俯视）", EditorStyles.boldLabel);
            DrawWorldAlignButtons();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("自动连接", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("正转下游"))
            {
                _selected.EditorTryAutoBindForward(true, _stations, CurrentFloor);
                RefreshSelectedSerializedObject();
            }

            if (GUILayout.Button("反转下游"))
            {
                _selected.EditorTryAutoBindReverse(true, _stations, CurrentFloor);
                RefreshSelectedSerializedObject();
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("前后工位"))
            {
                _selected.EditorTryAutoBindForward(true, _stations, CurrentFloor);
                _selected.EditorTryAutoBindReverse(true, _stations, CurrentFloor);
                RefreshSelectedSerializedObject();
            }

            if (_selected is JackTransferPartMotion)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox("顶升移栽可用「连配对线」：升起抢货、降下交还的配对输送线。", MessageType.None);
            }
        }

        EditorGUILayout.Space(10f);
        DrawSmartConnectSection();

        EditorGUILayout.Space(10f);
        DrawDeviceDisplaySection();

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("图例", EditorStyles.boldLabel);
        DrawLegendRow(PartFill, "ConveyorPartMotion");
        DrawLegendRow(JackFill, "JackTransferPartMotion");
        DrawLegendRow(LifterFill, "LifterPartMotion");
        DrawLegendRow(ForwardColor, "正转方向 / 正转下游（上游→下游）");
        DrawLegendRow(ReverseColor, "反转回程（与正转同一条连线）");
        DrawLegendRow(PairedColor, "配对输送线（曲线）");
        DrawLegendRow(MultiSelectedEdge, "选中的连线");

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawEdgeSelectionPanel()
    {
        int neighborCount = 0;
        int pairedCount = 0;
        for (int i = 0; i < _selectedEdges.Count; i++)
        {
            if (!_selectedEdges[i].IsValid) continue;
            if (_selectedEdges[i].Kind == TopologyEdgeKind.Paired) pairedCount++;
            else neighborCount++;
        }

        if (_selectedEdges.Count == 1 && _selectedEdge.IsValid)
        {
            var edge = _selectedEdge;
            string kind = edge.Kind == TopologyEdgeKind.Paired ? "配对连线" : "前后工位连线";
            EditorGUILayout.HelpBox(
                $"{kind}\n{DescribeEdgeDirection(edge)}\n箭头表示正转上下游；删除会同时清除两端引用。",
                MessageType.Warning);

            if (edge.Kind == TopologyEdgeKind.Neighbor)
            {
                var forward = ResolveForwardSide(edge);
                var back = ResolveBackSide(edge, forward);
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField("正转上游", forward, typeof(ConveyorPartMotion), true);
                EditorGUILayout.ObjectField("正转下游", back, typeof(ConveyorPartMotion), true);
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField("端点 A", edge.A, typeof(ConveyorPartMotion), true);
                EditorGUILayout.ObjectField("端点 B", edge.B, typeof(ConveyorPartMotion), true);
                EditorGUI.EndDisabledGroup();
            }
        }
        else
        {
            EditorGUILayout.HelpBox(
                $"已框选/多选 {_selectedEdges.Count} 条连线\n前后工位 {neighborCount} · 配对 {pairedCount}\n可批量反转正转方向或删除。",
                MessageType.Info);

            const int previewMax = 8;
            for (int i = 0; i < _selectedEdges.Count && i < previewMax; i++)
            {
                if (!_selectedEdges[i].IsValid) continue;
                EditorGUILayout.LabelField(
                    $"· {DescribeEdgeDirection(_selectedEdges[i])}",
                    EditorStyles.miniLabel);
            }

            if (_selectedEdges.Count > previewMax)
                EditorGUILayout.LabelField($"… 另有 {_selectedEdges.Count - previewMax} 条", EditorStyles.miniLabel);
        }

        EditorGUILayout.Space(4f);
        using (new EditorGUI.DisabledScope(neighborCount == 0))
        {
            if (GUILayout.Button($"反转正转方向（{neighborCount}）", GUILayout.Height(28f)))
                ReverseSelectedNeighborEdges();
        }

        if (GUILayout.Button($"删除选中连线（{_selectedEdges.Count}）", GUILayout.Height(28f)))
            DeleteSelectedEdges();

        if (GUILayout.Button("清除连线选择"))
            ClearEdgeSelection();
    }

    private void DrawSmartConnectSection()
    {
        EditorGUILayout.LabelField("智能连线", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "按正转轴向自动补全上下游；可选给顶升移栽匹配最近输送线。默认只填空位，方便随后在图上拖拽手改。",
            MessageType.None);

        EditorGUI.BeginChangeCheck();
        _smartOverwrite = EditorGUILayout.ToggleLeft("覆盖已有连接", _smartOverwrite);
        _smartPairJack = EditorGUILayout.ToggleLeft("自动配对顶升移栽", _smartPairJack);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetBool(PrefsSmartOverwrite, _smartOverwrite);
            EditorPrefs.SetBool(PrefsSmartPairJack, _smartPairJack);
        }

        if (GUILayout.Button("对当前列表执行智能连线", GUILayout.Height(28f)))
            RunSmartConnect();

        if (!string.IsNullOrEmpty(_lastSmartResult))
            EditorGUILayout.HelpBox(_lastSmartResult, MessageType.Info);
    }

    private void DrawDeviceDisplaySection()
    {
        EditorGUILayout.LabelField("设备显示", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "按设备类型控制场景模型与俯视节点的显示。",
            MessageType.None);

        EditorGUI.BeginChangeCheck();
        _displaySsxPart = DrawDisplayStatePopup("输送 ConveyorPartMotion", _displaySsxPart);
        _displayJack = DrawDisplayStatePopup("顶升移栽 JackTransfer", _displayJack);
        _displayOther = DrawDisplayStatePopup("提升机/其他", _displayOther);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetInt(PrefsDisplayConveyorPart, (int)_displaySsxPart);
            EditorPrefs.SetInt(PrefsDisplayJack, (int)_displayJack);
            EditorPrefs.SetInt(PrefsDisplayOther, (int)_displayOther);
            ApplyAllDeviceDisplayStates();
            SceneView.RepaintAll();
            Repaint();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("全部正常显示"))
        {
            _displaySsxPart = DeviceDisplayState.Normal;
            _displayJack = DeviceDisplayState.Normal;
            _displayOther = DeviceDisplayState.Normal;
            EditorPrefs.SetInt(PrefsDisplayConveyorPart, (int)_displaySsxPart);
            EditorPrefs.SetInt(PrefsDisplayJack, (int)_displayJack);
            EditorPrefs.SetInt(PrefsDisplayOther, (int)_displayOther);
            ApplyAllDeviceDisplayStates();
            SceneView.RepaintAll();
            Repaint();
        }

        if (GUILayout.Button("重新应用"))
        {
            ApplyAllDeviceDisplayStates();
            SceneView.RepaintAll();
        }

        EditorGUILayout.EndHorizontal();
    }

    private static DeviceDisplayState DrawDisplayStatePopup(string label, DeviceDisplayState state)
    {
        int next = EditorGUILayout.Popup(label, (int)state, DisplayStateLabels);
        if (next < 0 || next >= DisplayStateLabels.Length)
            return state;
        return (DeviceDisplayState)next;
    }

    private static DeviceDisplayState LoadDisplayState(string prefsKey)
    {
        int value = EditorPrefs.GetInt(prefsKey, (int)DeviceDisplayState.Normal);
        if (value < 0 || value > (int)DeviceDisplayState.Hidden)
            return DeviceDisplayState.Normal;
        return (DeviceDisplayState)value;
    }

    private DeviceDisplayState GetDisplayState(ConveyorPartMotion station)
    {
        if (station is JackTransferPartMotion) return _displayJack;
        if (station is LifterPartMotion) return _displayOther;
        if (station is ConveyorPartMotion) return _displaySsxPart;
        return _displayOther;
    }

    private bool IsStationVisibleInCanvas(ConveyorPartMotion station) =>
        station != null && GetDisplayState(station) != DeviceDisplayState.Hidden;

    private float GetCanvasDisplayAlpha(ConveyorPartMotion station)
    {
        switch (GetDisplayState(station))
        {
            case DeviceDisplayState.SemiTransparent:
                return SemiTransparentDither;
            case DeviceDisplayState.Hidden:
                return 0f;
            default:
                return 1f;
        }
    }

    private void ApplyAllDeviceDisplayStates()
    {
        RebuildStations();
        for (int i = 0; i < _stations.Count; i++)
        {
            var station = _stations[i];
            if (station == null) continue;
            ApplyStationDisplayState(station, GetDisplayState(station));
        }
    }

    private static void ApplyStationDisplayState(ConveyorPartMotion station, DeviceDisplayState state)
    {
        if (station == null) return;

        float dither = state switch
        {
            DeviceDisplayState.SemiTransparent => SemiTransparentDither,
            DeviceDisplayState.Hidden => 0f,
            _ => 1f
        };

        var multiRenders = station.GetComponentsInChildren<MultiRender>(true);
        bool hasMultiRender = multiRenders != null && multiRenders.Length > 0;
        if (hasMultiRender)
        {
            for (int i = 0; i < multiRenders.Length; i++)
            {
                if (multiRenders[i] != null)
                    multiRenders[i].SetGlobalDitherVisibility(dither);
            }
        }

        // 无 MultiRender 时用 Scene 可见性做隐藏；有 MultiRender 时保持 Show，避免与 dither 冲突。
        bool hideInScene = state == DeviceDisplayState.Hidden && !hasMultiRender;
        if (hideInScene)
            SceneVisibilityManager.instance.Hide(station.gameObject, false);
        else
            SceneVisibilityManager.instance.Show(station.gameObject, false);
    }

    private void DrawWorldAlignButtons()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+X"))
            AlignSelectedToWorld(Vector3.right);
        if (GUILayout.Button("-X"))
            AlignSelectedToWorld(Vector3.left);
        if (GUILayout.Button("+Z"))
            AlignSelectedToWorld(Vector3.forward);
        if (GUILayout.Button("-Z"))
            AlignSelectedToWorld(Vector3.back);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawLifterFloorNeighborFields(LifterPartMotion lifter)
    {
        if (lifter == null) return;

        int floor = CurrentFloor;
        EditorGUI.BeginChangeCheck();
        var forward = (ConveyorPartMotion)EditorGUILayout.ObjectField(
            $"正转下游（L{floor}）",
            lifter.EditorGetForwardNext(floor),
            typeof(ConveyorPartMotion),
            true);
        var reverse = (ConveyorPartMotion)EditorGUILayout.ObjectField(
            $"反转下游（L{floor}）",
            lifter.EditorGetReverseNext(floor),
            typeof(ConveyorPartMotion),
            true);
        if (!EditorGUI.EndChangeCheck()) return;

        var currentFwd = lifter.EditorGetForwardNext(floor);
        var currentRev = lifter.EditorGetReverseNext(floor);
        if (forward != currentFwd)
            lifter.EditorSetNeighbor(forward, forward: true, writeOpposite: true, floor);
        if (reverse != currentRev)
            lifter.EditorSetNeighbor(reverse, forward: false, writeOpposite: true, floor);
        RefreshSelectedSerializedObject();
        Repaint();
    }

    private void DrawLegendRow(Color color, string label)
    {
        EditorGUILayout.BeginHorizontal();
        var rect = GUILayoutUtility.GetRect(14f, 14f, GUILayout.Width(18f));
        EditorGUI.DrawRect(rect, color);
        EditorGUILayout.LabelField(label);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawCanvas()
    {
        Rect rect = GUILayoutUtility.GetRect(
            GUIContent.none, GUIStyle.none,
            GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        if (rect.width < 8f || rect.height < 8f)
            return;

        _canvasRect = rect;
        RebuildStations();

        if (_fitPending && Event.current.type == EventType.Repaint)
        {
            FitView(focusSelection: _selected != null);
            _fitPending = false;
        }

        HandleCanvasInput();

        if (Event.current.type == EventType.Repaint)
            PaintCanvas();
    }

    private void RebuildStations()
    {
        _stations.Clear();
        if (_manager == null) return;

        _manager.CollectStations(_stations);
        for (int i = _stations.Count - 1; i >= 0; i--)
        {
            var station = _stations[i];
            if (station == null)
            {
                _stations.RemoveAt(i);
                continue;
            }

            if (_onlySsxTypes && !IsListedStationType(station))
                _stations.RemoveAt(i);
        }

        if (_selected != null && !_stations.Contains(_selected))
            SelectStation(null, syncEditorSelection: false);
    }

    private void HandleCanvasInput()
    {
        Event e = Event.current;
        int controlId = GUIUtility.GetControlID(CanvasControlHint, FocusType.Keyboard, _canvasRect);
        Vector2 local = e.mousePosition - _canvasRect.position;
        bool inside = _canvasRect.Contains(e.mousePosition);

            if (e.type == EventType.MouseMove && inside)
            {
                _hover = HitTestStation(local);
                _hasHoverEdge = false;
                _hoverEdge = default;
                if (_hover == null && _showTopology)
                    _hasHoverEdge = TryHitTestEdge(local, out _hoverEdge);
                Repaint();
                return;
            }

        if (e.type == EventType.ScrollWheel && inside)
        {
            ZoomAt(local, e.delta.y);
            e.Use();
            Repaint();
            return;
        }

        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Escape)
            {
                _connectFrom = null;
                ClearEdgeSelection();
                e.Use();
                Repaint();
                return;
            }

            if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            {
                if (_hasSelectedEdge)
                {
                    DeleteSelectedEdges();
                    e.Use();
                    return;
                }
            }

            if (e.keyCode == KeyCode.Alpha1 || e.keyCode == KeyCode.Keypad1)
            {
                _connectMode = ConnectMode.ForwardNext;
                e.Use();
                Repaint();
                return;
            }

            if (e.keyCode == KeyCode.Alpha2 || e.keyCode == KeyCode.Keypad2)
            {
                _connectMode = ConnectMode.ReverseNext;
                e.Use();
                Repaint();
                return;
            }

            if (e.keyCode == KeyCode.Alpha3 || e.keyCode == KeyCode.Keypad3)
            {
                _connectMode = ConnectMode.PairedStation;
                e.Use();
                Repaint();
                return;
            }

            if (e.keyCode == KeyCode.F)
            {
                FitView(focusSelection: _selected != null);
                e.Use();
                Repaint();
                return;
            }

            if (e.keyCode == KeyCode.R)
            {
                RotateView(e.shift ? -90f : 90f);
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.Q)
            {
                RotateView(-90f);
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.E)
            {
                RotateView(90f);
                e.Use();
                return;
            }
        }

        if (e.type == EventType.MouseDown && inside)
        {
            Focus();
            GUIUtility.hotControl = controlId;
            _dragStartLocal = local;
            _dragCurrentLocal = local;
            _dragMoved = false;
            _hover = HitTestStation(local);

            bool panButton = e.button == 2 || (e.button == 0 && e.alt) || (e.button == 1 && e.alt);
            if (panButton)
            {
                _dragMode = DragMode.Pan;
                e.Use();
                return;
            }

            if (e.button == 1)
            {
                _dragMode = DragMode.None;
                e.Use();
                return;
            }

            if (e.button == 0)
            {
                _activeConnectMode = ResolveConnectMode(e);
                bool additive = e.shift;

                // 优先点工位拖连线；选中时罗盘改方向优先于连线，避免拓扑线挡住四向手柄
                _hover = HitTestStation(local, GetNodeRadius() + 6f);
                if (_hover != null)
                {
                    ClearEdgeSelection();
                    SelectStation(_hover, syncEditorSelection: true);
                    _dragMode = DragMode.Connect;
                    _connectFrom = _hover;
                    _activeConnectMode = ResolveConnectMode(e);
                }
                else if (TryHitCompassHandle(local, out var compassDir))
                {
                    if (_selected != null)
                    {
                        _selected.EditorForwardAxis =
                            ConveyorPartMotion.PickDirMatchingWorld(_selected.transform, compassDir);
                        RefreshSelectedSerializedObject();
                    }

                    ClearEdgeSelection();
                    _connectFrom = null;
                    _dragMode = DragMode.None;
                }
                else if (_showTopology && TryHitTestEdge(local, out var hitEdge))
                {
                    SelectEdge(hitEdge, additive);
                    _connectFrom = null;
                    _dragMode = DragMode.None;
                }
                else
                {
                    if (!additive)
                    {
                        SelectStation(null, syncEditorSelection: true);
                        ClearEdgeSelection();
                    }

                    _connectFrom = null;
                    _dragMode = DragMode.BoxSelect;
                    _boxSelectAdditive = additive;
                }

                e.Use();
                Repaint();
            }

            return;
        }

        if (GUIUtility.hotControl != controlId)
            return;

        if (e.type == EventType.MouseDrag)
        {
            _dragCurrentLocal = local;
            if ((_dragCurrentLocal - _dragStartLocal).sqrMagnitude > DragThreshold * DragThreshold)
                _dragMoved = true;

            if (_dragMode == DragMode.Pan)
                _pan += e.delta;
            else if (_dragMode == DragMode.Connect)
            {
                _activeConnectMode = ResolveConnectMode(e);
                _hover = HitTestStation(local, GetConnectSnapRadius());
                if (_hover == _connectFrom)
                    _hover = null;
            }
            else if (_dragMode == DragMode.BoxSelect)
            {
                // 仅更新矩形，由 PaintCanvas 绘制
            }

            e.Use();
            Repaint();
            return;
        }

        if (e.type == EventType.MouseUp)
        {
            _dragCurrentLocal = local;
            if (_dragMode == DragMode.Connect)
                _hover = HitTestStation(local, GetConnectSnapRadius());

            if (e.button == 1 && !_dragMoved)
            {
                if (_hover == null && _showTopology)
                    _hasHoverEdge = TryHitTestEdge(local, out _hoverEdge);
                ShowContextMenu();
            }
            else if (_dragMode == DragMode.Connect)
                FinishConnectGesture();
            else if (_dragMode == DragMode.BoxSelect)
                FinishBoxSelect();

            _dragMode = DragMode.None;
            GUIUtility.hotControl = 0;
            e.Use();
            Repaint();
        }
    }

    private ConnectMode ResolveConnectMode(Event e)
    {
        if (e != null && (e.control || e.command))
            return ConnectMode.PairedStation;
        if (e != null && e.shift)
            return ConnectMode.ReverseNext;
        return _connectMode;
    }

    private static string GetConnectModeLabel(ConnectMode mode) => mode switch
    {
        ConnectMode.ReverseNext => "反转下游",
        ConnectMode.PairedStation => "配对",
        _ => "正转下游"
    };

    private float GetConnectSnapRadius() => GetNodeRadius() + ConnectSnapPadding;

    private void FinishConnectGesture()
    {
        bool hasTarget = _hover != null && _connectFrom != null && _hover != _connectFrom;
        if (hasTarget && _dragMoved)
            FinishConnect();

        _connectFrom = null;
    }

    private void FinishConnect()
    {
        if (_connectFrom == null || _hover == null || _hover == _connectFrom)
        {
            _connectFrom = null;
            return;
        }

        var from = _connectFrom;
        var to = _hover;
        switch (_activeConnectMode)
        {
            case ConnectMode.ForwardNext:
                from.EditorSetNeighbor(to, forward: true, writeOpposite: true, CurrentFloor);
                break;
            case ConnectMode.ReverseNext:
                from.EditorSetNeighbor(to, forward: false, writeOpposite: true, CurrentFloor);
                break;
            case ConnectMode.PairedStation:
                if (from is JackTransferPartMotion jackFrom)
                    jackFrom.EditorSetPairedStation(to);
                else if (to is JackTransferPartMotion jackTo)
                    jackTo.EditorSetPairedStation(from);
                else
                    Debug.LogWarning("[俯视方向配置] 配对连线需要一端为顶升移栽机。", from);
                break;
        }

        SelectStation(from, syncEditorSelection: true);
        RefreshSelectedSerializedObject();
        _connectFrom = null;
        ShowNotification(new GUIContent($"{from.name} → {to.name}（{GetConnectModeLabel(_activeConnectMode)}）"));
    }

    private void PaintCanvas()
    {
        EditorGUI.DrawRect(_canvasRect, CanvasBg);
        GUI.BeginClip(_canvasRect);
        Handles.BeginGUI();

        DrawGrid();
        if (_showTopology)
            DrawTopologyLinks();
        DrawStations();
        DrawDirectionArrows();
        DrawCompassHandles();
        DrawConnectPreview();
        DrawBoxSelectOverlay();
        DrawAxisOverlay();

        Handles.EndGUI();
        GUI.EndClip();
    }

    private void DrawBoxSelectOverlay()
    {
        if (_dragMode != DragMode.BoxSelect || !_dragMoved)
            return;

        Rect box = GetNormalizedBox(_dragStartLocal, _dragCurrentLocal);
        EditorGUI.DrawRect(box, BoxSelectFill);
        Handles.color = BoxSelectBorder;
        Vector3[] corners =
        {
            new Vector3(box.xMin, box.yMin, 0f),
            new Vector3(box.xMax, box.yMin, 0f),
            new Vector3(box.xMax, box.yMax, 0f),
            new Vector3(box.xMin, box.yMax, 0f),
            new Vector3(box.xMin, box.yMin, 0f)
        };
        Handles.DrawAAPolyLine(2f, corners);
    }

    private static Rect GetNormalizedBox(Vector2 a, Vector2 b)
    {
        float xMin = Mathf.Min(a.x, b.x);
        float yMin = Mathf.Min(a.y, b.y);
        float xMax = Mathf.Max(a.x, b.x);
        float yMax = Mathf.Max(a.y, b.y);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private void FinishBoxSelect()
    {
        if (!_dragMoved || !_showTopology)
            return;

        Rect box = GetNormalizedBox(_dragStartLocal, _dragCurrentLocal);
        if (box.width < 2f && box.height < 2f)
            return;

        RebuildEdges();
        var hits = new List<TopologyEdge>();
        for (int i = 0; i < _edges.Count; i++)
        {
            var edge = _edges[i];
            if (!edge.IsValid) continue;
            if (!IsStationVisibleInCanvas(edge.A) || !IsStationVisibleInCanvas(edge.B))
                continue;
            if (EdgeIntersectsBox(edge, box))
                hits.Add(edge);
        }

        if (!_boxSelectAdditive)
            ClearEdgeSelection();

        for (int i = 0; i < hits.Count; i++)
            AddEdgeToSelection(hits[i]);

        if (hits.Count > 0)
        {
            _selected = null;
            RefreshSelectedSerializedObject();
            Selection.activeObject = null;
            ShowNotification(new GUIContent($"框选连线 {hits.Count} 条"));
        }
    }

    private bool EdgeIntersectsBox(TopologyEdge edge, Rect box)
    {
        Vector2 a = WorldToLocal(edge.A.transform.position);
        Vector2 b = WorldToLocal(edge.B.transform.position);
        if (edge.Kind == TopologyEdgeKind.Paired)
        {
            Vector2 control = GetPairCurveControl(a, b, edge.A.GetInstanceID(), edge.B.GetInstanceID());
            Vector2 prev = a;
            const int segments = 18;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector2 p = EvalQuadBezier(a, control, b, t);
                if (SegmentIntersectsRect(prev, p, box))
                    return true;
                prev = p;
            }

            return false;
        }

        return SegmentIntersectsRect(a, b, box);
    }

    private static bool SegmentIntersectsRect(Vector2 a, Vector2 b, Rect rect)
    {
        if (rect.Contains(a) || rect.Contains(b))
            return true;

        Vector2 r0 = new Vector2(rect.xMin, rect.yMin);
        Vector2 r1 = new Vector2(rect.xMax, rect.yMin);
        Vector2 r2 = new Vector2(rect.xMax, rect.yMax);
        Vector2 r3 = new Vector2(rect.xMin, rect.yMax);
        return SegmentsIntersect(a, b, r0, r1)
               || SegmentsIntersect(a, b, r1, r2)
               || SegmentsIntersect(a, b, r2, r3)
               || SegmentsIntersect(a, b, r3, r0);
    }

    private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float d1 = Cross(b - a, c - a);
        float d2 = Cross(b - a, d - a);
        float d3 = Cross(d - c, a - c);
        float d4 = Cross(d - c, b - c);
        if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) &&
            ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f)))
            return true;

        return (Mathf.Abs(d1) < 1e-6f && PointOnSegment(c, a, b))
               || (Mathf.Abs(d2) < 1e-6f && PointOnSegment(d, a, b))
               || (Mathf.Abs(d3) < 1e-6f && PointOnSegment(a, c, d))
               || (Mathf.Abs(d4) < 1e-6f && PointOnSegment(b, c, d));
    }

    private static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

    private static bool PointOnSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        if (Vector2.Distance(p, a) + Vector2.Distance(p, b) > Vector2.Distance(a, b) + 1e-3f)
            return false;
        return true;
    }

    private void DrawGrid()
    {
        float step = NiceGridStep();
        Vector2 origin = GraphToLocal(Vector2.zero);
        Handles.color = GridColor;

        float startX = origin.x % (step * _zoom);
        for (float x = startX; x < _canvasRect.width; x += step * _zoom)
            Handles.DrawLine(new Vector3(x, 0f), new Vector3(x, _canvasRect.height));

        float startY = origin.y % (step * _zoom);
        for (float y = startY; y < _canvasRect.height; y += step * _zoom)
            Handles.DrawLine(new Vector3(0f, y), new Vector3(_canvasRect.width, y));
    }

    private void DrawStations()
    {
        var labelStyle = GetLabelStyle();
        float radius = GetNodeRadius();
        for (int i = 0; i < _stations.Count; i++)
        {
            var station = _stations[i];
            if (station == null) continue;
            if (!IsStationVisibleInCanvas(station)) continue;

            Vector2 p = WorldToLocal(station.transform.position);
            bool selected = station == _selected;
            bool hovered = station == _hover;
            bool connectFrom = station == _connectFrom;
            bool dropTarget = _dragMode == DragMode.Connect && station == _hover && station != _connectFrom;

            Color fill = GetStationFill(station);
            if (selected) fill = SelectedFill;
            if (connectFrom) fill = new Color(0.18f, 0.55f, 0.28f, 1f);
            if (dropTarget) fill = new Color(0.95f, 0.75f, 0.2f, 1f);
            fill.a *= GetCanvasDisplayAlpha(station);

            Handles.color = fill;
            Handles.DrawSolidDisc(p, Vector3.forward, radius);
            Handles.color = hovered || selected || connectFrom || dropTarget
                ? new Color(1f, 1f, 1f, fill.a)
                : new Color(0.7f, 0.85f, 0.95f, 0.9f * fill.a);
            Handles.DrawWireDisc(p, Vector3.forward, radius);
            if (dropTarget)
            {
                Handles.color = new Color(1f, 0.85f, 0.2f, 0.95f * fill.a);
                Handles.DrawWireDisc(p, Vector3.forward, radius + 5f);
            }

            if (_showLabels || hovered || selected || connectFrom)
            {
                var labelRect = new Rect(p.x - 80f, p.y + radius + 1f, 160f, 16f);
                Color old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, fill.a);
                GUI.Label(labelRect, station.name, labelStyle);
                GUI.color = old;
            }
        }
    }

    private void DrawDirectionArrows()
    {
        float handleMeters = DirHandlePixels / Mathf.Max(_zoom, 0.01f);
        for (int i = 0; i < _stations.Count; i++)
        {
            var station = _stations[i];
            if (station == null) continue;
            if (!IsStationVisibleInCanvas(station)) continue;

            Vector3 worldAxis = station.ForwardWorldDirection;
            Vector2 planar = new Vector2(worldAxis.x, worldAxis.z);
            if (planar.sqrMagnitude < 1e-8f)
                continue;

            planar.Normalize();
            Vector2 from = WorldToLocal(station.transform.position);
            Vector2 to = from + WorldPlanarDeltaToLocal(planar * handleMeters);
            bool selected = station == _selected;
            Color color = selected ? ForwardColor : new Color(ForwardColor.r, ForwardColor.g, ForwardColor.b, 0.7f);
            color.a *= GetCanvasDisplayAlpha(station);
            DrawArrow(from, to, color, selected ? 11f : 8f);
        }
    }

    private void DrawTopologyLinks()
    {
        RebuildEdges();
        for (int i = 0; i < _edges.Count; i++)
        {
            var edge = _edges[i];
            if (!edge.IsValid) continue;
            if (!IsStationVisibleInCanvas(edge.A) || !IsStationVisibleInCanvas(edge.B))
                continue;

            bool selected = IsEdgeSelected(edge);
            bool hovered = _hasHoverEdge && _hoverEdge.Key == edge.Key;
            if (edge.Kind == TopologyEdgeKind.Paired)
                DrawCurvedLink(edge.A, edge.B, PairedColor, selected, hovered);
            else
                DrawNeighborEdge(edge, selected, hovered);
        }
    }

    private void RebuildEdges()
    {
        _edges.Clear();
        var seen = new HashSet<long>();

        for (int i = 0; i < _stations.Count; i++)
        {
            var station = _stations[i];
            if (station == null) continue;

            TryAddNeighborEdge(station, GetForwardNext(station), forwardSide: station, seen);
            TryAddNeighborEdge(station, GetReverseNext(station), forwardSide: null, seen);

            if (station is JackTransferPartMotion jack && jack.PairedStation != null)
            {
                var edge = new TopologyEdge
                {
                    A = jack,
                    B = jack.PairedStation,
                    Kind = TopologyEdgeKind.Paired
                };
                if (seen.Add(edge.Key))
                    _edges.Add(edge);
            }
        }

        if (_hasSelectedEdge)
            PruneInvalidEdgeSelection();
    }

    private void TryAddNeighborEdge(
        ConveyorPartMotion from,
        ConveyorPartMotion to,
        ConveyorPartMotion forwardSide,
        HashSet<long> seen)
    {
        if (from == null || to == null || from == to) return;

        var edge = new TopologyEdge
        {
            A = from,
            B = to,
            Kind = TopologyEdgeKind.Neighbor,
            ForwardSide = forwardSide
        };

        if (!seen.Add(edge.Key))
        {
            // 已有无向边时，补上正转侧信息
            for (int i = 0; i < _edges.Count; i++)
            {
                if (_edges[i].Key != edge.Key) continue;
                if (_edges[i].ForwardSide == null && forwardSide != null)
                {
                    var updated = _edges[i];
                    updated.ForwardSide = forwardSide;
                    // 保持较小 InstanceID 为 A，便于稳定显示
                    if (from.GetInstanceID() < to.GetInstanceID())
                    {
                        updated.A = from;
                        updated.B = to;
                    }

                    _edges[i] = updated;
                }

                return;
            }

            return;
        }

        if (from.GetInstanceID() > to.GetInstanceID())
        {
            edge.A = to;
            edge.B = from;
        }

        if (edge.ForwardSide == null)
        {
            if (GetForwardNext(edge.A) == edge.B) edge.ForwardSide = edge.A;
            else if (GetForwardNext(edge.B) == edge.A) edge.ForwardSide = edge.B;
        }

        _edges.Add(edge);
    }

    private void DrawNeighborEdge(TopologyEdge edge, bool selected, bool hovered)
    {
        var forward = ResolveForwardSide(edge);
        ConveyorPartMotion back = ResolveBackSide(edge, forward);
        if (forward == null || back == null)
            return;

        bool reverseOnly = edge.ForwardSide == null
                           && (GetReverseNext(edge.A) == edge.B || GetReverseNext(edge.B) == edge.A)
                           && GetForwardNext(edge.A) != edge.B
                           && GetForwardNext(edge.B) != edge.A;

        Vector2 a = WorldToLocal(forward.transform.position);
        Vector2 b = WorldToLocal(back.transform.position);
        float width = selected ? 5.5f : hovered ? 4.2f : 2.4f;
        Color color = selected
            ? MultiSelectedEdge
            : hovered
                ? new Color(0.85f, 0.95f, 1f, 1f)
                : reverseOnly
                    ? ReverseColor
                    : ForwardColor;

        Handles.color = color;
        Handles.DrawAAPolyLine(width, a, b);
        DrawArrowHead(a, b, color, selected ? 12f : 9f);
        if (_showEdgeFlow)
            DrawFlowChevrons(a, b, color, selected ? 8f : 6.5f);

        // 互为前后时补画反转回程色，仍属同一条可选中连线
        bool mutual = !reverseOnly && GetReverseNext(back) == forward;
        if (mutual)
        {
            Color rev = selected
                ? new Color(1f, 0.75f, 0.35f, 0.95f)
                : new Color(ReverseColor.r, ReverseColor.g, ReverseColor.b, 0.75f);
            Vector2 delta = b - a;
            Vector2 orth = delta.sqrMagnitude > 1e-6f
                ? new Vector2(-delta.y, delta.x).normalized * 3.5f
                : Vector2.up * 3.5f;
            Handles.color = rev;
            Handles.DrawAAPolyLine(selected ? 3.2f : 2f, b + orth, a + orth);
            DrawArrowHead(b + orth, a + orth, rev, 7f);
            if (_showEdgeFlow)
                DrawFlowChevrons(b + orth, a + orth, rev, 5.5f);
        }

        if (_showEdgeFlow && (selected || hovered))
        {
            string label = reverseOnly
                ? $"{forward.name} 反→ {back.name}"
                : $"{forward.name} → {back.name}";
            Vector2 mid = (a + b) * 0.5f;
            GUI.Label(new Rect(mid.x - 90f, mid.y - 16f, 180f, 18f), label, GetLabelStyle());
        }
    }

    private ConveyorPartMotion ResolveForwardSide(TopologyEdge edge)
    {
        if (!edge.IsValid) return null;
        if (edge.ForwardSide != null)
            return edge.ForwardSide;

        if (GetForwardNext(edge.A) == edge.B) return edge.A;
        if (GetForwardNext(edge.B) == edge.A) return edge.B;
        if (GetReverseNext(edge.A) == edge.B) return edge.A;
        if (GetReverseNext(edge.B) == edge.A) return edge.B;
        return edge.A;
    }

    private static ConveyorPartMotion ResolveBackSide(TopologyEdge edge, ConveyorPartMotion forward)
    {
        if (!edge.IsValid || forward == null) return null;
        return forward == edge.A ? edge.B : edge.A;
    }

    private string DescribeEdgeDirection(TopologyEdge edge)
    {
        if (!edge.IsValid) return string.Empty;
        if (edge.Kind == TopologyEdgeKind.Paired)
            return $"{edge.A.name} ↔ {edge.B.name}（配对）";

        var forward = ResolveForwardSide(edge);
        var back = ResolveBackSide(edge, forward);
        if (forward == null || back == null)
            return $"{edge.A.name} ↔ {edge.B.name}";
        return $"{forward.name} → {back.name}";
    }

    private void DrawLink(ConveyorPartMotion from, ConveyorPartMotion to, Color color)
    {
        if (from == null || to == null) return;

        Vector2 a = WorldToLocal(from.transform.position);
        Vector2 b = WorldToLocal(to.transform.position);
        Handles.color = color;
        Handles.DrawAAPolyLine(2.4f, a, b);
        DrawArrow(a, b, color, 8f);
    }

    /// <summary>
    /// 配对线用曲线：顶升移栽与输送线常重叠，直线会叠在一起看不清。
    /// </summary>
    private void DrawCurvedLink(ConveyorPartMotion from, ConveyorPartMotion to, Color color) =>
        DrawCurvedLink(from, to, color, selected: false, hovered: false);

    private void DrawCurvedLink(
        ConveyorPartMotion from,
        ConveyorPartMotion to,
        Color color,
        bool selected,
        bool hovered)
    {
        if (from == null || to == null) return;

        Vector2 a = WorldToLocal(from.transform.position);
        Vector2 b = WorldToLocal(to.transform.position);
        Vector2 control = GetPairCurveControl(a, b, from.GetInstanceID(), to.GetInstanceID());
        Color drawColor = selected
            ? MultiSelectedEdge
            : hovered
                ? new Color(1f, 0.95f, 0.55f, 1f)
                : color;
        float width = selected ? 4.5f : hovered ? 3.6f : 2.8f;
        DrawQuadraticCurve(a, control, b, drawColor, width);
        DrawArrowHead(
            EvalQuadBezier(a, control, b, 0.78f),
            EvalQuadBezier(a, control, b, 1f),
            drawColor, selected ? 10f : 8f);
    }

    private static Vector2 GetPairCurveControl(Vector2 a, Vector2 b, int fromId, int toId)
    {
        Vector2 mid = (a + b) * 0.5f;
        Vector2 delta = b - a;
        float dist = delta.magnitude;

        Vector2 normal;
        float bulge;
        if (dist < 6f)
        {
            // 几乎重叠：按实例稳定挑一个鼓出方向，画成可见的回环弧
            int slot = Mathf.Abs(fromId ^ toId) & 7;
            float angle = slot * (Mathf.PI * 0.25f) + 0.35f;
            normal = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            bulge = 42f;
        }
        else
        {
            Vector2 dir = delta / dist;
            normal = new Vector2(-dir.y, dir.x);
            if (((fromId ^ toId) & 1) == 0)
                normal = -normal;
            bulge = Mathf.Clamp(dist * 0.45f, 30f, 90f);
        }

        return mid + normal * bulge;
    }

    private static void DrawQuadraticCurve(Vector2 a, Vector2 control, Vector2 b, Color color, float width)
    {
        const int segments = 18;
        var points = new Vector3[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            Vector2 p = EvalQuadBezier(a, control, b, t);
            points[i] = new Vector3(p.x, p.y, 0f);
        }

        Handles.color = color;
        Handles.DrawAAPolyLine(width, points);
    }

    private static Vector2 EvalQuadBezier(Vector2 a, Vector2 control, Vector2 b, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * control + t * t * b;
    }

    private void DrawConnectPreview()
    {
        if (_dragMode != DragMode.Connect || _connectFrom == null || !_dragMoved)
            return;

        Vector2 from = WorldToLocal(_connectFrom.transform.position);
        Vector2 to = _hover != null && _hover != _connectFrom
            ? WorldToLocal(_hover.transform.position)
            : _dragCurrentLocal;

        Color color = _activeConnectMode switch
        {
            ConnectMode.ReverseNext => ReverseColor,
            ConnectMode.PairedStation => PairedColor,
            _ => ForwardColor
        };

        if (_activeConnectMode == ConnectMode.PairedStation)
        {
            int toId = _hover != null ? _hover.GetInstanceID() : 0;
            Vector2 control = GetPairCurveControl(from, to, _connectFrom.GetInstanceID(), toId);
            DrawQuadraticCurve(from, control, to, color, 3.2f);
            DrawArrow(
                EvalQuadBezier(from, control, to, 0.78f),
                to,
                color, 10f);
        }
        else
        {
            Handles.color = color;
            Handles.DrawAAPolyLine(3.2f, from, to);
            DrawArrow(from, to, color, 10f);
        }

        var labelStyle = GetLabelStyle();
        string tip = _hover != null && _hover != _connectFrom
            ? $"{GetConnectModeLabel(_activeConnectMode)} → {_hover.name}"
            : $"{GetConnectModeLabel(_activeConnectMode)}（拖到目标工位）";
        Vector2 mid = (from + to) * 0.5f;
        GUI.Label(new Rect(mid.x - 90f, mid.y - 18f, 180f, 18f), tip, labelStyle);
    }

    private void DrawAxisOverlay()
    {
        var style = GetAxisStyle();
        string right = FormatPlanarAxis(InverseRotatePlanar(Vector2.right));
        string up = FormatPlanarAxis(InverseRotatePlanar(Vector2.up));
        GUI.Label(
            new Rect(10f, 8f, 360f, 18f),
            $"俯视 · 旋转 {_viewYawDegrees:0}° · 屏右={right}  屏上={up}",
            style);
    }

    private void ShowContextMenu()
    {
        var menu = new GenericMenu();
        if (_hover != null)
        {
            var station = _hover;
            menu.AddItem(new GUIContent($"选中 {station.name}"), false, () =>
                SelectStation(station, syncEditorSelection: true));
            menu.AddItem(new GUIContent("自动连接前后工位"), false, () =>
            {
                station.EditorTryAutoBindForward(true, _stations, CurrentFloor);
                station.EditorTryAutoBindReverse(true, _stations, CurrentFloor);
                SelectStation(station, syncEditorSelection: true);
            });
            menu.AddItem(new GUIContent("聚焦场景"), false, () =>
            {
                SelectStation(station, syncEditorSelection: true);
                FrameSelectedInScene();
            });
        }
        else if (_hasHoverEdge && _hoverEdge.IsValid)
        {
            var edge = _hoverEdge;
            string kind = edge.Kind == TopologyEdgeKind.Paired ? "配对" : "前后";
            menu.AddItem(
                new GUIContent($"选中{kind}连线 {edge.A.name}↔{edge.B.name}"),
                false,
                () => SelectEdge(edge, additive: false));
            menu.AddItem(new GUIContent($"删除连线（两端一起清）"), false, () =>
            {
                SelectEdge(edge, additive: false);
                DeleteSelectedEdges();
            });
        }
        else if (_hasSelectedEdge && _selectedEdge.IsValid)
        {
            menu.AddItem(new GUIContent("反转选中正转方向"), false, ReverseSelectedNeighborEdges);
            menu.AddItem(new GUIContent("删除选中连线（两端一起清）"), false, DeleteSelectedEdges);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("适应窗口"), false, () =>
            {
                FitView(focusSelection: false);
                Repaint();
            });
        }
        else
        {
            menu.AddItem(new GUIContent("适应窗口"), false, () =>
            {
                FitView(focusSelection: false);
                Repaint();
            });
            menu.AddItem(new GUIContent("聚焦选中区域"), false, () =>
            {
                FitView(focusSelection: true);
                Repaint();
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("智能连线"), false, RunSmartConnect);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("视图旋转/左转 90°"), false, () => RotateView(-90f));
            menu.AddItem(new GUIContent("视图旋转/右转 90°"), false, () => RotateView(90f));
            menu.AddItem(new GUIContent("视图旋转/0°"), Mathf.Abs(Mathf.DeltaAngle(_viewYawDegrees, 0f)) < 0.1f,
                () => SetViewYaw(0f, keepCenter: true));
            menu.AddItem(new GUIContent("视图旋转/90°"), Mathf.Abs(Mathf.DeltaAngle(_viewYawDegrees, 90f)) < 0.1f,
                () => SetViewYaw(90f, keepCenter: true));
            menu.AddItem(new GUIContent("视图旋转/180°"), Mathf.Abs(Mathf.DeltaAngle(_viewYawDegrees, 180f)) < 0.1f,
                () => SetViewYaw(180f, keepCenter: true));
            menu.AddItem(new GUIContent("视图旋转/270°"), Mathf.Abs(Mathf.DeltaAngle(_viewYawDegrees, 270f)) < 0.1f,
                () => SetViewYaw(270f, keepCenter: true));
        }

        menu.ShowAsContext();
    }

    private void RunSmartConnect()
    {
        RebuildStations();
        if (_stations.Count == 0)
        {
            _lastSmartResult = "无工位可连";
            ShowNotification(new GUIContent(_lastSmartResult));
            Repaint();
            return;
        }

        if (_smartOverwrite &&
            !EditorUtility.DisplayDialog(
                "智能连线",
                $"将覆盖当前列表中 {_stations.Count} 个工位的已有上下游" +
                (_smartPairJack ? "与顶升配对" : string.Empty) +
                "，是否继续？\n（可用 Ctrl+Z 撤销）",
                "继续",
                "取消"))
            return;

        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("智能连线");

        int forwardBound = 0;
        int reverseBound = 0;
        int pairedBound = 0;

        for (int i = 0; i < _stations.Count; i++)
        {
            var station = _stations[i];
            if (station == null) continue;

            if (station.EditorTryAutoBindForward(_smartOverwrite, _stations, CurrentFloor))
                forwardBound++;
            if (station.EditorTryAutoBindReverse(_smartOverwrite, _stations, CurrentFloor))
                reverseBound++;
        }

        if (_smartPairJack)
            pairedBound = SmartBindJackPairs(_smartOverwrite);

        Undo.CollapseUndoOperations(group);

        _lastSmartResult =
            $"智能连线：正转 +{forwardBound} · 反转 +{reverseBound}" +
            (_smartPairJack ? $" · 配对 +{pairedBound}" : string.Empty) +
            $" / {_stations.Count} 工位";
        Debug.Log($"[StationDirectionWindow] {_lastSmartResult}", this);
        ShowNotification(new GUIContent(_lastSmartResult));
        RefreshSelectedSerializedObject();
        Repaint();
    }

    /// <summary>
    /// 顶升移栽配对：优先同物体上的输送组件，否则取水平距离最近且未被占用的输送工位。
    /// </summary>
    private int SmartBindJackPairs(bool overwrite)
    {
        var jacks = new List<JackTransferPartMotion>();
        var conveyors = new List<ConveyorPartMotion>();

        for (int i = 0; i < _stations.Count; i++)
        {
            var station = _stations[i];
            if (station == null) continue;
            if (station is JackTransferPartMotion jack)
                jacks.Add(jack);
            else
                conveyors.Add(station);
        }

        var claimed = new HashSet<int>();
        if (!overwrite)
        {
            for (int i = 0; i < jacks.Count; i++)
            {
                var paired = jacks[i].PairedStation;
                if (paired != null && paired != jacks[i])
                    claimed.Add(paired.GetInstanceID());
            }
        }

        int bound = 0;
        for (int i = 0; i < jacks.Count; i++)
        {
            var jack = jacks[i];
            if (jack == null) continue;
            if (!overwrite && jack.PairedStation != null && jack.PairedStation != jack)
                continue;

            ConveyorPartMotion best = FindSameObjectConveyor(jack);
            if (best != null && claimed.Contains(best.GetInstanceID()) && !overwrite)
                best = null;
            if (best == null)
                best = FindNearestConveyor(jack, conveyors, claimed);

            if (best == null) continue;

            jack.EditorSetPairedStation(best);
            claimed.Add(best.GetInstanceID());
            bound++;
        }

        return bound;
    }

    private static ConveyorPartMotion FindSameObjectConveyor(JackTransferPartMotion jack)
    {
        var stations = jack.GetComponents<ConveyorPartMotion>();
        for (int i = 0; i < stations.Length; i++)
        {
            var s = stations[i];
            if (s != null && s != jack && s is not JackTransferPartMotion)
                return s;
        }

        return null;
    }

    private static ConveyorPartMotion FindNearestConveyor(
        JackTransferPartMotion jack,
        List<ConveyorPartMotion> conveyors,
        HashSet<int> claimed)
    {
        ConveyorPartMotion best = null;
        float bestDist = SmartPairMaxDistance;

        Vector3 jackPos = jack.transform.position;
        for (int i = 0; i < conveyors.Count; i++)
        {
            var conveyor = conveyors[i];
            if (conveyor == null || conveyor == jack) continue;
            if (claimed.Contains(conveyor.GetInstanceID()))
                continue;

            Vector3 delta = conveyor.transform.position - jackPos;
            delta.y = 0f;
            float dist = delta.magnitude;
            if (dist > bestDist) continue;

            bestDist = dist;
            best = conveyor;
        }

        return best;
    }

    private void AlignSelectedToWorld(Vector3 worldDir)
    {
        if (_selected == null) return;
        _selected.EditorForwardAxis = ConveyorPartMotion.PickDirMatchingWorld(_selected.transform, worldDir);
        RefreshSelectedSerializedObject();
        Repaint();
    }

    private void SelectStation(ConveyorPartMotion station, bool syncEditorSelection)
    {
        _selected = station;
        if (station != null)
            ClearEdgeSelection();
        RefreshSelectedSerializedObject();
        if (syncEditorSelection)
            Selection.activeObject = station != null ? station.gameObject : null;
    }

    private void SelectEdge(TopologyEdge edge) => SelectEdge(edge, additive: false);

    private void SelectEdge(TopologyEdge edge, bool additive)
    {
        if (!edge.IsValid) return;

        if (additive)
        {
            if (IsEdgeSelected(edge))
                RemoveEdgeFromSelection(edge.Key);
            else
                AddEdgeToSelection(edge);
        }
        else
        {
            ClearEdgeSelection();
            AddEdgeToSelection(edge);
        }

        _selected = null;
        RefreshSelectedSerializedObject();
        Selection.activeObject = null;
        Repaint();
    }

    private bool IsEdgeSelected(TopologyEdge edge) =>
        edge.IsValid && _selectedEdgeKeys.Contains(edge.Key);

    private void AddEdgeToSelection(TopologyEdge edge)
    {
        if (!edge.IsValid || !_selectedEdgeKeys.Add(edge.Key))
            return;

        _selectedEdges.Add(edge);
        RefreshPrimarySelectedEdge();
    }

    private void RemoveEdgeFromSelection(long key)
    {
        if (!_selectedEdgeKeys.Remove(key))
            return;

        for (int i = _selectedEdges.Count - 1; i >= 0; i--)
        {
            if (_selectedEdges[i].Key == key)
                _selectedEdges.RemoveAt(i);
        }

        RefreshPrimarySelectedEdge();
    }

    private void RefreshPrimarySelectedEdge()
    {
        _hasSelectedEdge = _selectedEdges.Count > 0;
        _selectedEdge = _hasSelectedEdge ? _selectedEdges[_selectedEdges.Count - 1] : default;
    }

    private void ClearEdgeSelection()
    {
        _hasSelectedEdge = false;
        _selectedEdge = default;
        _selectedEdges.Clear();
        _selectedEdgeKeys.Clear();
    }

    private void PruneInvalidEdgeSelection()
    {
        if (_selectedEdges.Count == 0)
        {
            ClearEdgeSelection();
            return;
        }

        var validKeys = new HashSet<long>();
        for (int i = 0; i < _edges.Count; i++)
        {
            if (_edges[i].IsValid)
                validKeys.Add(_edges[i].Key);
        }

        for (int i = _selectedEdges.Count - 1; i >= 0; i--)
        {
            long key = _selectedEdges[i].Key;
            if (validKeys.Contains(key))
            {
                // 用重建后的边实例替换，保证 ForwardSide 等字段最新
                for (int j = 0; j < _edges.Count; j++)
                {
                    if (_edges[j].Key != key) continue;
                    _selectedEdges[i] = _edges[j];
                    break;
                }

                continue;
            }

            _selectedEdgeKeys.Remove(key);
            _selectedEdges.RemoveAt(i);
        }

        RefreshPrimarySelectedEdge();
    }

    private void DeleteSelectedEdges()
    {
        if (_selectedEdges.Count == 0)
            return;

        var edges = new List<TopologyEdge>(_selectedEdges);
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(edges.Count > 1 ? "批量删除工位连线" : "删除工位连线");

        int deleted = 0;
        for (int i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            if (!edge.IsValid) continue;

            if (edge.Kind == TopologyEdgeKind.Paired)
            {
                if (edge.A is JackTransferPartMotion jackA)
                    jackA.EditorSetPairedStation(null);
                if (edge.B is JackTransferPartMotion jackB)
                    jackB.EditorSetPairedStation(null);
            }
            else
            {
                ConveyorPartMotion.EditorClearMutualNeighbor(edge.A, edge.B, CurrentFloor);
            }

            deleted++;
        }

        Undo.CollapseUndoOperations(group);
        ClearEdgeSelection();
        RefreshSelectedSerializedObject();
        ShowNotification(new GUIContent($"已删除连线 {deleted} 条"));
        Repaint();
    }

    private void ReverseSelectedNeighborEdges()
    {
        if (_selectedEdges.Count == 0)
            return;

        var edges = new List<TopologyEdge>(_selectedEdges);
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(edges.Count > 1 ? "批量反转连线方向" : "反转连线方向");

        int reversed = 0;
        for (int i = 0; i < edges.Count; i++)
        {
            if (TryReverseNeighborEdge(edges[i], CurrentFloor))
                reversed++;
        }

        Undo.CollapseUndoOperations(group);
        RebuildEdges();
        PruneInvalidEdgeSelection();

        // 反转后按 key 重新挂上选中
        var keys = new List<long>(_selectedEdgeKeys);
        ClearEdgeSelection();
        for (int i = 0; i < _edges.Count; i++)
        {
            if (keys.Contains(_edges[i].Key))
                AddEdgeToSelection(_edges[i]);
        }

        RefreshSelectedSerializedObject();
        ShowNotification(new GUIContent($"已反转正转方向 {reversed} 条"));
        Repaint();
    }

    private static bool TryReverseNeighborEdge(TopologyEdge edge, int floorPlc)
    {
        if (!edge.IsValid || edge.Kind != TopologyEdgeKind.Neighbor)
            return false;

        var forward = edge.ForwardSide;
        if (forward == null)
        {
            if (edge.A.EditorGetForwardNext(floorPlc) == edge.B) forward = edge.A;
            else if (edge.B.EditorGetForwardNext(floorPlc) == edge.A) forward = edge.B;
            else if (edge.A.EditorGetReverseNext(floorPlc) == edge.B) forward = edge.A;
            else if (edge.B.EditorGetReverseNext(floorPlc) == edge.A) forward = edge.B;
            else forward = edge.A;
        }

        var back = forward == edge.A ? edge.B : edge.A;
        if (forward == null || back == null)
            return false;

        bool hadForward = forward.EditorGetForwardNext(floorPlc) == back;
        bool hadReverseOnly = !hadForward && forward.EditorGetReverseNext(floorPlc) == back;

        ConveyorPartMotion.EditorClearMutualNeighbor(edge.A, edge.B, floorPlc);

        if (hadReverseOnly)
        {
            // 仅反转引用：改为正转上下游
            back.EditorSetNeighbor(forward, forward: true, writeOpposite: true, floorPlc);
        }
        else
        {
            // 正转上游/下游对调
            back.EditorSetNeighbor(forward, forward: true, writeOpposite: true, floorPlc);
        }

        return true;
    }

    private bool TryHitTestEdge(Vector2 local, out TopologyEdge hit)
    {
        RebuildEdges();
        hit = default;
        float best = EdgeHitWidth;
        bool found = false;

        for (int i = 0; i < _edges.Count; i++)
        {
            var edge = _edges[i];
            if (!edge.IsValid) continue;
            if (!IsStationVisibleInCanvas(edge.A) || !IsStationVisibleInCanvas(edge.B))
                continue;

            float dist = edge.Kind == TopologyEdgeKind.Paired
                ? DistanceToPairedCurve(local, edge.A, edge.B)
                : DistancePointToSegment(
                    local,
                    WorldToLocal(edge.A.transform.position),
                    WorldToLocal(edge.B.transform.position));

            if (dist >= best) continue;
            best = dist;
            hit = edge;
            found = true;
        }

        return found;
    }

    private float DistanceToPairedCurve(Vector2 local, ConveyorPartMotion from, ConveyorPartMotion to)
    {
        Vector2 a = WorldToLocal(from.transform.position);
        Vector2 b = WorldToLocal(to.transform.position);
        Vector2 control = GetPairCurveControl(a, b, from.GetInstanceID(), to.GetInstanceID());
        float best = float.PositiveInfinity;
        Vector2 prev = a;
        const int segments = 18;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            Vector2 p = EvalQuadBezier(a, control, b, t);
            best = Mathf.Min(best, DistancePointToSegment(local, prev, p));
            prev = p;
        }

        return best;
    }

    private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 1e-8f)
            return Vector2.Distance(p, a);

        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
        return Vector2.Distance(p, a + ab * t);
    }

    private void RefreshSelectedSerializedObject()
    {
        _selectedSO = _selected != null ? new SerializedObject(_selected) : null;
    }

    private void SyncFromEditorSelection()
    {
        var go = Selection.activeGameObject;
        if (go == null) return;

        var manager = go.GetComponent<ConveyorLineManager>();
        var station = go.GetComponent<ConveyorPartMotion>();
        if (station != null)
        {
            if (_manager == null || !_manager.Contains(station))
            {
                var owner = ConveyorLineManager.FindOwner(station);
                if (owner != null)
                    SetManager(owner);
            }

            if (_onlySsxTypes && !IsListedStationType(station))
                return;
            SelectStation(station, syncEditorSelection: false);
            return;
        }

        if (manager != null && manager != _manager)
            SetManager(manager);
    }

    private void FrameSelectedInScene()
    {
        if (_selected == null) return;
        Selection.activeObject = _selected.gameObject;
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.FrameSelected();
    }

    private ConveyorPartMotion HitTestStation(Vector2 local) =>
        HitTestStation(local, GetNodeRadius() + 4f);

    private ConveyorPartMotion HitTestStation(Vector2 local, float maxDist)
    {
        ConveyorPartMotion best = null;
        float bestDist = maxDist;
        for (int i = 0; i < _stations.Count; i++)
        {
            var station = _stations[i];
            if (station == null) continue;
            if (!IsStationVisibleInCanvas(station)) continue;
            float dist = Vector2.Distance(local, WorldToLocal(station.transform.position));
            if (dist > bestDist) continue;
            bestDist = dist;
            best = station;
        }

        return best;
    }

    private float GetNodeRadius() =>
        Mathf.Clamp(BaseNodeRadius + Mathf.Log(_zoom + 1f, 2f) * 1.2f, 7f, 16f);

    private void DrawCompassHandles()
    {
        if (_selected == null) return;

        float handleMeters = CompassHandlePixels / Mathf.Max(_zoom, 0.01f);
        Vector2 center = WorldToLocal(_selected.transform.position);
        Vector3 current = _selected.ForwardWorldDirection;
        Vector2 currentPlanar = new Vector2(current.x, current.z);
        if (currentPlanar.sqrMagnitude > 1e-8f)
            currentPlanar.Normalize();

        foreach (var worldDir in GetCompassWorldDirs())
        {
            Vector2 planar = new Vector2(worldDir.x, worldDir.z);
            Vector2 tip = center + WorldPlanarDeltaToLocal(planar * handleMeters);
            bool active = currentPlanar.sqrMagnitude > 1e-8f && Vector2.Dot(currentPlanar, planar) > 0.85f;
            Color fill = active ? ForwardColor : new Color(0.75f, 0.78f, 0.82f, 0.85f);
            Handles.color = fill;
            Handles.DrawSolidDisc(tip, Vector3.forward, 5.5f);
            Handles.color = Color.white;
            Handles.DrawWireDisc(tip, Vector3.forward, 5.5f);
            DrawArrow(center, tip, fill, 7f);
        }
    }

    private bool TryHitCompassHandle(Vector2 local, out Vector3 worldDir)
    {
        worldDir = Vector3.zero;
        if (_selected == null) return false;

        float handleMeters = CompassHandlePixels / Mathf.Max(_zoom, 0.01f);
        Vector2 center = WorldToLocal(_selected.transform.position);
        float best = CompassHitRadius;
        bool hit = false;

        foreach (var dir in GetCompassWorldDirs())
        {
            Vector2 planar = new Vector2(dir.x, dir.z);
            Vector2 tip = center + WorldPlanarDeltaToLocal(planar * handleMeters);
            float dist = Vector2.Distance(local, tip);
            if (dist >= best) continue;
            best = dist;
            worldDir = dir;
            hit = true;
        }

        return hit;
    }

    private static IEnumerable<Vector3> GetCompassWorldDirs()
    {
        yield return Vector3.right;
        yield return Vector3.left;
        yield return Vector3.forward;
        yield return Vector3.back;
    }

    private void FitView(bool focusSelection)
    {
        if (_canvasRect.width < 8f || _canvasRect.height < 8f)
            return;

        if (_stations.Count == 0)
        {
            _zoom = 24f;
            _pan = new Vector2(_canvasRect.width * 0.5f, _canvasRect.height * 0.5f);
            return;
        }

        var points = new List<Vector2>(_stations.Count);
        Vector2 focusCenter = Vector2.zero;
        bool hasFocus = focusSelection && _selected != null;
        if (hasFocus)
            focusCenter = WorldToGraph(_selected.transform.position);

        for (int i = 0; i < _stations.Count; i++)
        {
            var station = _stations[i];
            if (station == null) continue;
            Vector2 p = WorldToGraph(station.transform.position);
            if (hasFocus && Vector2.Distance(p, focusCenter) > FocusRadiusMeters)
                continue;
            points.Add(p);
        }

        if (points.Count == 0)
        {
            for (int i = 0; i < _stations.Count; i++)
            {
                if (_stations[i] == null) continue;
                points.Add(WorldToGraph(_stations[i].transform.position));
            }
        }

        GetRobustBounds(points, out Vector2 min, out Vector2 max);
        Vector2 size = max - min;
        size.x = Mathf.Max(size.x, 1.5f);
        size.y = Mathf.Max(size.y, 1.5f);

        float zoomX = (_canvasRect.width - FitPadding * 2f) / size.x;
        float zoomY = (_canvasRect.height - FitPadding * 2f) / size.y;
        float fitZoom = Mathf.Min(zoomX, zoomY);

        float medianGap = ComputeMedianNearestNeighborGap(points);
        float spacingZoom = medianGap > 1e-4f
            ? TargetNeighborPixels / medianGap
            : fitZoom;

        float zoom = fitZoom;
        // 整图过小时按邻距放大，避免工位叠成一团（可再点「适应全部」或滚轮缩小）
        if (medianGap * fitZoom < TargetNeighborPixels * 0.75f)
            zoom = Mathf.Max(fitZoom, spacingZoom);
        if (hasFocus)
            zoom = Mathf.Max(zoom, spacingZoom * 0.9f);

        _zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);

        Vector2 center = hasFocus ? focusCenter : (min + max) * 0.5f;
        _pan = new Vector2(_canvasRect.width * 0.5f, _canvasRect.height * 0.5f) - GraphToOffset(center);
    }

    private static void GetRobustBounds(List<Vector2> points, out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        if (points.Count == 0)
        {
            min = Vector2.zero;
            max = Vector2.one;
            return;
        }

        if (points.Count <= 4)
        {
            for (int i = 0; i < points.Count; i++)
            {
                min = Vector2.Min(min, points[i]);
                max = Vector2.Max(max, points[i]);
            }

            return;
        }

        Vector2 centroid = Vector2.zero;
        for (int i = 0; i < points.Count; i++)
            centroid += points[i];
        centroid /= points.Count;

        var distances = new List<float>(points.Count);
        for (int i = 0; i < points.Count; i++)
            distances.Add(Vector2.Distance(points[i], centroid));
        distances.Sort();
        float limit = distances[Mathf.Clamp(Mathf.FloorToInt(distances.Count * 0.9f), 0, distances.Count - 1)];
        limit = Mathf.Max(limit, 1f);

        int used = 0;
        for (int i = 0; i < points.Count; i++)
        {
            if (Vector2.Distance(points[i], centroid) > limit * 1.25f)
                continue;
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
            used++;
        }

        if (used == 0)
        {
            for (int i = 0; i < points.Count; i++)
            {
                min = Vector2.Min(min, points[i]);
                max = Vector2.Max(max, points[i]);
            }
        }
    }

    private static float ComputeMedianNearestNeighborGap(List<Vector2> points)
    {
        if (points.Count < 2)
            return 2f;

        var gaps = new List<float>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            float best = float.PositiveInfinity;
            for (int j = 0; j < points.Count; j++)
            {
                if (i == j) continue;
                float d = Vector2.Distance(points[i], points[j]);
                if (d < best) best = d;
            }

            if (best < float.PositiveInfinity)
                gaps.Add(best);
        }

        if (gaps.Count == 0)
            return 2f;

        gaps.Sort();
        return gaps[gaps.Count / 2];
    }

    private void ZoomAt(Vector2 local, float scrollDelta)
    {
        Vector2 graph = LocalToGraph(local);
        float factor = scrollDelta > 0f ? 0.9f : 1.11f;
        _zoom = Mathf.Clamp(_zoom * factor, MinZoom, MaxZoom);
        _pan = local - GraphToOffset(graph);
    }

    private void RotateView(float deltaDegrees) =>
        SetViewYaw(_viewYawDegrees + deltaDegrees, keepCenter: true);

    private void SetViewYaw(float degrees, bool keepCenter)
    {
        degrees = Mathf.Repeat(degrees, 360f);
        if (Mathf.Abs(Mathf.DeltaAngle(_viewYawDegrees, degrees)) < 0.01f)
            return;

        Vector2 localCenter = new Vector2(_canvasRect.width * 0.5f, _canvasRect.height * 0.5f);
        Vector3 worldCenter = default;
        bool hasCenter = keepCenter && _canvasRect.width > 8f && _canvasRect.height > 8f;
        if (hasCenter)
            worldCenter = GraphToWorld(LocalToGraph(localCenter));

        _viewYawDegrees = degrees;
        EditorPrefs.SetFloat(PrefsViewYaw, _viewYawDegrees);

        if (hasCenter)
            _pan = localCenter - GraphToOffset(WorldToGraph(worldCenter));

        Repaint();
    }

    private Vector2 WorldToGraph(Vector3 world) =>
        RotatePlanar(new Vector2(world.x, world.z));

    private Vector3 GraphToWorld(Vector2 graph)
    {
        Vector2 xz = InverseRotatePlanar(graph);
        return new Vector3(xz.x, 0f, xz.y);
    }

    private Vector2 WorldToLocal(Vector3 world) => GraphToLocal(WorldToGraph(world));

    private Vector2 GraphToLocal(Vector2 graph) => _pan + GraphToOffset(graph);

    private Vector2 GraphToOffset(Vector2 graph) =>
        new Vector2(graph.x * _zoom, -graph.y * _zoom);

    private Vector2 LocalToGraph(Vector2 local)
    {
        Vector2 offset = local - _pan;
        return new Vector2(offset.x / _zoom, -offset.y / _zoom);
    }

    private Vector2 WorldPlanarDeltaToLocal(Vector2 worldPlanarDelta) =>
        GraphToOffset(RotatePlanar(worldPlanarDelta));

    private Vector2 RotatePlanar(Vector2 v)
    {
        if (Mathf.Abs(_viewYawDegrees) < 1e-4f)
            return v;

        float rad = _viewYawDegrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(rad);
        float s = Mathf.Sin(rad);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    private Vector2 InverseRotatePlanar(Vector2 v)
    {
        if (Mathf.Abs(_viewYawDegrees) < 1e-4f)
            return v;

        float rad = -_viewYawDegrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(rad);
        float s = Mathf.Sin(rad);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    private static string FormatPlanarAxis(Vector2 worldPlanar)
    {
        if (worldPlanar.sqrMagnitude < 1e-8f)
            return "?";

        worldPlanar.Normalize();
        if (Mathf.Abs(worldPlanar.x) >= Mathf.Abs(worldPlanar.y))
            return worldPlanar.x >= 0f ? "+X" : "-X";
        return worldPlanar.y >= 0f ? "+Z" : "-Z";
    }

    private float NiceGridStep()
    {
        float worldStep = 1f;
        float pixel = worldStep * _zoom;
        while (pixel < 28f)
        {
            worldStep *= 2f;
            pixel = worldStep * _zoom;
            if (worldStep > 1e5f) break;
        }

        while (pixel > 96f && worldStep > 1e-3f)
        {
            worldStep *= 0.5f;
            pixel = worldStep * _zoom;
        }

        return Mathf.Max(worldStep, 1e-3f);
    }

    private static bool IsListedStationType(ConveyorPartMotion station) =>
        station != null;

    private static Color GetStationFill(ConveyorPartMotion station)
    {
        if (station is JackTransferPartMotion) return JackFill;
        if (station is LifterPartMotion) return LifterFill;
        if (station is ConveyorPartMotion) return PartFill;
        return OtherFill;
    }

    private static string GetStationTypeLabel(ConveyorPartMotion station)
    {
        if (station is JackTransferPartMotion) return "JackTransferPartMotion（顶升移栽）";
        if (station is LifterPartMotion) return "LifterPartMotion（提升机）";
        if (station is ConveyorPartMotion) return "ConveyorPartMotion（输送）";
        return station.GetType().Name;
    }

    private GUIStyle GetLabelStyle()
    {
        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = 10,
                fontStyle = FontStyle.Bold
            };
            _labelStyle.normal.textColor = new Color(0.92f, 0.94f, 0.96f, 0.95f);
        }

        return _labelStyle;
    }

    private GUIStyle GetAxisStyle()
    {
        if (_axisStyle == null)
        {
            _axisStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };
            _axisStyle.normal.textColor = AxisOverlay;
        }

        return _axisStyle;
    }

    private static void DrawArrow(Vector2 from, Vector2 to, Color color, float size)
    {
        Vector2 delta = to - from;
        if (delta.sqrMagnitude < 1e-4f) return;

        Handles.color = color;
        Handles.DrawAAPolyLine(2.5f, from, to);
        DrawArrowHead(from, to, color, size);
    }

    private static void DrawArrowHead(Vector2 from, Vector2 to, Color color, float size)
    {
        Vector2 delta = to - from;
        if (delta.sqrMagnitude < 1e-4f) return;

        Vector2 dir = delta.normalized;
        Vector2 tip = to;
        Vector2 orth = new Vector2(-dir.y, dir.x) * (size * 0.45f);
        Vector2 basePoint = tip - dir * size;

        Handles.color = color;
        Handles.DrawAAConvexPolygon(tip, basePoint + orth, basePoint - orth);
        Handles.DrawAAPolyLine(2f, tip, basePoint + orth);
        Handles.DrawAAPolyLine(2f, tip, basePoint - orth);
    }

    private static void DrawFlowChevrons(Vector2 from, Vector2 to, Color color, float size)
    {
        Vector2 delta = to - from;
        float len = delta.magnitude;
        if (len < 36f) return;

        Vector2 dir = delta / len;
        Vector2 orth = new Vector2(-dir.y, dir.x);
        float spacing = Mathf.Clamp(len * 0.28f, 28f, 52f);
        int count = Mathf.Clamp(Mathf.FloorToInt((len - 24f) / spacing), 1, 4);

        Handles.color = color;
        for (int i = 1; i <= count; i++)
        {
            float t = (i / (count + 1f));
            // 避开两端箭头区域
            t = Mathf.Lerp(0.22f, 0.78f, t);
            Vector2 center = from + delta * t;
            Vector2 tip = center + dir * (size * 0.55f);
            Vector2 left = center - dir * (size * 0.35f) + orth * (size * 0.45f);
            Vector2 right = center - dir * (size * 0.35f) - orth * (size * 0.45f);
            Handles.DrawAAPolyLine(2.2f, left, tip, right);
        }
    }
}
}
