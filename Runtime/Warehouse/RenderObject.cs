using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    /// <summary>
    /// 负责管理单种 Mesh 的间接实例化渲染数据。
    /// 采用 A/B 双缓冲方式，降低“写入命令缓冲”与“提交渲染”的竞争风险。
    /// </summary>
    public class RenderObject
    {
        /// <summary>
        /// Shader 中每实例数据缓冲的属性名。
        /// </summary>
        private static readonly int PerInstanceItemData = Shader.PropertyToID("_PerInstanceItemData");

        private static readonly int CommandCount = 1;

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
        private readonly Mesh _mesh;

        private readonly uint _indexCountPerInstance;

        /// <summary>
        /// 筛选后参与渲染的实例列表。
        /// </summary>
        private List<ItemInstanceData> _items;

        private static readonly Stack<List<ItemInstanceData>> ItemListPool = new Stack<List<ItemInstanceData>>();
        private static readonly object ItemListPoolLock = new object();

        /// <summary>
        /// 每实例结构化数据缓冲（矩阵等）。
        /// </summary>
        private ComputeBuffer _instancesBuffer;

        // A/B 两套渲染参数与命令缓冲，避免同一缓冲在更新和渲染时冲突。
        private RenderParams _rpA;
        private RenderParams _rpB;
        private GraphicsBuffer _commandBufA;
        private GraphicsBuffer _commandBufB;
        private GraphicsBuffer.IndirectDrawIndexedArgs[] _commandDataA;
        private GraphicsBuffer.IndirectDrawIndexedArgs[] _commandDataB;

        /// <summary>
        /// 当前渲染阶段是否从 A 缓冲读取。
        /// </summary>
        private bool _renderFromA = false;

        private bool _canRenderA = false;
        private bool _canRenderB = false;
        private int _updateTicket = 0;
        private bool _released = false;


        /// <summary>
        /// 构造渲染对象并初始化双缓冲资源。
        /// </summary>
        public RenderObject(Mesh mesh, Material material, Matrix4x4 offset)
        {
            _mesh = mesh;
            _indexCountPerInstance = mesh != null ? mesh.GetIndexCount(0) : 0u;
            _offset = offset;
            _items = RentItemList(0);

            _commandBufA = CreateCommandBuffer();
            _commandDataA = new GraphicsBuffer.IndirectDrawIndexedArgs[CommandCount];
            _rpA = CreateRenderParams(material);

            _commandBufB = CreateCommandBuffer();
            _commandDataB = new GraphicsBuffer.IndirectDrawIndexedArgs[CommandCount];
            _rpB = CreateRenderParams(material);
        }

        ~RenderObject()
        {
            Release();
        }

        /// <summary>
        /// 释放 GPU 资源。可被外部主动调用，也会在析构时兜底调用。
        /// </summary>
        public void Release()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _instancesBuffer?.Release();
            _instancesBuffer = null;
            _commandBufA?.Release();
            _commandBufA = null;
            _commandBufB?.Release();
            _commandBufB = null;
            ReturnItemList(_items);
            _items = null;
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
            if (inputCount >= ThreadPoolBuildThreshold)
            {
                await UniTask.SwitchToThreadPool();
                nextItems = BuildItems(itemTrans, itemState);
            }
            else
            {
                nextItems = BuildItems(itemTrans, itemState);
            }

            await UniTask.SwitchToMainThread();

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

            if (_items.Count == 0)
            {
                DisableCurrentWriteTargetRenderFlag();
            }
            else
            {
                UploadToCurrentWriteTarget();
            }

            // 下一帧切换到另一套缓冲渲染。
            _renderFromA = !_renderFromA;
        }

        /// <summary>
        /// 提交间接绘制。根据双缓冲状态，渲染“上一帧准备好”的缓冲。
        /// </summary>
        public void Render(bool render = true)
        {
            if (!render) return;

            if (_renderFromA)
            {
                if (_canRenderA)
                {
                    Graphics.RenderMeshIndirect(_rpA, _mesh, _commandBufA);
                }

                return;
            }

            if (_canRenderB)
            {
                Graphics.RenderMeshIndirect(_rpB, _mesh, _commandBufB);
            }
        }

        /// <summary>
        /// 每个实例上传到 GPU 的结构体。
        /// </summary>
        private struct ItemInstanceData
        {
            public Matrix4x4 Matrix;

            /// <summary>
            /// 结构体字节大小（16 个 float）。
            /// </summary>
            public static int Size()
            {
                return sizeof(float) * 4 * 4;
            }
        }

        /// <summary>
        /// 创建间接绘制命令缓冲。
        /// </summary>
        private static GraphicsBuffer CreateCommandBuffer()
        {
            return new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments,
                CommandCount,
                GraphicsBuffer.IndirectDrawIndexedArgs.size);
        }

        /// <summary>
        /// 创建并初始化渲染参数。
        /// </summary>
        private static RenderParams CreateRenderParams(Material material)
        {
            return new RenderParams(material)
            {
                worldBounds = DefaultWorldBounds,
                matProps = new MaterialPropertyBlock()
            };
        }

        /// <summary>
        /// 把当前实例列表写入 GPU，并同步更新间接绘制参数。
        /// </summary>
        private void UpdateBufferAndCommand(
            GraphicsBuffer.IndirectDrawIndexedArgs[] commandData,
            GraphicsBuffer commandBuffer,
            MaterialPropertyBlock matProps)
        {
            if (_instancesBuffer == null || _items.Count != _instancesBuffer.count)
            {
                _instancesBuffer?.Release();
                _instancesBuffer = new ComputeBuffer(_items.Count, ItemInstanceData.Size());
            }

            _instancesBuffer.SetData(_items);

            commandData[0].indexCountPerInstance = _indexCountPerInstance;
            // 必须使用筛选后的实例数，否则会出现“命令数 > 实际数据数”的绘制错误。
            commandData[0].instanceCount = (uint)_items.Count;
            commandBuffer.SetData(commandData);
            matProps.SetBuffer(PerInstanceItemData, _instancesBuffer);
        }

        /// <summary>
        /// 判断上传渲染数据所需资源是否完整。
        /// </summary>
        private bool CanUploadRenderData()
        {
            return _commandBufA != null && _commandBufB != null && _mesh != null;
        }

        /// <summary>
        /// 禁用当前“写入目标缓冲”对应的渲染标记。
        /// </summary>
        private void DisableCurrentWriteTargetRenderFlag()
        {
            if (_renderFromA)
            {
                _canRenderB = false;
                return;
            }

            _canRenderA = false;
        }

        /// <summary>
        /// 将实例数据上传到当前写入目标缓冲。
        /// </summary>
        private void UploadToCurrentWriteTarget()
        {
            if (_renderFromA)
            {
                UpdateBufferAndCommand(_commandDataB, _commandBufB, _rpB.matProps);
                _canRenderB = true;
                return;
            }

            UpdateBufferAndCommand(_commandDataA, _commandBufA, _rpA.matProps);
            _canRenderA = true;
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

                result.Add(new ItemInstanceData
                {
                    Matrix = itemTrans[i] * _offset,
                });
            }

            return result;
        }

        private void SwapItems(List<ItemInstanceData> nextItems)
        {
            if (nextItems == null)
            {
                nextItems = RentItemList(0);
            }

            var oldItems = _items;
            _items = nextItems;
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
}
