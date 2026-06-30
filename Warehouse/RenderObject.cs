using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    /// <summary>
    /// 负责管理单种 Mesh 的实例化渲染数据。
    /// 采用 A/B 双缓冲方式，降低“写入命令缓冲”与“提交渲染”的竞争风险。
    /// 根据平台能力选择 <see cref="RenderObjectIndirect"/> 或 <see cref="RenderObjectMatrixBatch"/>。
    /// </summary>
    public abstract class RenderObject
    {
        private const int ThreadPoolBuildThreshold = 512;
        private const int PickBatchSize = 1023;

        /// <summary>
        /// 统一的超大包围盒，避免实例被错误裁剪。
        /// </summary>
        private static readonly Bounds
            DefaultWorldBounds = new Bounds(Vector3.zero, new Vector3(100000f, 100000f, 100000f));

        /// <summary>
        /// 实例局部偏移矩阵（例如把物体中心对齐到格位）。
        /// </summary>
        private readonly Matrix4x4 _offset;

        /// <summary>
        /// 渲染使用的网格。
        /// </summary>
        protected readonly Mesh Mesh;

        protected readonly uint IndexCountPerInstance;

        internal Mesh RenderMesh => Mesh;

        /// <summary>
        /// 筛选后参与渲染的实例列表。
        /// </summary>
        protected List<ItemInstanceData> Items;

        // A/B 两套渲染参数，避免同一套属性块在更新和渲染时冲突。
        protected RenderParams RenderParamsA;
        protected RenderParams RenderParamsB;

        /// <summary>
        /// 当前渲染阶段是否从 A 缓冲读取。
        /// </summary>
        protected bool RenderFromA;

        protected bool CanRenderA;
        protected bool CanRenderB;

        private int _updateTicket;
        private bool _released;
        private readonly object _releaseLock = new object();

        private static readonly Stack<List<ItemInstanceData>> ItemListPool = new Stack<List<ItemInstanceData>>();
        private static readonly object ItemListPoolLock = new object();

        /// <summary>
        /// Shader 中 <c>StructuredBuffer</c> 属性名（与 <c>SimpleInstancing.hlsl</c> 的 <c>_PerInstanceItemData</c> 一致）。
        /// </summary>
        protected static readonly int PerInstanceItemDataPropertyId = Shader.PropertyToID("_PerInstanceItemData");

        /// <summary>
        /// 材质级全局显隐（<c>WarehouseInstance</c> 的 <c>_DitherVisibility</c>），变更时无需重建实例缓冲。
        /// </summary>
        private static readonly int DitherVisibilityPropertyId = Shader.PropertyToID("_DitherVisibility");
        private static readonly int WarehousePickColorPropertyId = Shader.PropertyToID("_WarehousePickColor");

        private readonly Matrix4x4[] _pickMatrices = new Matrix4x4[PickBatchSize];
        private readonly Vector4[] _pickColors = new Vector4[PickBatchSize];
        private readonly MaterialPropertyBlock _pickMaterialProperties = new MaterialPropertyBlock();

        /// <summary>
        /// 按平台能力创建具体实现：支持 GPU 间接参数缓冲时用 <see cref="RenderObjectIndirect"/>，否则用 <see cref="RenderObjectMatrixBatch"/>（WebGL 常见）。
        /// </summary>
        public static RenderObject Create(Mesh mesh, Material material, Matrix4x4 offset)
        {
            if (UseIndirectInstancingPath())
            {
                return new RenderObjectIndirect(mesh, material, offset);
            }

            return new RenderObjectMatrixBatch(mesh, material, offset);
        }
        
        /// <summary>
        /// 当前设备上 <see cref="Create"/> 是否会选用 <see cref="RenderObjectIndirect"/>（否则为 <see cref="RenderObjectMatrixBatch"/>）。
        /// 供运行时诊断 / 测试脚本使用。
        /// </summary>
        public static bool CreateWillUseIndirectPath()
        {
            return UseIndirectInstancingPath();
        }

        /// <summary>
        /// 是否可创建 / 使用 <see cref="GraphicsBuffer.Target.IndirectArguments"/>（与 <see cref="Create"/> 分支一致）。
        /// WebGL 等通常为 false；切勿在未满足时 new 该类型缓冲，否则会报 compute 相关错误。
        /// </summary>
        protected static bool CanAllocateIndirectCommandBuffers()
        {
#if UNITY_2022_1_OR_NEWER
            return SystemInfo.supportsIndirectArgumentsBuffer;
#else
            return SystemInfo.supportsComputeShaders;
#endif
        }

        /// <summary>
        /// 是否使用 <see cref="Graphics.RenderMeshIndirect"/>。WebGL 通常不支持间接参数缓冲；2022.1+ 以 <see cref="SystemInfo.supportsIndirectArgumentsBuffer"/> 为准，避免仅依赖 <c>supportsComputeShaders</c> 误判。
        /// </summary>
        private static bool UseIndirectInstancingPath()
        {
            return CanAllocateIndirectCommandBuffers();
        }

        /// <summary>
        /// 每个实例上传到 GPU 的结构体（与 <c>SimpleInstancing.hlsl</c> 的 <c>InstanceItemData</c> 布局一致）。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        protected readonly struct ItemInstanceData
        {
            public readonly Matrix4x4 Matrix;
            /// <summary>显隐因子 [0,1]，Shader 侧用 Bayer Dither + clip 实现，非 Alpha 混合。</summary>
            public readonly float Visibility;
            public readonly uint PickId;
            public readonly float Pad1;
            public readonly float Pad2;

            public ItemInstanceData(Matrix4x4 matrix, float visibility = 1f, uint pickId = WarehousePickId.Miss)
            {
                Matrix = matrix;
                Visibility = visibility;
                PickId = pickId;
                Pad1 = 0f;
                Pad2 = 0f;
            }

            public static ItemInstanceData Identity(float visibility = 1f, uint pickId = WarehousePickId.Miss)
            {
                return new ItemInstanceData(Matrix4x4.identity, visibility, pickId);
            }

            /// <summary>
            /// 结构体字节大小（矩阵 64 + alpha/padding 16）。
            /// </summary>
            public static int Size()
            {
                return sizeof(float) * 20;
            }
        }

        protected RenderObject(Mesh mesh, Material material, Matrix4x4 offset)
        {
            Mesh = mesh;
            IndexCountPerInstance = mesh != null ? mesh.GetIndexCount(0) : 0u;
            _offset = offset;
            Items = RentItemList(0);
            RenderParamsA = CreateRenderParams(material);
            RenderParamsB = CreateRenderParams(material);
        }

        /// <summary>
        /// 释放 GPU 资源。必须在主线程显式调用。
        /// </summary>
        public void Release()
        {
            lock (_releaseLock)
            {
                if (_released)
                {
                    return;
                }

                _released = true;
                Interlocked.Increment(ref _updateTicket);
                ReleaseBackendResources();
                ReturnItemList(Items);
                Items = null;
            }
        }

        /// <summary>
        /// 统一更新入口：调用方只传实例数据，不需要关心 Step1/Step2 细节。
        /// </summary>
        public UniTask UpdateItems(Matrix4x4[] itemTrans, bool[] itemState)
        {
            return UpdateItems(itemTrans, itemState, null, null);
        }

        public async UniTask UpdateItems(Matrix4x4[] itemTrans, bool[] itemState, float[] itemVisibilities)
        {
            await UpdateItems(itemTrans, itemState, itemVisibilities, null);
        }

        public async UniTask UpdateItems(Matrix4x4[] itemTrans, bool[] itemState, float[] itemVisibilities, uint[] itemPickIds)
        {
            if (_released)
            {
                return;
            }

            int localTicket = Interlocked.Increment(ref _updateTicket);
            List<ItemInstanceData> nextItems;
            int inputCount = itemTrans == null || itemState == null ? 0 : Mathf.Min(itemTrans.Length, itemState.Length);
            bool useThreadPoolForBuild = inputCount >= ThreadPoolBuildThreshold &&
                                         !WarehousePlatformCompat.CpuInstancingBuildMustUseMainThread;
            if (useThreadPoolForBuild)
            {
                await UniTask.SwitchToThreadPool();
                nextItems = BuildItems(itemTrans, itemState, itemVisibilities, itemPickIds);
            }
            else
            {
                nextItems = BuildItems(itemTrans, itemState, itemVisibilities, itemPickIds);
            }

            await UniTask.SwitchToMainThread();
            lock (_releaseLock)
            {
                if (_released || localTicket != _updateTicket)
                {
                    ReturnItemList(nextItems);
                    return;
                }

                SwapItems(nextItems);

                if (!CanUploadRenderData())
                {
                    return;
                }

                if (Items.Count == 0)
                {
                    DisableCurrentWriteTargetRenderFlag();
                }
                else
                {
                    UploadToWriteTarget();
                }

                // 下一帧切换到另一套缓冲渲染。
                RenderFromA = !RenderFromA;
            }
        }

        /// <summary>
        /// 设置材质级全局显隐乘数（立即生效，不走 <see cref="UpdateItems"/>）。
        /// </summary>
        public void SetDitherVisibility(float visibility)
        {
            float v = Mathf.Clamp01(visibility);
            RenderParamsA.matProps.SetFloat(DitherVisibilityPropertyId, v);
            RenderParamsB.matProps.SetFloat(DitherVisibilityPropertyId, v);
        }

        /// <summary>
        /// 仅更新已上传实例的显隐因子并写回 GPU（矩阵与 show 集合未变时走快速路径）。
        /// </summary>
        /// <returns>是否成功走快速路径；false 时调用方应回退到 <see cref="UpdateItems"/>。</returns>
        public bool TryPatchVisibilitiesOnly(Matrix4x4[] itemTrans, bool[] itemState, float[] itemVisibilities)
        {
            return TryPatchVisibilitiesOnly(itemTrans, itemState, itemVisibilities, null);
        }

        public bool TryPatchVisibilitiesOnly(Matrix4x4[] itemTrans, bool[] itemState, float[] itemVisibilities, uint[] itemPickIds)
        {
            if (_released || itemTrans == null || itemState == null)
            {
                return false;
            }

            int count = Mathf.Min(itemTrans.Length, itemState.Length);
            if (count == 0)
            {
                return Items != null && Items.Count == 0;
            }

            int visibleCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (itemState[i])
                {
                    visibleCount++;
                }
            }

            if (Items == null || Items.Count != visibleCount)
            {
                return false;
            }

            lock (_releaseLock)
            {
                if (_released)
                {
                    return false;
                }

                int itemIndex = 0;
                for (int i = 0; i < count; i++)
                {
                    if (!itemState[i])
                    {
                        continue;
                    }

                    ItemInstanceData current = Items[itemIndex];
                    float visibility = ResolveItemVisibility(itemVisibilities, i);
                    uint pickId = ResolveItemPickId(itemPickIds, i, current.PickId);
                    Items[itemIndex] = new ItemInstanceData(current.Matrix, visibility, pickId);
                    itemIndex++;
                }

                if (!CanUploadRenderData())
                {
                    return true;
                }

                if (Items.Count == 0)
                {
                    DisableCurrentWriteTargetRenderFlag();
                    return true;
                }

                UploadToWriteTarget();
                RenderFromA = !RenderFromA;
                return true;
            }
        }

        /// <summary>
        /// 提交绘制。根据双缓冲状态，渲染“上一帧准备好”的缓冲。
        /// </summary>
        public void Render(bool render = true)
        {
            if (!render)
            {
                return;
            }

            RenderBackend();
        }

        internal void RenderPick(Material pickMaterial, CommandBuffer cmd)
        {
            if (pickMaterial == null || cmd == null || Mesh == null || Items == null || Items.Count == 0)
            {
                return;
            }

            RenderPickBatches(pickMaterial, cmd);
        }

        /// <summary>
        /// 释放子类持有的 GPU 资源。
        /// </summary>
        protected abstract void ReleaseBackendResources();

        /// <summary>
        /// 将实例数据写入当前“写入侧”并标记可读。
        /// </summary>
        protected abstract void UploadToWriteTarget();

        /// <summary>
        /// 按双缓冲状态提交一帧绘制。
        /// </summary>
        protected abstract void RenderBackend();

        internal int UploadedInstanceCount => Items?.Count ?? 0;

        internal bool CanPickRender => RenderFromA ? CanRenderA : CanRenderB;

        /// <summary>
        /// Pick Pass 直接读取 <see cref="Items"/> 矩阵批绘制，不依赖间接绘制缓冲是否就绪。
        /// </summary>
        internal bool HasPickInstances => Items != null && Items.Count > 0;

        /// <summary>
        /// 除网格外，子类是否已具备上传条件（如间接路径的命令缓冲已创建）。
        /// </summary>
        protected abstract bool ValidateBackendForUpload();

        private bool CanUploadRenderData()
        {
            if (Mesh == null)
            {
                return false;
            }

            return ValidateBackendForUpload();
        }

        private void DisableCurrentWriteTargetRenderFlag()
        {
            if (RenderFromA)
            {
                CanRenderB = false;
                return;
            }

            CanRenderA = false;
        }

        private static RenderParams CreateRenderParams(Material material)
        {
            return new RenderParams(material)
            {
                worldBounds = DefaultWorldBounds,
                matProps = new MaterialPropertyBlock()
            };
        }

        private List<ItemInstanceData> BuildItems(Matrix4x4[] itemTrans, bool[] itemState, float[] itemVisibilities, uint[] itemPickIds)
        {
            int capacity = 0;
            if (itemTrans != null && itemState != null)
            {
                capacity = Mathf.Min(itemTrans.Length, itemState.Length);
            }

            var result = RentItemList(capacity);
            result.Clear();

            // 输入保护：无效输入返回空列表，防止空引用。
            if (itemTrans == null || itemState == null)
            {
                return result;
            }

            if (itemTrans.Length == 0 || itemState.Length == 0)
            {
                return result;
            }

            // 长度不一致时按最短长度处理，避免越界。
            int count = Mathf.Min(itemTrans.Length, itemState.Length);
            if (result.Capacity < count)
            {
                result.Capacity = count;
            }

            for (int i = 0; i < count; i++)
            {
                if (!itemState[i])
                {
                    continue;
                }

                float visibility = ResolveItemVisibility(itemVisibilities, i);
                uint pickId = ResolveItemPickId(itemPickIds, i, WarehousePickId.Miss);
                result.Add(new ItemInstanceData(itemTrans[i] * _offset, visibility, pickId));
            }

            return result;
        }

        private static float ResolveItemVisibility(float[] itemVisibilities, int index)
        {
            if (itemVisibilities == null || itemVisibilities.Length == 0)
            {
                return 1f;
            }

            if (index < 0 || index >= itemVisibilities.Length)
            {
                return 1f;
            }

            return Mathf.Clamp01(itemVisibilities[index]);
        }

        private static uint ResolveItemPickId(uint[] itemPickIds, int index, uint fallback)
        {
            if (itemPickIds == null || itemPickIds.Length == 0 || index < 0 || index >= itemPickIds.Length)
            {
                return fallback;
            }

            return itemPickIds[index];
        }

        private void RenderPickBatches(Material pickMaterial, CommandBuffer cmd)
        {
            int totalCount = Items.Count;
            for (int start = 0; start < totalCount; start += PickBatchSize)
            {
                int batchCount = Mathf.Min(PickBatchSize, totalCount - start);
                for (int i = 0; i < batchCount; i++)
                {
                    ItemInstanceData item = Items[start + i];
                    _pickMatrices[i] = item.Matrix;
                    _pickColors[i] = EncodePickColor(item.PickId);
                }

                _pickMaterialProperties.Clear();
                _pickMaterialProperties.SetVectorArray(WarehousePickColorPropertyId, _pickColors);
                cmd.DrawMeshInstanced(Mesh, 0, pickMaterial, 0, _pickMatrices, batchCount, _pickMaterialProperties);
            }
        }

        private static Vector4 EncodePickColor(uint pickId)
        {
            const float inv255 = 1f / 255f;
            return new Vector4(
                (pickId & 0xFFu) * inv255,
                ((pickId >> 8) & 0xFFu) * inv255,
                ((pickId >> 16) & 0xFFu) * inv255,
                ((pickId >> 24) & 0xFFu) * inv255);
        }

        private void SwapItems(List<ItemInstanceData> nextItems)
        {
            if (nextItems == null)
            {
                nextItems = RentItemList(0);
            }

            var oldItems = Items;
            Items = nextItems;
            ReturnItemList(oldItems);
        }

        private static List<ItemInstanceData> RentItemList(int minCapacity)
        {
            lock (ItemListPoolLock)
            {
                if (ItemListPool.Count > 0)
                {
                    var list = ItemListPool.Pop();
                    if (list.Capacity < minCapacity)
                    {
                        list.Capacity = minCapacity;
                    }

                    return list;
                }
            }

            return new List<ItemInstanceData>(minCapacity);
        }

        private static void ReturnItemList(List<ItemInstanceData> list)
        {
            if (list == null)
            {
                return;
            }

            list.Clear();
            lock (ItemListPoolLock)
            {
                ItemListPool.Push(list);
            }
        }
    }

    /// <summary>
    /// 支持 Indirect 与 <see cref="ComputeBuffer"/> 的路径：<c>Graphics.RenderMeshIndirect</c>。
    /// </summary>
    public sealed class RenderObjectIndirect : RenderObject
    {
        private static readonly int CommandCount = 1;

        private ComputeBuffer _instancesBuffer;
        private GraphicsBuffer _commandBufA;
        private GraphicsBuffer _commandBufB;
        private GraphicsBuffer.IndirectDrawIndexedArgs[] _commandDataA;
        private GraphicsBuffer.IndirectDrawIndexedArgs[] _commandDataB;

        public RenderObjectIndirect(Mesh mesh, Material material, Matrix4x4 offset)
            : base(mesh, material, offset)
        {
            // WebGL 等不支持 IndirectArguments 时禁止分配，否则 Unity 抛错且无法恢复。
            if (!CanAllocateIndirectCommandBuffers())
            {
                _commandDataA = null;
                _commandDataB = null;
                return;
            }

            _commandBufA = CreateCommandBuffer();
            _commandDataA = new GraphicsBuffer.IndirectDrawIndexedArgs[CommandCount];
            _commandBufB = CreateCommandBuffer();
            _commandDataB = new GraphicsBuffer.IndirectDrawIndexedArgs[CommandCount];
        }

        protected override void ReleaseBackendResources()
        {
            _instancesBuffer?.Release();
            _instancesBuffer = null;
            _commandBufA?.Release();
            _commandBufA = null;
            _commandBufB?.Release();
            _commandBufB = null;
        }

        protected override bool ValidateBackendForUpload()
        {
            return _commandBufA != null && _commandBufB != null;
        }

        protected override void UploadToWriteTarget()
        {
            if (RenderFromA)
            {
                UpdateBufferAndCommand(_commandDataB, _commandBufB, RenderParamsB.matProps);
                CanRenderB = true;
                return;
            }

            UpdateBufferAndCommand(_commandDataA, _commandBufA, RenderParamsA.matProps);
            CanRenderA = true;
        }

        protected override void RenderBackend()
        {
            if (RenderFromA)
            {
                if (CanRenderA)
                {
                    Graphics.RenderMeshIndirect(RenderParamsA, Mesh, _commandBufA);
                }

                return;
            }

            if (CanRenderB)
            {
                Graphics.RenderMeshIndirect(RenderParamsB, Mesh, _commandBufB);
            }
        }

        private void UpdateBufferAndCommand(
            GraphicsBuffer.IndirectDrawIndexedArgs[] commandData,
            GraphicsBuffer commandBuffer,
            MaterialPropertyBlock matProps)
        {
            if (_instancesBuffer == null || Items.Count != _instancesBuffer.count)
            {
                _instancesBuffer?.Release();
                _instancesBuffer = new ComputeBuffer(Items.Count, ItemInstanceData.Size());
            }

            _instancesBuffer.SetData(Items);

            commandData[0].indexCountPerInstance = IndexCountPerInstance;
            // 必须使用筛选后的实例数，否则会出现“命令数 > 实际数据数”的绘制错误。
            commandData[0].instanceCount = (uint)Items.Count;
            commandBuffer.SetData(commandData);
            matProps.SetBuffer(PerInstanceItemDataPropertyId, _instancesBuffer);
        }

        private static GraphicsBuffer CreateCommandBuffer()
        {
            return new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments,
                CommandCount,
                GraphicsBuffer.IndirectDrawIndexedArgs.size);
        }

        /// <summary>
        /// 是否已成功分配间接命令缓冲。不支持 IndirectArguments 的平台（如多数 WebGL）上为 false，此时不应调用绘制。
        /// </summary>
        public bool HasIndirectCommandBuffers => _commandBufA != null && _commandBufB != null;
    }

    /// <summary>
    /// 无 GPU 间接参数缓冲时的路径：矩阵分批 <see cref="Graphics.DrawMeshInstanced"/>（典型于 WebGL）。
    /// 变换由 <c>DrawMeshInstanced</c> 矩阵数组提供；<c>_PerInstanceItemData</c> 中矩阵为单位矩阵，仅承载每实例显隐因子（Dither）。
    /// </summary>
    public sealed class RenderObjectMatrixBatch : RenderObject
    {
        /// <summary>
        /// 与 <see cref="NonsensicalKit.DigitalTwin.Render.PartInfo"/> / <c>Graphics.RenderMeshInstanced</c> 一致的单次上限。
        /// </summary>
        private const int MaxInstancedBatch = 1023;

        private Matrix4x4[][] _matrixBatchesA;
        private Matrix4x4[][] _matrixBatchesB;
        private ItemInstanceData[][] _instanceBatchesA;
        private ItemInstanceData[][] _instanceBatchesB;
        private ComputeBuffer _instancesBuffer;

        public RenderObjectMatrixBatch(Mesh mesh, Material material, Matrix4x4 offset)
            : base(mesh, material, offset)
        {
        }

        protected override void ReleaseBackendResources()
        {
            _instancesBuffer?.Release();
            _instancesBuffer = null;
        }

        protected override bool ValidateBackendForUpload()
        {
            // WebGL2 上 maxComputeBufferInputsVertex 常为 0，但 DrawMeshInstanced + 顶点 StructuredBuffer 仍可用；勿用该字段拦截上传。
            return SystemInfo.supportsInstancing;
        }

        protected override void UploadToWriteTarget()
        {
            if (RenderFromA)
            {
                RebuildDrawBatches(Items, ref _matrixBatchesB, ref _instanceBatchesB);
                CanRenderB = true;
                return;
            }

            RebuildDrawBatches(Items, ref _matrixBatchesA, ref _instanceBatchesA);
            CanRenderA = true;
        }

        protected override void RenderBackend()
        {
            if (RenderFromA)
            {
                if (CanRenderA)
                {
                    DrawMatrixInstancedBatches(in RenderParamsA, _matrixBatchesA, _instanceBatchesA);
                }

                return;
            }

            if (CanRenderB)
            {
                DrawMatrixInstancedBatches(in RenderParamsB, _matrixBatchesB, _instanceBatchesB);
            }
        }

        private void DrawMatrixInstancedBatches(
            in RenderParams rp,
            Matrix4x4[][] matrixBatches,
            ItemInstanceData[][] instanceBatches,
            Material pickMaterial = null)
        {
            Material material = pickMaterial != null ? pickMaterial : rp.material;
            if (Mesh == null || material == null || matrixBatches == null || instanceBatches == null ||
                matrixBatches.Length == 0)
            {
                return;
            }

            for (int i = 0; i < matrixBatches.Length; i++)
            {
                Matrix4x4[] matrixBatch = matrixBatches[i];
                ItemInstanceData[] instanceBatch = i < instanceBatches.Length ? instanceBatches[i] : null;
                if (matrixBatch == null || matrixBatch.Length == 0 || instanceBatch == null ||
                    instanceBatch.Length != matrixBatch.Length)
                {
                    continue;
                }

                EnsureInstancesBufferCapacity(instanceBatch.Length);
                _instancesBuffer.SetData(instanceBatch);
                rp.matProps.SetBuffer(PerInstanceItemDataPropertyId, _instancesBuffer);

                Graphics.DrawMeshInstanced(
                    Mesh,
                    0,
                    material,
                    matrixBatch,
                    matrixBatch.Length,
                    rp.matProps,
                    rp.shadowCastingMode,
                    rp.receiveShadows,
                    rp.layer,
                    rp.camera,
                    rp.lightProbeUsage,
                    rp.lightProbeProxyVolume);
            }
        }

        private void EnsureInstancesBufferCapacity(int requiredCount)
        {
            if (_instancesBuffer != null && _instancesBuffer.count >= requiredCount)
            {
                return;
            }

            _instancesBuffer?.Release();
            _instancesBuffer = new ComputeBuffer(requiredCount, ItemInstanceData.Size());
        }

        private static void RebuildDrawBatches(
            IReadOnlyList<ItemInstanceData> items,
            ref Matrix4x4[][] matrixBatches,
            ref ItemInstanceData[][] instanceBatches)
        {
            int totalCount = items.Count;
            if (totalCount <= 0)
            {
                matrixBatches = Array.Empty<Matrix4x4[]>();
                instanceBatches = Array.Empty<ItemInstanceData[]>();
                return;
            }

            int batchCount = (totalCount - 1) / MaxInstancedBatch + 1;
            if (matrixBatches == null || matrixBatches.Length != batchCount)
            {
                matrixBatches = new Matrix4x4[batchCount][];
            }

            if (instanceBatches == null || instanceBatches.Length != batchCount)
            {
                instanceBatches = new ItemInstanceData[batchCount][];
            }

            for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
            {
                int chunkStart = batchIndex * MaxInstancedBatch;
                int chunkLength = Mathf.Min(MaxInstancedBatch, totalCount - chunkStart);
                if (matrixBatches[batchIndex] == null || matrixBatches[batchIndex].Length != chunkLength)
                {
                    matrixBatches[batchIndex] = new Matrix4x4[chunkLength];
                }

                if (instanceBatches[batchIndex] == null || instanceBatches[batchIndex].Length != chunkLength)
                {
                    instanceBatches[batchIndex] = new ItemInstanceData[chunkLength];
                }

                for (int i = 0; i < chunkLength; i++)
                {
                    ItemInstanceData item = items[chunkStart + i];
                    matrixBatches[batchIndex][i] = item.Matrix;
                    instanceBatches[batchIndex][i] = ItemInstanceData.Identity(item.Visibility, item.PickId);
                }
            }
        }
    }
}
