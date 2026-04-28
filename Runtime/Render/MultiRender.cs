using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.Tools;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace NonsensicalKit.DigitalTwin.Render
{
    public class MultiRender : NonsensicalMono
    {
        [SerializeField] private List<RenderSetting> m_settings;
        [SerializeField] private ReflectionProbe m_probe;
        [SerializeField] private bool m_refreshTrans;

        public bool RefreshTrans
        {
            get=> m_refreshTrans;
            set => m_refreshTrans = value;
        }
        
        private void Awake()
        {
            Init();
            AddListener<bool>("isInAnimationTiming", RealTimeRefreshTrans);
            AddListener<bool>("realTimeModelRefreshTrans", RealTimeRefreshTrans);
        }


        private void Update()
        {
            if (m_refreshTrans) RefreshTransMatrix();
            Render();
        }

        public void RealTimeRefreshTrans(bool enable)
        {
            m_refreshTrans = enable;
        }

        private void Init()
        {
            if (m_settings == null)
            {
                return;
            }

            foreach (var setting in m_settings)
            {
                foreach (var VARIABLE in setting.m_LoadTrans)
                {
                    if (VARIABLE == false)
                    {
                        Debug.Log("配置为空");
                        continue;
                    }

                    MeshRenderer[] ms = VARIABLE.GetComponentsInChildren<MeshRenderer>();

                    foreach (var mr in ms)
                    {
                        mr.enabled = false;
                    }
                }

                setting.Init(m_probe);
            }
        }

        private void Render()
        {
            if (m_settings == null)
            {
                return;
            }

            foreach (var setting in m_settings)
            {
                setting.RenderMesh();
            }
        }

        private void RefreshTransMatrix()
        {
            if (m_settings == null)
            {
                return;
            }

            foreach (var setting in m_settings)
            {
                setting.Update();
            }
        }
        
#if UNITY_EDITOR
        //自己遍历子节点进行配置
        [Button]
        public void SmartSetting()
        {
            Dictionary<string, RenderSetting> dict = new Dictionary<string, RenderSetting>();

            var ts = transform.GetComponentsInChildren<MeshRenderer>().ToList();

            foreach (var mr in ts)
            {
                if (mr.enabled == false)
                    continue;
                var mf = mr.gameObject.GetComponent<MeshFilter>();
                if (mf == null)
                {
                    continue;
                }

                var v = mr.GetComponentInParent<MeshRenderer>();
                if (v != null && v != mr)
                {
                    continue;
                }

                string matNames = mf.sharedMesh.name + "|";

                foreach (var m in mr.sharedMaterials)
                {
                    matNames += m.name + "|";
                    m.enableInstancing = true;
                }

                if (dict.ContainsKey(matNames) == false)
                {
                    dict.Add(matNames, new RenderSetting());
                }

                dict[matNames].m_LoadTrans.Add(mr.gameObject.transform);
            }


            m_settings = new List<RenderSetting>();

            foreach (var VARIABLE in dict)
            {
                var zero = VARIABLE.Value.m_LoadTrans[0].gameObject;
                bool isPrefabRoot = PrefabUtility.IsAnyPrefabInstanceRoot(zero);
                if (isPrefabRoot)
                {
                    var prefabRoot = PrefabUtility.GetCorrespondingObjectFromSource(zero);
                    VARIABLE.Value.m_Prefab = prefabRoot;
                }
                else
                {
                    var newZero = Instantiate(VARIABLE.Value.m_LoadTrans[0].gameObject);
                    foreach (Transform child in zero.transform)
                    {
                        DestroyImmediate(child.gameObject);
                    }

                    FileTool.EnsureDir(Application.dataPath + "/Prefabs/Auto");
                    var newPrefab =
                        PrefabUtility.SaveAsPrefabAsset(newZero, "Assets/Prefabs/Auto/" + zero.name + ".prefab");
                    VARIABLE.Value.m_Prefab = newPrefab;
                    DestroyImmediate(newZero);
                }

                m_settings.Add(VARIABLE.Value);
            }
            
            AssetDatabase.SaveAssets();
        }

        //刷新配置位置数据
        [Button]
        public void RefreshSetting()
        {
            RefreshTransMatrix();
        }
        
        //清理空对象
        [Button]
        private void ClearNullTrans()
        {
            if (m_settings == null)
            {
                return;
            }

            for (int i = 0; i < m_settings.Count; i++)
            {
                if (m_settings[i].m_Prefab == null)
                {
                    m_settings.RemoveAt(i);
                    i--;
                    continue;
                }

                if (i < 0 || i >= m_settings.Count || m_settings[i] == null)
                {
                    continue;
                }

                for (int j = 0; j < m_settings[i].m_LoadTrans.Count; j++)
                {
                    if (m_settings[i].m_LoadTrans[j] == null)
                    {
                        m_settings[i].m_LoadTrans.RemoveAt(j);
                        j--;
                    }
                }
            }
        }
#endif
    }

    [Serializable]
    public class RenderSetting
    {
        public GameObject m_Prefab;
        public List<Transform> m_LoadTrans = new List<Transform>();
        private List<PartInfo> _parts;
        private readonly List<Matrix4x4> _transCache = new List<Matrix4x4>();

        public void Init(ReflectionProbe mainReflectionProbe = null)
        {
            InitMeshes(m_Prefab, mainReflectionProbe);
            Update();
        }

        public void RenderMesh()
        {
            //Debug.Log(m_Prefab.name,m_Prefab.transform);
            foreach (var t in _parts)
            {
                foreach (var item in t.Trans)
                {
                    if (item == null) continue;
                    Graphics.RenderMeshInstanced(t.RenderParams, t.Mesh, t.SubMeshCount, item);
                }
            }
        }

        public void Update()
        {
            _transCache.Clear();
            foreach (var t in m_LoadTrans)
            {
                if (t == null)
                {
                    continue;
                }

                if (t.gameObject.activeInHierarchy)
                {
                    _transCache.Add(t.localToWorldMatrix);
                }
            }

            foreach (var item in _parts)
            {
                item.UpdateTrans(_transCache);
            }
        }

        private void InitMeshes(GameObject prefab, ReflectionProbe mainReflectionProbe = null)
        {
            _parts = new List<PartInfo>();
            var meshs = prefab.GetComponentsInChildren<MeshFilter>();
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            if (mainReflectionProbe != null)
            {
                block.SetTexture("unity_SpecCube0", mainReflectionProbe.texture);
                block.SetTexture("unity_SpecCube1", mainReflectionProbe.texture);
                var textureHDRDecodeValues = mainReflectionProbe.textureHDRDecodeValues;
                block.SetVector("unity_SpecCube0_HDR", textureHDRDecodeValues);
                block.SetVector("unity_SpecCube1_HDR", textureHDRDecodeValues);
            }

            foreach (var item in meshs)
            {
                if (!item.gameObject.TryGetComponent<MeshRenderer>(out var renderer)) continue;
                if (item.sharedMesh.subMeshCount < renderer.sharedMaterials.Length)
                {
                    return;
                }

                for (var i = 0; i < renderer.sharedMaterials.Length; i++)
                {
                    var renderParams = new RenderParams(renderer.sharedMaterials[i])
                    {
                        shadowCastingMode = ShadowCastingMode.On,
                        reflectionProbeUsage = ReflectionProbeUsage.BlendProbes,
                        lightProbeUsage = LightProbeUsage.Off,
                        matProps = block,
                        layer = 8
                    };

                    _parts.Add(new PartInfo(renderParams, item.sharedMesh, i,
                        prefab.transform.worldToLocalMatrix * item.transform.localToWorldMatrix));
                }
            }
        }
    }

    public class PartInfo
    {
        public RenderParams RenderParams;
        public Mesh Mesh;
        public int SubMeshCount;
        public Matrix4x4 Offset;

        public Matrix4x4[][] Trans;

        public PartInfo(RenderParams renderParams, Mesh mesh, int subMeshCount, Matrix4x4 offset)
        {
            RenderParams = renderParams;
            Mesh = mesh;
            SubMeshCount = subMeshCount;
            Offset = offset;
        }

        public void UpdateTrans(List<Matrix4x4> trans)
        {
            int totalCount = trans.Count;
            if (totalCount <= 0)
            {
                Trans = Array.Empty<Matrix4x4[]>();
                return;
            }

            int patchCount = (totalCount - 1) / 1023 + 1;
            if (Trans == null || Trans.Length != patchCount)
            {
                Trans = new Matrix4x4[patchCount][];
            }

            for (int patchIndex = 0; patchIndex < patchCount; patchIndex++)
            {
                int chunkStart = patchIndex * 1023;
                int chunkLength = Mathf.Min(1023, totalCount - chunkStart);
                if (Trans[patchIndex] == null || Trans[patchIndex].Length != chunkLength)
                {
                    Trans[patchIndex] = new Matrix4x4[chunkLength];
                }

                for (int i = 0; i < chunkLength; i++)
                {
                    Trans[patchIndex][i] = trans[chunkStart + i] * Offset;
                }
            }
        }
    }
}