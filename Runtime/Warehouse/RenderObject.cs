using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

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
        /// 每个实例上传到 GPU 的结构体（间接路径）或中间表示（矩阵路径）。
        /// </summary>
        protected readonly struct ItemInstanceData
        {
            public readonly Matrix4x4 Matrix;

            public ItemInstanceData(Matrix4x4 matrix)
            {
                Matrix = matrix;
            }

            /// <summary>
            /// 结构体字节大小（16 个 float）。
            /// </summary>
            public static int Size()
            {
                return sizeof(float) * 4 * 4;
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
        public async UniTask UpdateItems(Matrix4x4[] itemTrans, bool[] itemState)
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
                nextItems = BuildItems(itemTrans, itemState);
            }
            else
            {
                nextItems = BuildItems(itemTrans, itemState);
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

        private List<ItemInstanceData> BuildItems(Matrix4x4[] itemTrans, bool[] itemState)
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

                result.Add(new ItemInstanceData(itemTrans[i] * _offset));
            }

            return result;
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
    /// 仍绑定只含单位矩阵的 <see cref="ComputeBuffer"/>，以满足着色器中 <c>_PerInstanceItemData</c> 的读取
    /// （顶点变换已由 <c>DrawMeshInstanced</c> 的矩阵数组提供，与 <c>mul(M, I)</c> 一致）。
    /// </summary>
    public sealed class RenderObjectMatrixBatch : RenderObject
    {
        /// <summary>
        /// 与 <see cref="NonsensicalKit.DigitalTwin.Render.PartInfo"/> / <c>Graphics.RenderMeshInstanced</c> 一致的单次上限。
        /// </summary>
        private const int MaxInstancedBatch = 1023;

        private Matrix4x4[][] _matrixBatchesA;
        private Matrix4x4[][] _matrixBatchesB;

        /// <summary>
        /// 每实例为单位矩阵，供 Shader 中 <c>StructuredBuffer</c> 绑定；避免 WebGL 上未绑定缓冲导致未定义读取。
        /// </summary>
        private ComputeBuffer _perInstanceIdentityBuffer;

        public RenderObjectMatrixBatch(Mesh mesh, Material material, Matrix4x4 offset)
            : base(mesh, material, offset)
        {
            var stub = new ItemInstanceData[MaxInstancedBatch];
            var identity = new ItemInstanceData(Matrix4x4.identity);
            for (int i = 0; i < MaxInstancedBatch; i++)
            {
                stub[i] = identity;
            }

            _perInstanceIdentityBuffer = new ComputeBuffer(MaxInstancedBatch, ItemInstanceData.Size());
            _perInstanceIdentityBuffer.SetData(stub);
        }

        protected override void ReleaseBackendResources()
        {
            _perInstanceIdentityBuffer?.Release();
            _perInstanceIdentityBuffer = null;
        }

        protected override bool ValidateBackendForUpload()
        {
            // WebGL2 上 maxComputeBufferInputsVertex 常为 0，但 DrawMeshInstanced + 顶点 StructuredBuffer 仍可用；勿用该字段拦截上传。
            return SystemInfo.supportsInstancing && _perInstanceIdentityBuffer != null;
        }

        protected override void UploadToWriteTarget()
        {
            if (RenderFromA)
            {
                RebuildMatrixBatches(Items, ref _matrixBatchesB);
                CanRenderB = true;
                return;
            }

            RebuildMatrixBatches(Items, ref _matrixBatchesA);
            CanRenderA = true;
        }

        protected override void RenderBackend()
        {
            if (RenderFromA)
            {
                if (CanRenderA)
                {
                    DrawMatrixInstancedBatches(in RenderParamsA, _matrixBatchesA);
                }

                return;
            }

            if (CanRenderB)
            {
                DrawMatrixInstancedBatches(in RenderParamsB, _matrixBatchesB);
            }
        }

        private void DrawMatrixInstancedBatches(in RenderParams rp, Matrix4x4[][] matrixBatches)
        {
            if (Mesh == null || rp.material == null || matrixBatches == null || matrixBatches.Length == 0 ||
                _perInstanceIdentityBuffer == null)
            {
                return;
            }

            for (int i = 0; i < matrixBatches.Length; i++)
            {
                Matrix4x4[] matrixBatch = matrixBatches[i];
                if (matrixBatch == null || matrixBatch.Length == 0)
                {
                    continue;
                }

                rp.matProps.SetBuffer(PerInstanceItemDataPropertyId, _perInstanceIdentityBuffer);

                Graphics.DrawMeshInstanced(
                    Mesh,
                    0,
                    rp.material,
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

        private static void RebuildMatrixBatches(IReadOnlyList<ItemInstanceData> items, ref Matrix4x4[][] batches)
        {
            int totalCount = items.Count;
            if (totalCount <= 0)
            {
                batches = Array.Empty<Matrix4x4[]>();
                return;
            }

            int batchCount = (totalCount - 1) / MaxInstancedBatch + 1;
            if (batches == null || batches.Length != batchCount)
            {
                batches = new Matrix4x4[batchCount][];
            }

            for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
            {
                int chunkStart = batchIndex * MaxInstancedBatch;
                int chunkLength = Mathf.Min(MaxInstancedBatch, totalCount - chunkStart);
                if (batches[batchIndex] == null || batches[batchIndex].Length != chunkLength)
                {
                    batches[batchIndex] = new Matrix4x4[chunkLength];
                }

                for (int i = 0; i < chunkLength; i++)
                {
                    batches[batchIndex][i] = items[chunkStart + i].Matrix;
                }
            }
        }
    }
}
