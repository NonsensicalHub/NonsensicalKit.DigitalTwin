using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WarehouseManagerLayoutEditor))]
public sealed class WarehouseManagerLayoutEditorInspector : Editor
{
    private SerializedProperty _warehouseManager;
    private SerializedProperty _warehouseFileName;
    private SerializedProperty _layoutOrigin;
    private SerializedProperty _rowDirection;
    private SerializedProperty _layerDirection;
    private SerializedProperty _columnDirection;
    private SerializedProperty _posRow;
    private SerializedProperty _posLayer;
    private SerializedProperty _posColumn;
    private SerializedProperty _depthCount;
    private SerializedProperty _depthDirectionMode;
    private SerializedProperty _depthDirection;
    private SerializedProperty _evenColumnDepthDirection;
    private SerializedProperty _oddColumnDepthDirection;
    private SerializedProperty _autoApplyAtRuntime;
    private SerializedProperty _verboseLog;
    private SerializedProperty _focusMode;
    private SerializedProperty _focusStyle;
    private SerializedProperty _focusIndex;
    private SerializedProperty _focusPinRow;
    private SerializedProperty _focusPinColumn;
    private SerializedProperty _focusPinLayer;
    private SerializedProperty _focusGizmosOnly;
    private SerializedProperty _disableRules;
    private SerializedProperty _uniformOrigin;
    private SerializedProperty _uniformSpacing;
    private SerializedProperty _uniformCount;
    private SerializedProperty _defaultDepthDirection;
    private SerializedProperty _axisOpTarget;
    private SerializedProperty _axisTranslateDelta;
    private SerializedProperty _offset;
    private SerializedProperty _drawGizmos;
    private SerializedProperty _gizmoSize;
    private SerializedProperty _gizmoColor;

    private void OnEnable()
    {
        _warehouseManager = serializedObject.FindProperty("m_warehouseManager");
        _warehouseFileName = serializedObject.FindProperty("m_warehouseFileName");
        _layoutOrigin = serializedObject.FindProperty("m_layoutOrigin");
        _rowDirection = serializedObject.FindProperty("m_rowDirection");
        _layerDirection = serializedObject.FindProperty("m_layerDirection");
        _columnDirection = serializedObject.FindProperty("m_columnDirection");
        _posRow = serializedObject.FindProperty("m_posRow");
        _posLayer = serializedObject.FindProperty("m_posLayer");
        _posColumn = serializedObject.FindProperty("m_posColumn");
        _depthCount = serializedObject.FindProperty("m_depthCount");
        _depthDirectionMode = serializedObject.FindProperty("m_depthDirectionMode");
        _depthDirection = serializedObject.FindProperty("m_depthDirection");
        _evenColumnDepthDirection = serializedObject.FindProperty("m_evenColumnDepthDirection");
        _oddColumnDepthDirection = serializedObject.FindProperty("m_oddColumnDepthDirection");
        _autoApplyAtRuntime = serializedObject.FindProperty("m_autoApplyAtRuntime");
        _verboseLog = serializedObject.FindProperty("m_verboseLog");
        _focusMode = serializedObject.FindProperty("m_focusMode");
        _focusStyle = serializedObject.FindProperty("m_focusStyle");
        _focusIndex = serializedObject.FindProperty("m_focusIndex");
        _focusPinRow = serializedObject.FindProperty("m_focusPinRow");
        _focusPinColumn = serializedObject.FindProperty("m_focusPinColumn");
        _focusPinLayer = serializedObject.FindProperty("m_focusPinLayer");
        _focusGizmosOnly = serializedObject.FindProperty("m_focusGizmosOnly");
        _disableRules = serializedObject.FindProperty("m_disableRules");
        _uniformOrigin = serializedObject.FindProperty("m_uniformOrigin");
        _uniformSpacing = serializedObject.FindProperty("m_uniformSpacing");
        _uniformCount = serializedObject.FindProperty("m_uniformCount");
        _defaultDepthDirection = serializedObject.FindProperty("m_defaultDepthDirection");
        _axisOpTarget = serializedObject.FindProperty("m_axisOpTarget");
        _axisTranslateDelta = serializedObject.FindProperty("m_axisTranslateDelta");
        _offset = serializedObject.FindProperty("m_offset");
        _drawGizmos = serializedObject.FindProperty("m_drawGizmos");
        _gizmoSize = serializedObject.FindProperty("m_gizmoSize");
        _gizmoColor = serializedObject.FindProperty("m_gizmoColor");
    }

    public override void OnInspectorGUI()
    {
        var editor = (WarehouseManagerLayoutEditor)target;
        serializedObject.Update();

        DrawTitle(editor);
        DrawTargetAndIO(editor);
        DrawAxes(editor);
        DrawDisableRules(editor);
        DrawFocus(editor);
        DrawAxisOps(editor);
        DrawUniformGenerate(editor);
        DrawGizmos();
        DrawDangerZone(editor);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawTitle(WarehouseManagerLayoutEditor editor)
    {
        EditorGUILayout.Space(2f);
        EditorGUILayout.LabelField("货位布局编辑器", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "世界坐标 = 原点 + 排标量×排方向 + 层标量×层方向 + 列标量×列方向。\n" +
            $"当前尺寸：排 {editor.RowCount} / 层 {editor.LayerCount} / 列 {editor.ColumnCount} / 深 {editor.DepthAxisCount}\n" +
            (Application.isPlaying
                ? "Play 中：改参可热刷新货物位置（尺寸变更需先保存并重进 Play）。"
                : "Edit 中：可改轴并保存 .dat；进 Play 后才能预览货物位置。"),
            MessageType.Info);
    }

    private void DrawTargetAndIO(WarehouseManagerLayoutEditor editor)
    {
        if (!BeginFoldout("wk.layout.target", "1. 目标与文件", true))
        {
            return;
        }

        EditorGUILayout.PropertyField(_warehouseManager, new GUIContent("仓库管理器"));
        EditorGUILayout.PropertyField(_warehouseFileName, new GUIContent("仓库文件名"));
        EditorGUILayout.PropertyField(_autoApplyAtRuntime, new GUIContent("改参后自动应用到货物"));
        EditorGUILayout.PropertyField(_verboseLog, new GUIContent("详细日志（拖参时建议关）"));

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("从文件加载", GUILayout.Height(24f)))
            {
                Invoke(editor, "从文件加载轴参数", editor.LoadFromFile);
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("应用到运行时", GUILayout.Height(24f)))
                {
                    Invoke(editor, "应用到运行时货物位置", editor.ApplyToRuntime);
                }
            }

            if (GUILayout.Button("保存 .dat", GUILayout.Height(24f)))
            {
                if (EditorUtility.DisplayDialog(
                        "保存仓库布局",
                        $"将覆盖 StreamingAssets/Warehouse/{_warehouseFileName.stringValue}.dat，确定？",
                        "保存",
                        "取消"))
                {
                    Invoke(editor, "保存到 StreamingAssets", editor.SaveToFile);
                }
            }
        }

        EndFoldout();
    }

    private void DrawAxes(WarehouseManagerLayoutEditor editor)
    {
        if (!BeginFoldout("wk.layout.axes", "2. 轴坐标", true))
        {
            return;
        }

        EditorGUILayout.PropertyField(_layoutOrigin, new GUIContent("布局原点"));
        EditorGUILayout.PropertyField(
            _rowDirection,
            new GUIContent("排方向", "世界空间方向，不强制归一化；负向量即反向。"));
        EditorGUILayout.PropertyField(
            _layerDirection,
            new GUIContent("层方向", "世界空间方向，不强制归一化；负向量即反向。"));
        EditorGUILayout.PropertyField(
            _columnDirection,
            new GUIContent("列方向", "世界空间方向，不强制归一化；负向量即反向。"));

        EditorGUILayout.Space(2f);
        EditorGUILayout.PropertyField(_posRow, new GUIContent("排标量 (Row)"), true);
        EditorGUILayout.PropertyField(_posLayer, new GUIContent("层标量 (Layer)"), true);
        EditorGUILayout.PropertyField(_posColumn, new GUIContent("列标量 (Column)"), true);
        EditorGUILayout.PropertyField(
            _depthCount,
            new GUIContent("深 Depth 数量", "索引维度，不改变货位世界坐标；供堆垛机等判断叉子伸出档位。"));
        EditorGUILayout.PropertyField(
            _depthDirectionMode,
            new GUIContent("深度配置模式", "深度方向仅写入 .dat 供设备使用，不参与坐标计算。"));

        EditorGUILayout.HelpBox(
            "深度是索引，不是空间轴：同排/层/列下各 Depth 共用同一坐标。\n" +
            "深度方向配置保存后供堆垛机等设备判断叉伸方向。",
            MessageType.Info);

        if (editor.IsDetailedDepthMode)
        {
            EditorGUILayout.PropertyField(
                _depthDirection,
                new GUIContent("列深度方向", "每列叉伸方向（设备元数据），不参与货位坐标。"),
                true);
            EditorGUILayout.PropertyField(_defaultDepthDirection, new GUIContent("默认深度方向"));
            if (GUILayout.Button("用默认深度方向填充所有列"))
            {
                Invoke(editor, "填充列深度方向", editor.FillDepthDirectionsWithDefault);
            }
        }
        else if (editor.IsOddEvenDepthMode)
        {
            EditorGUILayout.PropertyField(
                _evenColumnDepthDirection,
                new GUIContent("偶数列深度方向", "设备元数据，不参与坐标。"));
            EditorGUILayout.PropertyField(
                _oddColumnDepthDirection,
                new GUIContent("奇数列深度方向", "设备元数据，不参与坐标。"));
        }

        EndFoldout();
    }

    private void DrawDisableRules(WarehouseManagerLayoutEditor editor)
    {
        if (!BeginFoldout("wk.layout.disable", "3. 禁用货位规则", true))
        {
            return;
        }

        EditorGUILayout.HelpBox(
            "多条规则为「或」。单条内已勾选的轴约束为「且」。\n" +
            "例：第一排下面 3 层无货 → 勾选「约束排」0~0，勾选「约束层」0~2。\n" +
            "禁用货位不显示，正式运行时也无法再打开。保存后写入 .dat。",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(_disableRules, new GUIContent("规则列表"), true);
        bool rulesChanged = EditorGUI.EndChangeCheck();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("添加：第一排下层示例"))
            {
                AddExampleFirstRowLowerLayers();
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("立即应用禁用规则"))
                {
                    Invoke(editor, "应用禁用规则", editor.ApplyDisableRulesNow);
                }
            }
        }

        if (rulesChanged && Application.isPlaying)
        {
            serializedObject.ApplyModifiedProperties();
            editor.ApplyDisableRulesNow();
        }

        EndFoldout();
    }

    private void AddExampleFirstRowLowerLayers()
    {
        int index = _disableRules.arraySize;
        _disableRules.arraySize = index + 1;
        SerializedProperty rule = _disableRules.GetArrayElementAtIndex(index);
        rule.FindPropertyRelative("Enabled").boolValue = true;
        rule.FindPropertyRelative("ConstrainRow").boolValue = true;
        rule.FindPropertyRelative("RowMin").intValue = 0;
        rule.FindPropertyRelative("RowMax").intValue = 0;
        rule.FindPropertyRelative("ConstrainColumn").boolValue = false;
        rule.FindPropertyRelative("ConstrainLevel").boolValue = true;
        rule.FindPropertyRelative("LevelMin").intValue = 0;
        rule.FindPropertyRelative("LevelMax").intValue = 2;
        rule.FindPropertyRelative("ConstrainDepth").boolValue = false;
        serializedObject.ApplyModifiedProperties();
    }

    private void DrawFocus(WarehouseManagerLayoutEditor editor)
    {
        if (!BeginFoldout("wk.layout.focus", "4. 专注模式", true))
        {
            return;
        }

        EditorGUILayout.PropertyField(_focusMode, new GUIContent("专注轴"));

        if (!editor.IsFocusModeOn)
        {
            EditorGUILayout.HelpBox("关闭时显示全部货物。选择层/列/排后可进入切片或整轴专注。", MessageType.None);
            EndFoldout();
            return;
        }

        EditorGUILayout.PropertyField(_focusStyle, new GUIContent("专注样式"));
        EditorGUILayout.PropertyField(_focusGizmosOnly, new GUIContent("Gizmo 也只画专注范围"));

        EditorGUILayout.Space(2f);
        EditorGUILayout.HelpBox(editor.DescribeFocus(), MessageType.None);

        if (editor.IsFocusSlice)
        {
            int max = GetSliceMax(editor);
            DrawIntSlider(_focusIndex, "切片索引", max);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("← 上一项"))
                {
                    Invoke(editor, "切片上一项", editor.FocusPrevious);
                }

                if (GUILayout.Button("下一项 →"))
                {
                    Invoke(editor, "切片下一项", editor.FocusNext);
                }
            }
        }
        else if (editor.IsFocusAxisStack)
        {
            EditorGUILayout.LabelField("固定探针（整轴展开目标轴）", EditorStyles.miniBoldLabel);
            if (editor.ShowFocusPinRow)
            {
                DrawIntSlider(_focusPinRow, "固定排", Mathf.Max(0, editor.RowCount - 1));
                if (GUILayout.Button("切换固定排"))
                {
                    Invoke(editor, "切换固定排", editor.CycleFocusPinRow);
                }
            }

            if (editor.ShowFocusPinColumn)
            {
                DrawIntSlider(_focusPinColumn, "固定列", Mathf.Max(0, editor.ColumnCount - 1));
                if (GUILayout.Button("切换固定列"))
                {
                    Invoke(editor, "切换固定列", editor.CycleFocusPinColumn);
                }
            }

            if (editor.ShowFocusPinLayer)
            {
                DrawIntSlider(_focusPinLayer, "固定层", Mathf.Max(0, editor.LayerCount - 1));
                if (GUILayout.Button("切换固定层"))
                {
                    Invoke(editor, "切换固定层", editor.CycleFocusPinLayer);
                }
            }
        }

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("立即应用专注显隐", GUILayout.Height(22f)))
            {
                Invoke(editor, "应用专注显隐", editor.ApplyFocusNow);
            }
        }

        EndFoldout();
    }

    private void DrawAxisOps(WarehouseManagerLayoutEditor editor)
    {
        if (!BeginFoldout("wk.layout.axisOps", "5. 轴操作（翻转 / 平移）", true))
        {
            return;
        }

        EditorGUILayout.PropertyField(_axisOpTarget, new GUIContent("操作轴"));

        EditorGUILayout.Space(2f);
        EditorGUILayout.LabelField("翻转索引", EditorStyles.miniBoldLabel);
        EditorGUILayout.HelpBox(
            "将该轴标量数组首尾对调。世界坐标集合不变，仅索引与位置对调。翻列时同步处理深度方向。",
            MessageType.None);
        if (GUILayout.Button("翻转所选轴索引", GUILayout.Height(24f)))
        {
            Invoke(editor, "翻转轴索引", editor.FlipSelectedAxis);
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("整体平移", EditorStyles.miniBoldLabel);
        EditorGUILayout.PropertyField(_axisTranslateDelta, new GUIContent("单轴标量平移量"));
        if (GUILayout.Button("平移所选轴标量", GUILayout.Height(22f)))
        {
            Invoke(editor, "平移所选轴", editor.TranslateSelectedAxis);
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.PropertyField(_offset, new GUIContent("原点世界平移量 (XYZ)"));
        if (GUILayout.Button("平移布局原点", GUILayout.Height(22f)))
        {
            Invoke(editor, "平移布局原点", editor.ApplyOffsetToAxes);
        }

        EndFoldout();
    }

    private void DrawUniformGenerate(WarehouseManagerLayoutEditor editor)
    {
        if (!BeginFoldout("wk.layout.uniform", "6. 均匀生成辅助", false))
        {
            return;
        }

        EditorGUILayout.PropertyField(_uniformOrigin, new GUIContent("原点（写入布局原点）"));
        EditorGUILayout.PropertyField(
            _uniformSpacing,
            new GUIContent("标量间距 (排 / 层 / 列)", "可为负以实现反向排布。"));
        EditorGUILayout.PropertyField(_uniformCount, new GUIContent("数量 (排 / 层 / 列)"));
        if (editor.IsDetailedDepthMode)
        {
            EditorGUILayout.PropertyField(_defaultDepthDirection, new GUIContent("默认深度方向"));
        }

        EditorGUILayout.HelpBox(
            "生成标量 = 索引 × 间距；世界位置再乘以当前排/层/列方向。",
            MessageType.None);

        if (GUILayout.Button("按原点+间距生成轴坐标", GUILayout.Height(24f)))
        {
            Invoke(editor, "均匀生成轴坐标", editor.GenerateUniformAxes);
        }

        EndFoldout();
    }

    private void DrawGizmos()
    {
        if (!BeginFoldout("wk.layout.gizmo", "7. Gizmo", false))
        {
            return;
        }

        EditorGUILayout.PropertyField(_drawGizmos, new GUIContent("绘制货位预览"));
        EditorGUILayout.PropertyField(_gizmoSize, new GUIContent("预览盒尺寸"));
        EditorGUILayout.PropertyField(_gizmoColor, new GUIContent("预览颜色"));
        EndFoldout();
    }

    private void DrawDangerZone(WarehouseManagerLayoutEditor editor)
    {
        if (!BeginFoldout("wk.layout.danger", "8. 收尾", false))
        {
            return;
        }

        EditorGUILayout.HelpBox("保存 .dat 后移除本组件，避免进入正式流程。", MessageType.Warning);
        GUI.backgroundColor = new Color(1f, 0.55f, 0.45f);
        if (GUILayout.Button("保存并移除本组件", GUILayout.Height(28f)))
        {
            if (EditorUtility.DisplayDialog(
                    "保存并移除",
                    "将写入 StreamingAssets 并删除 WarehouseManagerLayoutEditor 组件，确定？",
                    "保存并移除",
                    "取消"))
            {
                Undo.RecordObject(editor, "保存并移除布局编辑器");
                editor.SaveBeforeRemove();
                if (editor != null)
                {
                    Undo.DestroyObjectImmediate(editor);
                }
            }
        }

        GUI.backgroundColor = Color.white;
        EndFoldout();
    }

    private static int GetSliceMax(WarehouseManagerLayoutEditor editor)
    {
        int count = editor.FocusMode switch
        {
            WarehouseManagerLayoutEditor.FocusAxisMode.Layer => editor.LayerCount,
            WarehouseManagerLayoutEditor.FocusAxisMode.Column => editor.ColumnCount,
            WarehouseManagerLayoutEditor.FocusAxisMode.Row => editor.RowCount,
            _ => 0,
        };
        return Mathf.Max(0, count - 1);
    }

    private static void DrawIntSlider(SerializedProperty property, string label, int max)
    {
        max = Mathf.Max(0, max);
        property.intValue = EditorGUILayout.IntSlider(label, property.intValue, 0, max);
    }

    private void Invoke(WarehouseManagerLayoutEditor editor, string undoName, System.Action action)
    {
        Undo.RecordObject(editor, undoName);
        action();
        if (editor != null)
        {
            EditorUtility.SetDirty(editor);
            serializedObject.Update();
            Repaint();
        }
    }

    private static GUIStyle s_sectionFoldoutStyle;

    private static GUIStyle SectionFoldoutStyle =>
        s_sectionFoldoutStyle ??= new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };

    private bool BeginFoldout(string key, string title, bool defaultOpen)
    {
        bool open = SessionState.GetBool(key, defaultOpen);
        // 不能用 BeginFoldoutHeaderGroup：轴数组 PropertyField 内部也会画 Foldout Header，会触发嵌套报错。
        open = EditorGUILayout.Foldout(open, title, true, SectionFoldoutStyle);
        SessionState.SetBool(key, open);
        if (open)
        {
            EditorGUI.indentLevel++;
        }

        return open;
    }

    private static void EndFoldout()
    {
        EditorGUI.indentLevel--;
        EditorGUILayout.Space(4f);
    }
}
