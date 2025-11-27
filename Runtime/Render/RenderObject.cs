using System;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Render
{
    public class RenderObject : IDisposable
    {
        private static readonly int PerInstanceItemData = Shader.PropertyToID("_PerInstanceItemData");
        private const int CommandCount = 1;
        private const float BoundsSize = 100000f;

        private readonly Matrix4x4 _offset;

        // 双缓冲相关
        private readonly RenderBuffer _bufferA;
        private readonly RenderBuffer _bufferB;
        private bool _useBufferA = false;

        private readonly List<ItemInstanceData> _activeItems = new List<ItemInstanceData>();

        // 共享的实例数据缓冲区
        private ComputeBuffer _instancesBuffer;

        private bool _disposed = false;

        public RenderObject(Mesh mesh, Material material, Matrix4x4 offset)
        {
            _bufferA = new RenderBuffer(mesh, material);
            _bufferB = new RenderBuffer(mesh, material);

            _offset = offset;
        }

        /// <summary>
        /// 步骤1：计算实例数据（可在子线程执行）
        /// </summary>
        public void UpdateItemsStep1(Matrix4x4[] itemTrans, bool[] itemState)
        {
            if (itemTrans == null || itemState == null)
                throw new ArgumentNullException("itemTrans or itemState is null");

            if (itemTrans.Length != itemState.Length)
                throw new ArgumentException("itemTrans and itemState must have same length");

            _activeItems.Clear();

            // 只处理激活的物体
            for (int i = 0; i < itemTrans.Length; i++)
            {
                if (itemState[i])
                {
                    _activeItems.Add(new ItemInstanceData
                    {
                        Matrix = itemTrans[i] * _offset
                    });
                }
            }
        }

        /// <summary>
        /// 步骤2：更新 GPU 缓冲区（必须在主线程执行）
        /// </summary>
        public void UpdateItemsStep2()
        {
            if (_disposed) return;

            var currentBuffer = _useBufferA ? _bufferB : _bufferA;
            var itemCount = _activeItems.Count;

            if (itemCount == 0)
            {
                currentBuffer.CanRender = false;
                _useBufferA = !_useBufferA;
                return;
            }

            // 重建或更新实例缓冲区
            if (_instancesBuffer == null || itemCount != _instancesBuffer.count)
            {
                _instancesBuffer?.Release();
                _instancesBuffer = new ComputeBuffer(itemCount, ItemInstanceData.Size());
            }

            _instancesBuffer.SetData(_activeItems);

            // 更新命令缓冲区和材质属性
            currentBuffer.UpdateCommandBuffer((uint)itemCount);
            currentBuffer.SetInstanceBuffer(_instancesBuffer);
            currentBuffer.CanRender = true;

            _useBufferA = !_useBufferA;
        }

        /// <summary>
        /// 渲染当前缓冲区
        /// </summary>
        public void Render(bool shouldRender = true)
        {
            if (!shouldRender || _disposed) return;

            var renderBuffer = _useBufferA ? _bufferA : _bufferB;
            renderBuffer.Render();
        }

        public void Dispose()
        {
            if (_disposed) return;

            _instancesBuffer?.Dispose();
            _bufferA?.Dispose();
            _bufferB?.Dispose();
            _activeItems?.Clear();

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #region 嵌套类型

        /// <summary>
        /// 实例数据结构
        /// </summary>
        private struct ItemInstanceData
        {
            public Matrix4x4 Matrix;

            public static int Size() => sizeof(float) * 16; // 4x4 matrix
        }

        /// <summary>
        /// 渲染缓冲区封装
        /// </summary>
        private class RenderBuffer : IDisposable
        {
            private readonly RenderParams _renderParams;
            private readonly GraphicsBuffer _commandBuffer;
            private readonly GraphicsBuffer.IndirectDrawIndexedArgs[] _commandData;
            private readonly Mesh _mesh;

            public bool CanRender { get; set; }

            public RenderBuffer(Mesh mesh, Material material)
            {
                _mesh = mesh;

                _commandBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.IndirectArguments,
                    CommandCount,
                    GraphicsBuffer.IndirectDrawIndexedArgs.size
                );

                _commandData = new GraphicsBuffer.IndirectDrawIndexedArgs[CommandCount];

                _renderParams = new RenderParams(material)
                {
                    worldBounds = new Bounds(Vector3.zero, Vector3.one * BoundsSize),
                    matProps = new MaterialPropertyBlock()
                };
            }

            public void UpdateCommandBuffer(uint instanceCount)
            {
                _commandData[0].indexCountPerInstance = _mesh.GetIndexCount(0);
                _commandData[0].instanceCount = instanceCount;
                _commandBuffer.SetData(_commandData);
            }

            public void SetInstanceBuffer(ComputeBuffer buffer)
            {
                _renderParams.matProps.SetBuffer(PerInstanceItemData, buffer);
            }

            public void Render()
            {
                if (CanRender && _commandBuffer != null)
                {
                    Graphics.RenderMeshIndirect(_renderParams, _mesh, _commandBuffer);
                }
            }

            public void Dispose()
            {
                _commandBuffer?.Dispose();
            }
        }

        #endregion
    }
}
