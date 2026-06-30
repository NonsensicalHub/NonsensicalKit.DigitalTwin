#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Warehouse.GpuPicking.Editor
{
    /// <summary>
    /// 将 <c>Warehouse/GpuPicking/WarehousePick.shader</c> 加入 Always Included Shaders（若尚未包含）。
    /// </summary>
    [InitializeOnLoad]
    internal static class WarehouseGpuPickShaderInclude
    {
        private const string PickShaderGuid = "fe302afe69d35a04ba3fc76dd9370277";

        static WarehouseGpuPickShaderInclude()
        {
            EditorApplication.delayCall += EnsureShaderAlwaysIncluded;
        }

        private static void EnsureShaderAlwaysIncluded()
        {
            string shaderPath = AssetDatabase.GUIDToAssetPath(PickShaderGuid);
            if (string.IsNullOrEmpty(shaderPath))
            {
                return;
            }

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null)
            {
                return;
            }

            Object[] graphicsSettingsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (graphicsSettingsAssets == null || graphicsSettingsAssets.Length == 0)
            {
                return;
            }

            SerializedObject serializedObject = new SerializedObject(graphicsSettingsAssets[0]);
            SerializedProperty alwaysIncluded = serializedObject.FindProperty("m_AlwaysIncludedShaders");
            if (alwaysIncluded == null)
            {
                return;
            }

            for (int i = 0; i < alwaysIncluded.arraySize; i++)
            {
                if (alwaysIncluded.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                {
                    return;
                }
            }

            alwaysIncluded.InsertArrayElementAtIndex(alwaysIncluded.arraySize);
            alwaysIncluded.GetArrayElementAtIndex(alwaysIncluded.arraySize - 1).objectReferenceValue = shader;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
