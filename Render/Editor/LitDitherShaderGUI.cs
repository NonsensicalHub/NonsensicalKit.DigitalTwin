using System;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace NonsensicalKit.DigitalTwin.Editor.Render
{
    /// <summary>
    /// URP Lit 材质面板；Dither Visibility 固定在 Surface Options 顶部（兼容 Unity 6）。
    /// </summary>
    public sealed class LitDitherShaderGUI : BaseShaderGUI
    {
        private static class Styles
        {
            public static readonly GUIContent DitherVisibility = EditorGUIUtility.TrTextContent(
                "Dither Visibility",
                "显隐因子 [0,1]。0 全隐，1 全显，中间为 Bayer 网点渐隐（非 Alpha 混合）。与贴图 Alpha、Base Color Alpha、实例显隐相乘。");

            public static readonly GUIContent DitherActiveHint = EditorGUIUtility.TrTextContent(
                "当前处于 Bayer 渐隐区间，材质仍按不透明队列渲染。");

            public static readonly GUIContent DetailInputsLabel = EditorGUIUtility.TrTextContent("Detail Inputs",
                "These settings define the surface details by tiling and overlaying additional maps on the surface.");

            public static readonly GUIContent DetailMaskText = EditorGUIUtility.TrTextContent("Mask",
                "Select a mask for the Detail map. The mask uses the alpha channel of the selected texture.");

            public static readonly GUIContent DetailAlbedoMapText = EditorGUIUtility.TrTextContent("Base Map",
                "Select the surface detail texture.The alpha of your texture determines surface hue and intensity.");

            public static readonly GUIContent DetailNormalMapText = EditorGUIUtility.TrTextContent("Normal Map",
                "Designates a Normal Map to create the illusion of bumps and dents in the details of this Material's surface.");

            public static readonly GUIContent DetailAlbedoMapScaleInfo = EditorGUIUtility.TrTextContent(
                "Setting the scaling factor to a value other than 1 results in a less performant shader variant.");

            public static readonly GUIContent DetailAlbedoMapFormatError = EditorGUIUtility.TrTextContent(
                "This texture is not in linear space.");
        }

        private const float DitherVisibilityMin = 0f;
        private const float DitherVisibilityMax = 1f;

        private static readonly string[] WorkflowModeNames = Enum.GetNames(typeof(LitGUI.WorkflowMode));

        private LitGUI.LitProperties _litProperties;
        private MaterialProperty _detailMask;
        private MaterialProperty _detailAlbedoMapScale;
        private MaterialProperty _detailAlbedoMap;
        private MaterialProperty _detailNormalMapScale;
        private MaterialProperty _detailNormalMap;
        private MaterialProperty _ditherVisibility;

        public override void FillAdditionalFoldouts(MaterialHeaderScopeList materialScopesList)
        {
            materialScopesList.RegisterHeaderScope(Styles.DetailInputsLabel, Expandable.Details, _ => DrawDetailArea());
        }

        public override void FindProperties(MaterialProperty[] properties)
        {
            base.FindProperties(properties);
            _litProperties = new LitGUI.LitProperties(properties);
            _detailMask = FindProperty("_DetailMask", properties, false);
            _detailAlbedoMapScale = FindProperty("_DetailAlbedoMapScale", properties, false);
            _detailAlbedoMap = FindProperty("_DetailAlbedoMap", properties, false);
            _detailNormalMapScale = FindProperty("_DetailNormalMapScale", properties, false);
            _detailNormalMap = FindProperty("_DetailNormalMap", properties, false);
            _ditherVisibility = FindProperty("_DitherVisibility", properties, false);
        }

        public override void ValidateMaterial(Material material)
        {
            ClampDitherVisibility(material);
            SetMaterialKeywords(material, LitGUI.SetMaterialKeywords, SetDetailMaterialKeywords);
        }

        public override void DrawSurfaceOptions(Material material)
        {
            EditorGUIUtility.labelWidth = 0f;

            if (_litProperties.workflowMode != null)
            {
                DoPopup(LitGUI.Styles.workflowModeText, _litProperties.workflowMode, WorkflowModeNames);
            }

            DrawDitherVisibility(material);

            base.DrawSurfaceOptions(material);
        }

        public override void DrawSurfaceInputs(Material material)
        {
            base.DrawSurfaceInputs(material);
            LitGUI.Inputs(_litProperties, materialEditor, material);
            DrawEmissionProperties(material, true);
            DrawTileOffset(materialEditor, baseMapProp);
        }

        public override void DrawAdvancedOptions(Material material)
        {
            if (_litProperties.reflections != null && _litProperties.highlights != null)
            {
                materialEditor.ShaderProperty(_litProperties.highlights, LitGUI.Styles.highlightsText);
                materialEditor.ShaderProperty(_litProperties.reflections, LitGUI.Styles.reflectionsText);
            }

            base.DrawAdvancedOptions(material);
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            if (material.HasProperty("_Emission"))
            {
                material.SetColor("_EmissionColor", material.GetColor("_Emission"));
            }

            base.AssignNewShaderToMaterial(material, oldShader, newShader);

            if (oldShader == null || !oldShader.name.Contains("Legacy Shaders/"))
            {
                SetupMaterialBlendMode(material);
                return;
            }

            SurfaceType surfaceType = SurfaceType.Opaque;
            BlendMode blendMode = BlendMode.Alpha;
            if (oldShader.name.Contains("/Transparent/Cutout/"))
            {
                surfaceType = SurfaceType.Opaque;
                material.SetFloat("_AlphaClip", 1);
            }
            else if (oldShader.name.Contains("/Transparent/"))
            {
                surfaceType = SurfaceType.Transparent;
                blendMode = BlendMode.Alpha;
            }

            material.SetFloat("_Blend", (float)blendMode);
            material.SetFloat("_Surface", (float)surfaceType);
            if (surfaceType == SurfaceType.Opaque)
            {
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            else
            {
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }

            if (oldShader.name.Equals("Standard (Specular setup)"))
            {
                material.SetFloat("_WorkflowMode", (float)LitGUI.WorkflowMode.Specular);
                Texture texture = material.GetTexture("_SpecGlossMap");
                if (texture != null)
                {
                    material.SetTexture("_MetallicSpecGlossMap", texture);
                }
            }
            else
            {
                material.SetFloat("_WorkflowMode", (float)LitGUI.WorkflowMode.Metallic);
                Texture texture = material.GetTexture("_MetallicGlossMap");
                if (texture != null)
                {
                    material.SetTexture("_MetallicSpecGlossMap", texture);
                }
            }
        }

        private void DrawDitherVisibility(Material material)
        {
            if (_ditherVisibility == null)
            {
                return;
            }

            EditorGUILayout.Space(4);

            materialEditor.BeginAnimatedCheck(_ditherVisibility);
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = _ditherVisibility.hasMixedValue;
            float value = EditorGUILayout.Slider(
                Styles.DitherVisibility,
                _ditherVisibility.floatValue,
                DitherVisibilityMin,
                DitherVisibilityMax);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
            {
                materialEditor.RegisterPropertyChangeUndo(Styles.DitherVisibility.text);
                _ditherVisibility.floatValue = Mathf.Clamp(value, DitherVisibilityMin, DitherVisibilityMax);
            }

            materialEditor.EndAnimatedCheck();

            float visibility = Mathf.Clamp(_ditherVisibility.floatValue, DitherVisibilityMin, DitherVisibilityMax);
            if (visibility > DitherVisibilityMin && visibility < DitherVisibilityMax)
            {
                EditorGUILayout.HelpBox(Styles.DitherActiveHint.text, MessageType.Info, false);
            }

            EditorGUILayout.Space(2);
        }

        private static void ClampDitherVisibility(Material material)
        {
            if (!material.HasProperty("_DitherVisibility"))
            {
                return;
            }

            float value = material.GetFloat("_DitherVisibility");
            float clamped = Mathf.Clamp(value, DitherVisibilityMin, DitherVisibilityMax);
            if (!Mathf.Approximately(value, clamped))
            {
                material.SetFloat("_DitherVisibility", clamped);
            }
        }

        private void DrawDetailArea()
        {
            if (_detailMask == null)
            {
                return;
            }

            materialEditor.TexturePropertySingleLine(Styles.DetailMaskText, _detailMask);
            materialEditor.TexturePropertySingleLine(
                Styles.DetailAlbedoMapText,
                _detailAlbedoMap,
                _detailAlbedoMap.textureValue != null ? _detailAlbedoMapScale : null);

            if (_detailAlbedoMapScale != null && _detailAlbedoMapScale.floatValue != 1.0f)
            {
                EditorGUILayout.HelpBox(Styles.DetailAlbedoMapScaleInfo.text, MessageType.Info, true);
            }

            var detailAlbedoTexture = _detailAlbedoMap.textureValue as Texture2D;
            if (detailAlbedoTexture != null &&
                GraphicsFormatUtility.IsSRGBFormat(detailAlbedoTexture.graphicsFormat))
            {
                EditorGUILayout.HelpBox(Styles.DetailAlbedoMapFormatError.text, MessageType.Warning, true);
            }

            materialEditor.TexturePropertySingleLine(
                Styles.DetailNormalMapText,
                _detailNormalMap,
                _detailNormalMap.textureValue != null ? _detailNormalMapScale : null);
            materialEditor.TextureScaleOffsetProperty(_detailAlbedoMap);
        }

        private static void SetDetailMaterialKeywords(Material material)
        {
            if (!material.HasProperty("_DetailAlbedoMap") || !material.HasProperty("_DetailNormalMap") ||
                !material.HasProperty("_DetailAlbedoMapScale"))
            {
                return;
            }

            bool isScaled = material.GetFloat("_DetailAlbedoMapScale") != 1.0f;
            bool hasDetailMap = material.GetTexture("_DetailAlbedoMap") || material.GetTexture("_DetailNormalMap");
            CoreUtils.SetKeyword(material, "_DETAIL_MULX2", !isScaled && hasDetailMap);
            CoreUtils.SetKeyword(material, "_DETAIL_SCALED", isScaled && hasDetailMap);
        }
    }
}
