using System;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    /// <summary>
    /// 一次性并排验证多种绘制路径（含 <see cref="Graphics.RenderMeshInstanced"/> / <see cref="Graphics.DrawMeshInstanced"/> / <see cref="RenderObject"/> 等），便于 WebGL 等平台快速判断哪条可用。
    /// 挂到空物体上，指定 Mesh / 材质，面向相机摆放；Console 与屏幕左上角会输出能力与各行错误信息。
    /// </summary>
    [ExecuteAlways]
    public sealed class WarehouseRenderPathSmokeTest : MonoBehaviour
    {
        private enum LaneKind
        {
            DrawMeshLoop,
            DrawMeshInstancedSimple,
            DrawMeshInstancedWithPerInstanceBuffer,
            // Graphics.RenderMeshInstanced(RenderParams, Mesh, submesh, Matrix4x4[]) — 同 MultiRender.RenderMesh
            RenderMeshInstanced,
            RenderMeshIndirect,
            RenderObjectMatrixBatch,
            RenderObjectIndirect,
            RenderObjectCreate
        }

        [Header("资源（必填 Mesh；材质按行说明选填）")]
        [SerializeField]
        private Mesh _mesh;

        /// <summary>普通 GPU Instancing 材质（无 StructuredBuffer），用于验证基础实例化是否可用。</summary>
        [SerializeField]
        private Material _materialSimpleInstancing;

        /// <summary>仓库用材质（含 _PerInstanceItemData / procedural instancing）。可与 Simple 相同仅作占位，但 Simple 行更适合用 URP Lit 等。</summary>
        [SerializeField]
        private Material _materialWarehouse;

        [Header("各 Lane 专用材质（可选，用颜色区分路径；留空则用上方默认）")]
        [SerializeField]
        private Material _matLane_DrawMeshLoop;

        [SerializeField]
        private Material _matLane_DrawMeshInstancedSimple;

        [SerializeField]
        private Material _matLane_DrawMeshInstancedWithBuffer;

        [SerializeField]
        private Material _matLane_RenderMeshInstanced;

        [SerializeField]
        private Material _matLane_RenderMeshIndirect;

        [SerializeField]
        private Material _matLane_RenderObjectMatrixBatch;

        [SerializeField]
        private Material _matLane_RenderObjectIndirect;

        [SerializeField]
        private Material _matLane_RenderObjectCreate;

        [Header("布局")]
        [Tooltip("勾选：各 lane 沿世界 X 轴并排（默认摄像机下最易分辨）。关闭：lane 沿 Z、实例沿 X（易从透视看成叠在一起）。")]
        [SerializeField]
        private bool _lanesSpreadAlongWorldX = true;

        [SerializeField]
        private int _instancesPerRow = 8;

        [SerializeField]
        private float _spacingAlongRow = 1.2f;

        [SerializeField]
        private float _spacingBetweenLanes = 2.5f;

        [SerializeField]
        private Vector3 _firstInstanceOrigin = new Vector3(-15f, 0f, 0f);

        [Header("可选")]
        [SerializeField]
        private bool _logSystemInfoOnce = true;

        [SerializeField]
        private bool _showOnScreenHud = true;

        private readonly StringBuilder _hud = new StringBuilder(2048);
        private readonly string[] _laneErrors = new string[Enum.GetValues(typeof(LaneKind)).Length];

        /// <summary>初始化阶段（缓冲 / RenderObject 构造）失败信息，不在每帧清空。</summary>
        private readonly string[] _initLaneErrors = new string[Enum.GetValues(typeof(LaneKind)).Length];

        private GraphicsBuffer _indirectCommandBuf;
        private GraphicsBuffer.IndirectDrawIndexedArgs[] _indirectArgs;
        private ComputeBuffer _instanceDataBuf;
        private ComputeBuffer _identityStubBuf;

        private RenderObject _roMatrix;
        private RenderObject _roIndirect;
        private RenderObject _roCreate;

        /// <summary>运行帧计数：第 2 帧统一推送一次 <see cref="RenderObject.UpdateItems"/>（异步），之后仅 <see cref="RenderObject.Render"/>。</summary>
        private int _playFrame;

        private static readonly Bounds HugeBounds =
            new Bounds(Vector3.zero, new Vector3(100000f, 100000f, 100000f));

        private static readonly int PerInstanceItemDataId = Shader.PropertyToID("_PerInstanceItemData");

        private void OnEnable()
        {
            _playFrame = 0;
            ClearLaneErrors();
            ClearInitLaneErrors();
            TryInitBuffers();
            TryInitRenderObjects();
            if (_logSystemInfoOnce && Application.isPlaying)
            {
                LogSystemInfo();
            }
        }

        private void OnDisable()
        {
            ReleaseBuffers();
            ReleaseRenderObjects();
        }

        private void Update()
        {
            if (!Application.isPlaying || _mesh == null)
            {
                return;
            }

            _playFrame++;
            if (_playFrame == 2)
            {
                TryPushRenderObjectsOnce();
            }

            ClearLaneErrors();

            int lane = 0;
            RunLane(LaneKind.DrawMeshLoop, lane++);
            RunLane(LaneKind.DrawMeshInstancedSimple, lane++);
            RunLane(LaneKind.DrawMeshInstancedWithPerInstanceBuffer, lane++);
            RunLane(LaneKind.RenderMeshInstanced, lane++);
            RunLane(LaneKind.RenderMeshIndirect, lane++);
            RunLane(LaneKind.RenderObjectMatrixBatch, lane++);
            RunLane(LaneKind.RenderObjectIndirect, lane++);
            RunLane(LaneKind.RenderObjectCreate, lane++);

            if (_showOnScreenHud)
            {
                BuildHud();
            }
        }

        private void OnGUI()
        {
            if (!_showOnScreenHud || !Application.isPlaying)
            {
                return;
            }

            GUIStyle style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                richText = true
            };
            const float margin = 8f;
            GUI.Box(new Rect(margin, margin, Screen.width - margin * 2f, 400f), _hud.ToString(), style);
        }

        private void LogSystemInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[WarehouseRenderPathSmokeTest] SystemInfo");
            sb.AppendLine("supportsInstancing: " + SystemInfo.supportsInstancing);
            sb.AppendLine("supportsComputeShaders: " + SystemInfo.supportsComputeShaders);
#if UNITY_2022_1_OR_NEWER
            sb.AppendLine("supportsIndirectArgumentsBuffer: " + SystemInfo.supportsIndirectArgumentsBuffer);
#else
            sb.AppendLine("supportsIndirectArgumentsBuffer: (Unity 版本低于 2022.1 时无此 API)");
#endif
            sb.AppendLine("maxComputeBufferInputsVertex: " + SystemInfo.maxComputeBufferInputsVertex);
            sb.AppendLine("graphicsDeviceType: " + SystemInfo.graphicsDeviceType);
            sb.AppendLine("RenderObject.CreateWillUseIndirectPath: " + RenderObject.CreateWillUseIndirectPath());
            Debug.Log(sb.ToString());
        }

        private void BuildHud()
        {
            _hud.Clear();
            _hud.AppendLine("<b>Warehouse 渲染路径烟雾测试</b>（FAIL=异常或未就绪；RenderObject 行在第 2 帧推送数据）");
            _hud.AppendLine("Create→Indirect? " + RenderObject.CreateWillUseIndirectPath());
            _hud.AppendLine("Lane 布局: " + (_lanesSpreadAlongWorldX ? "lane 沿 +X 并排 / 实例沿 +Z" : "lane 沿 +Z / 实例沿 +X（易与摄像机透视重叠）"));
            _hud.AppendLine();

            int i = 0;
            foreach (LaneKind kind in Enum.GetValues(typeof(LaneKind)))
            {
                int idx = (int)kind;
                string err = _initLaneErrors[idx] ?? _laneErrors[idx];
                string status = string.IsNullOrEmpty(err) ? "<color=#88ff88>OK</color>" : "<color=#ff8888>FAIL</color>";
                Material mat = MaterialForLane(kind);
                string matHint = mat != null ? $"<size=10>[{mat.name}]</size>" : "<size=10>[无材质]</size>";
                _hud.AppendLine($"{i++}. {kind} {status}  {matHint}");
                if (!string.IsNullOrEmpty(err))
                {
                    _hud.AppendLine("   " + err);
                }
            }
        }

        /// <summary>
        /// 该绘制路径实际使用的材质：专用槽非空优先，否则回退到默认 Simple / Warehouse。
        /// </summary>
        private Material MaterialForLane(LaneKind kind)
        {
            switch (kind)
            {
                case LaneKind.DrawMeshLoop:
                    return _matLane_DrawMeshLoop ? _matLane_DrawMeshLoop :
                        _materialWarehouse ? _materialWarehouse : _materialSimpleInstancing;
                case LaneKind.DrawMeshInstancedSimple:
                    return _matLane_DrawMeshInstancedSimple ? _matLane_DrawMeshInstancedSimple :
                        _materialSimpleInstancing;
                case LaneKind.DrawMeshInstancedWithPerInstanceBuffer:
                    return _matLane_DrawMeshInstancedWithBuffer ? _matLane_DrawMeshInstancedWithBuffer :
                        _materialWarehouse;
                case LaneKind.RenderMeshInstanced:
                    return _matLane_RenderMeshInstanced ? _matLane_RenderMeshInstanced : _materialWarehouse;
                case LaneKind.RenderMeshIndirect:
                    return _matLane_RenderMeshIndirect ? _matLane_RenderMeshIndirect : _materialWarehouse;
                case LaneKind.RenderObjectMatrixBatch:
                    return _matLane_RenderObjectMatrixBatch ? _matLane_RenderObjectMatrixBatch : _materialWarehouse;
                case LaneKind.RenderObjectIndirect:
                    return _matLane_RenderObjectIndirect ? _matLane_RenderObjectIndirect : _materialWarehouse;
                case LaneKind.RenderObjectCreate:
                    return _matLane_RenderObjectCreate ? _matLane_RenderObjectCreate : _materialWarehouse;
                default:
                    return null;
            }
        }

        private void ClearLaneErrors()
        {
            for (int i = 0; i < _laneErrors.Length; i++)
            {
                _laneErrors[i] = null;
            }
        }

        private void ClearInitLaneErrors()
        {
            for (int i = 0; i < _initLaneErrors.Length; i++)
            {
                _initLaneErrors[i] = null;
            }
        }

        private void TryInitBuffers()
        {
            ReleaseBuffers();

            if (_mesh == null)
            {
                return;
            }

            uint indexCount = _mesh.GetIndexCount(0);

            // 与 RenderObject 一致：不支持时切勿 new IndirectArguments，否则会触发「requires compute shader」类错误。
            if (!RenderObject.CreateWillUseIndirectPath())
            {
                _initLaneErrors[(int)LaneKind.RenderMeshIndirect] =
                    "当前平台不支持 IndirectArguments（如 WebGL），本列跳过；请仅依赖 MatrixBatch / DrawMeshInstanced 路径。";
            }
            else
            {
                try
                {
                    _indirectCommandBuf = new GraphicsBuffer(
                        GraphicsBuffer.Target.IndirectArguments,
                        1,
                        GraphicsBuffer.IndirectDrawIndexedArgs.size);
                    _indirectArgs = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
                    _indirectArgs[0].indexCountPerInstance = indexCount;
                    _indirectArgs[0].instanceCount = (uint)Mathf.Min(_instancesPerRow, 1023);
                    _indirectCommandBuf.SetData(_indirectArgs);
                }
                catch (Exception e)
                {
                    _initLaneErrors[(int)LaneKind.RenderMeshIndirect] = "Init Indirect buf: " + e.Message;
                }
            }

            int count = Mathf.Clamp(_instancesPerRow, 1, 1023);
            try
            {
                _instanceDataBuf = new ComputeBuffer(count, sizeof(float) * 16);
                var data = new Matrix4x4[count];
                for (int i = 0; i < count; i++)
                {
                    data[i] = Matrix4x4.identity;
                }

                _instanceDataBuf.SetData(data);
            }
            catch (Exception e)
            {
                _initLaneErrors[(int)LaneKind.RenderMeshIndirect] =
                    (_initLaneErrors[(int)LaneKind.RenderMeshIndirect] ?? "") + " Init instance CB: " + e.Message;
            }

            try
            {
                _identityStubBuf = new ComputeBuffer(1023, sizeof(float) * 16);
                var stub = new Matrix4x4[1023];
                for (int i = 0; i < 1023; i++)
                {
                    stub[i] = Matrix4x4.identity;
                }

                _identityStubBuf.SetData(stub);
            }
            catch (Exception e)
            {
                string stubErr = "Init stub CB: " + e.Message;
                _initLaneErrors[(int)LaneKind.DrawMeshInstancedWithPerInstanceBuffer] = stubErr;
                _initLaneErrors[(int)LaneKind.RenderMeshInstanced] = stubErr;
            }
        }

        private void TryInitRenderObjects()
        {
            ReleaseRenderObjects();
            if (_mesh == null)
            {
                return;
            }

            Material matMatrix = MaterialForLane(LaneKind.RenderObjectMatrixBatch);
            if (matMatrix != null)
            {
                try
                {
                    _roMatrix = new RenderObjectMatrixBatch(_mesh, matMatrix, Matrix4x4.identity);
                }
                catch (Exception e)
                {
                    _initLaneErrors[(int)LaneKind.RenderObjectMatrixBatch] = "Ctor: " + e.Message;
                }
            }
            else
            {
                _initLaneErrors[(int)LaneKind.RenderObjectMatrixBatch] = "未指定材质（专用槽或默认仓库材质）";
            }

            Material matIndirect = MaterialForLane(LaneKind.RenderObjectIndirect);
            if (matIndirect != null)
            {
                try
                {
                    _roIndirect = new RenderObjectIndirect(_mesh, matIndirect, Matrix4x4.identity);
                    if (_roIndirect is RenderObjectIndirect ri && !ri.HasIndirectCommandBuffers)
                    {
                        _initLaneErrors[(int)LaneKind.RenderObjectIndirect] =
                            "当前平台不支持 IndirectArguments 缓冲，本列不会绘制（WebGL 请用 MatrixBatch / RenderObject.Create）。";
                    }
                }
                catch (Exception e)
                {
                    _initLaneErrors[(int)LaneKind.RenderObjectIndirect] = "Ctor: " + e.Message;
                }
            }
            else
            {
                _initLaneErrors[(int)LaneKind.RenderObjectIndirect] = "未指定材质（专用槽或默认仓库材质）";
            }

            Material matCreate = MaterialForLane(LaneKind.RenderObjectCreate);
            if (matCreate != null)
            {
                try
                {
                    _roCreate = RenderObject.Create(_mesh, matCreate, Matrix4x4.identity);
                }
                catch (Exception e)
                {
                    _initLaneErrors[(int)LaneKind.RenderObjectCreate] = "Ctor: " + e.Message;
                }
            }
            else
            {
                _initLaneErrors[(int)LaneKind.RenderObjectCreate] = "未指定材质（专用槽或默认仓库材质）";
            }
        }

        /// <summary>
        /// 与下方 <see cref="RunLane"/> 中各行索引一致：MatrixBatch=5，Indirect=6，Create=7（前面 0～4 为 Draw / RenderMeshInstanced 等）。
        /// </summary>
        private void TryPushRenderObjectsOnce()
        {
            OneShotUpdateItems(_roMatrix, 5);
            OneShotUpdateItems(_roIndirect, 6);
            OneShotUpdateItems(_roCreate, 7);
        }

        private void OneShotUpdateItems(RenderObject ro, int laneIndex)
        {
            if (ro == null)
            {
                return;
            }

            int n = Mathf.Clamp(_instancesPerRow, 1, 1023);
            Vector3 origin = LaneOrigin(laneIndex);
            var mats = new Matrix4x4[n];
            var show = new bool[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 p = origin + InstanceOffsetInLane(i);
                mats[i] = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one);
                show[i] = true;
            }

            ro.UpdateItems(mats, show).Forget();
        }

        private void ReleaseRenderObjects()
        {
            _roMatrix?.Release();
            _roMatrix = null;
            _roIndirect?.Release();
            _roIndirect = null;
            _roCreate?.Release();
            _roCreate = null;
        }

        private void ReleaseBuffers()
        {
            _indirectCommandBuf?.Release();
            _indirectCommandBuf = null;
            _instanceDataBuf?.Release();
            _instanceDataBuf = null;
            _identityStubBuf?.Release();
            _identityStubBuf = null;
            _indirectArgs = null;
        }

        private void RunLane(LaneKind kind, int laneIndex)
        {
            int id = (int)kind;
            if (_mesh == null)
            {
                _laneErrors[id] = "Mesh 未指定";
                return;
            }

            try
            {
                switch (kind)
                {
                    case LaneKind.DrawMeshLoop:
                        RunDrawMeshLoop(laneIndex);
                        break;
                    case LaneKind.DrawMeshInstancedSimple:
                        RunDrawMeshInstancedSimple(laneIndex);
                        break;
                    case LaneKind.DrawMeshInstancedWithPerInstanceBuffer:
                        RunDrawMeshInstancedWithBuffer(laneIndex);
                        break;
                    case LaneKind.RenderMeshInstanced:
                        RunGraphicsRenderMeshInstanced(laneIndex);
                        break;
                    case LaneKind.RenderMeshIndirect:
                        RunRenderMeshIndirect(laneIndex);
                        break;
                    case LaneKind.RenderObjectMatrixBatch:
                        RunRenderObject(_roMatrix);
                        break;
                    case LaneKind.RenderObjectIndirect:
                        RunRenderObject(_roIndirect);
                        break;
                    case LaneKind.RenderObjectCreate:
                        RunRenderObject(_roCreate);
                        break;
                }
            }
            catch (Exception e)
            {
                _laneErrors[id] = e.GetType().Name + ": " + e.Message;
            }
        }

        /// <summary>
        /// 每条绘制路径一条 lane：原点错开，避免彼此重叠。
        /// </summary>
        private Vector3 LaneOrigin(int laneIndex)
        {
            if (_lanesSpreadAlongWorldX)
            {
                return _firstInstanceOrigin + Vector3.right * (laneIndex * _spacingBetweenLanes);
            }

            return _firstInstanceOrigin + Vector3.forward * (laneIndex * _spacingBetweenLanes);
        }

        /// <summary>
        /// 同一 lane 内多个实例的位移（与 lane 轴向垂直，避免「lane 间距」与「实例间距」共线叠在一起）。
        /// </summary>
        private Vector3 InstanceOffsetInLane(int indexInLane)
        {
            if (_lanesSpreadAlongWorldX)
            {
                return Vector3.forward * (indexInLane * _spacingAlongRow);
            }

            return Vector3.right * (indexInLane * _spacingAlongRow);
        }

        private void RunDrawMeshLoop(int laneIndex)
        {
            Material mat = MaterialForLane(LaneKind.DrawMeshLoop);
            if (mat == null)
            {
                _laneErrors[(int)LaneKind.DrawMeshLoop] = "需要至少一种材质（专用槽或默认 Simple / 仓库）";
                return;
            }

            Vector3 origin = LaneOrigin(laneIndex);
            int n = Mathf.Clamp(_instancesPerRow, 1, 64);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = origin + InstanceOffsetInLane(i);
                Graphics.DrawMesh(_mesh, Matrix4x4.TRS(p, Quaternion.identity, Vector3.one), mat, gameObject.layer);
            }
        }

        private void RunDrawMeshInstancedSimple(int laneIndex)
        {
            Material mat = MaterialForLane(LaneKind.DrawMeshInstancedSimple);
            if (mat == null)
            {
                _laneErrors[(int)LaneKind.DrawMeshInstancedSimple] =
                    "未指定材质（专用槽或默认 _materialSimpleInstancing，建议 URP Lit + GPU Instancing）";
                return;
            }

            int n = Mathf.Clamp(_instancesPerRow, 1, 1023);
            Vector3 origin = LaneOrigin(laneIndex);
            var matrices = new Matrix4x4[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 p = origin + InstanceOffsetInLane(i);
                matrices[i] = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one);
            }

            Graphics.DrawMeshInstanced(
                _mesh,
                0,
                mat,
                matrices,
                n,
                null,
                ShadowCastingMode.On,
                true,
                gameObject.layer);
        }

        private void RunDrawMeshInstancedWithBuffer(int laneIndex)
        {
            Material mat = MaterialForLane(LaneKind.DrawMeshInstancedWithPerInstanceBuffer);
            if (mat == null)
            {
                _laneErrors[(int)LaneKind.DrawMeshInstancedWithPerInstanceBuffer] =
                    "未指定材质（专用槽或默认仓库材质，需支持 _PerInstanceItemData）";
                return;
            }

            if (_identityStubBuf == null)
            {
                return;
            }

            int n = Mathf.Clamp(_instancesPerRow, 1, 1023);
            Vector3 origin = LaneOrigin(laneIndex);
            var block = new MaterialPropertyBlock();
            block.SetBuffer(PerInstanceItemDataId, _identityStubBuf);
            var matrices = new Matrix4x4[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 p = origin + InstanceOffsetInLane(i);
                matrices[i] = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one);
            }

            Graphics.DrawMeshInstanced(
                _mesh,
                0,
                mat,
                matrices,
                n,
                block,
                ShadowCastingMode.On,
                true,
                gameObject.layer);
        }

        /// <summary>
        /// <see cref="Graphics.RenderMeshInstanced(RenderParams, Mesh, int, Matrix4x4[])"/>（与 <see cref="MultiRender.RenderSetting.RenderMesh"/> 相同入口）。
        /// 仓库 Shader 时绑定单位矩阵 stub，与「DrawMeshInstanced + Buffer」列一致。
        /// </summary>
        private void RunGraphicsRenderMeshInstanced(int laneIndex)
        {
            Material mat = MaterialForLane(LaneKind.RenderMeshInstanced);
            if (mat == null)
            {
                _laneErrors[(int)LaneKind.RenderMeshInstanced] =
                    "未指定材质（专用槽或默认仓库材质；需 _PerInstanceItemData 时请用仓库 Shader）";
                return;
            }

            if (_identityStubBuf == null)
            {
                return;
            }

            int total = Mathf.Clamp(_instancesPerRow, 1, 1023 * 16);
            Vector3 origin = LaneOrigin(laneIndex);
            const int maxPerDraw = 1023;
            int offset = 0;
            while (offset < total)
            {
                int batchLen = Mathf.Min(maxPerDraw, total - offset);
                var matrices = new Matrix4x4[batchLen];
                for (int i = 0; i < batchLen; i++)
                {
                    Vector3 p = origin + InstanceOffsetInLane(offset + i);
                    matrices[i] = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one);
                }

                var rp = new RenderParams(mat)
                {
                    worldBounds = HugeBounds,
                    matProps = new MaterialPropertyBlock()
                };
                rp.matProps.SetBuffer(PerInstanceItemDataId, _identityStubBuf);

                Graphics.RenderMeshInstanced(rp, _mesh, 0, matrices);
                offset += batchLen;
            }
        }

        private void RunRenderMeshIndirect(int laneIndex)
        {
            Material mat = MaterialForLane(LaneKind.RenderMeshIndirect);
            if (mat == null)
            {
                _laneErrors[(int)LaneKind.RenderMeshIndirect] = "未指定材质（专用槽或默认仓库材质）";
                return;
            }

            if (_indirectCommandBuf == null || _indirectArgs == null)
            {
                _laneErrors[(int)LaneKind.RenderMeshIndirect] ??= "间接命令缓冲未初始化";
                return;
            }

            int n = Mathf.Clamp(_instancesPerRow, 1, 1023);
            EnsureIndirectInstanceBuffer(n);

            if (_instanceDataBuf == null)
            {
                _laneErrors[(int)LaneKind.RenderMeshIndirect] ??= "实例 ComputeBuffer 创建失败";
                return;
            }

            Vector3 origin = LaneOrigin(laneIndex);
            var matrices = new Matrix4x4[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 p = origin + InstanceOffsetInLane(i);
                matrices[i] = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one);
            }

            _instanceDataBuf.SetData(matrices);

            uint indexCount = _mesh.GetIndexCount(0);
            _indirectArgs[0].indexCountPerInstance = indexCount;
            _indirectArgs[0].instanceCount = (uint)n;
            _indirectCommandBuf.SetData(_indirectArgs);

            var rp = new RenderParams(mat)
            {
                worldBounds = HugeBounds,
                matProps = new MaterialPropertyBlock()
            };
            rp.matProps.SetBuffer(PerInstanceItemDataId, _instanceDataBuf);

            Graphics.RenderMeshIndirect(rp, _mesh, _indirectCommandBuf);
        }

        private void EnsureIndirectInstanceBuffer(int instanceCount)
        {
            if (_instanceDataBuf != null && _instanceDataBuf.count == instanceCount)
            {
                return;
            }

            _instanceDataBuf?.Release();
            _instanceDataBuf = new ComputeBuffer(instanceCount, sizeof(float) * 16);
        }

        /// <summary>仅提交绘制；数据由 <see cref="TryPushRenderObjectsOnce"/> 在第 2 帧推送。</summary>
        private static void RunRenderObject(RenderObject ro)
        {
            if (ro == null)
            {
                return;
            }

            ro.Render(true);
        }
    }
}
