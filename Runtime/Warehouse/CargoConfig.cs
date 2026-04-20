using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    /// <summary>
    /// 货物渲染配置，负责分块数据组织、可见性裁剪与实例渲染更新。
    /// </summary>
    public class CargoConfig
    {
        private const float ChunkBoundsPadding = 1.0f;

        private readonly GameObject _prefab;
        private readonly Int4 _cellCount;
        private readonly Int4 _chunkSize;
        private readonly float _maxCullDistance;

        private List<PartTemplate> _partTemplates;
        private List<RenderChunk> _chunks;
        private Array4<Matrix4x4> _loadTrans;
        private Array4<bool> _loadStates;
        private Array4<bool> _hasBins;
        private Matrix4x4 _ltw;
        private bool _chunkBoundsDirty = true;
        private readonly Plane[] _frustumPlanesCache = new Plane[6];

        private readonly object _updateLock = new object();
        private int _updateQueued;
        private int _updateLoopRunning;
        private UniTask[] _updateTasksCache = Array.Empty<UniTask>();

        public CargoConfig(Int4 cellCount, GameObject prefab, Int4 chunkSize, float maxCullDistance)
        {
            _cellCount = cellCount;
            _loadTrans = new Array4<Matrix4x4>(cellCount.X, cellCount.Y, cellCount.Z, cellCount.W);
            _loadStates = new Array4<bool>(cellCount.X, cellCount.Y, cellCount.Z, cellCount.W);
            _hasBins = new Array4<bool>(cellCount.X, cellCount.Y, cellCount.Z, cellCount.W);

            _prefab = prefab;
            _chunkSize = NormalizeChunkSize(chunkSize);
            _maxCullDistance = maxCullDistance;
            InitMeshes();
        }

        /// <summary>
        /// 渲染当前货架配置中的所有分块。
        /// </summary>
        public void RenderLoads(Camera renderCamera, bool render = true)
        {
            if (!HasChunks())
            {
                return;
            }

            if (!render)
            {
                return;
            }

            bool useCull = renderCamera != null;
            var cameraPosition = Vector3.zero;
            float maxCullDistanceSqr = _maxCullDistance * _maxCullDistance;
            if (useCull)
            {
                cameraPosition = renderCamera.transform.position;
                GeometryUtility.CalculateFrustumPlanes(renderCamera, _frustumPlanesCache);
            }

            if (_chunkBoundsDirty)
            {
                UpdateChunkWorldBounds();
                _chunkBoundsDirty = false;
            }

            foreach (var chunk in _chunks)
            {
                bool chunkVisible = IsChunkVisible(chunk, useCull, cameraPosition, maxCullDistanceSqr);
                if (!chunkVisible)
                {
                    continue;
                }

                foreach (var part in chunk.Parts)
                {
                    part.Render(true);
                }
            }
        }

        /// <summary>
        /// 释放该配置持有的 GPU 资源。
        /// </summary>
        public void Release()
        {
            if (!HasChunks())
            {
                return;
            }

            foreach (var chunk in _chunks)
            {
                foreach (var part in chunk.Parts)
                {
                    part.Release();
                }
            }
        }

        /// <summary>
        /// 按格位坐标更新货物状态。
        /// </summary>
        public void SetNewState(Int4 location, Matrix4x4 trans, bool show, bool autoUpdate)
        {
            if (!HasPartTemplates())
            {
                return;
            }

            lock (_updateLock)
            {
                _loadTrans[location] = trans;
                _loadStates[location] = show;
                _hasBins[location] = true;
            }

            if (autoUpdate)
            {
                UpdateParts().Forget();
            }
        }

        /// <summary>
        /// 按四维索引更新货物状态。
        /// </summary>
        public void SetNewState(int layer, int column, int row, int depth, Matrix4x4 trans, bool show, bool autoUpdate)
        {
            if (!HasPartTemplates())
            {
                return;
            }

            lock (_updateLock)
            {
                _loadTrans[layer, column, row, depth] = trans;
                _loadStates[layer, column, row, depth] = show;
                _hasBins[layer, column, row, depth] = true;
            }

            if (autoUpdate)
            {
                UpdateParts().Forget();
            }
        }

        /// <summary>
        /// 更新仓库整体变换矩阵（影响所有实例的世界坐标）。
        /// </summary>
        public void UpdateWarehouseTransform(Matrix4x4 ltw, bool autoUpdate)
        {
            if (!HasPartTemplates())
            {
                return;
            }

            lock (_updateLock)
            {
                if (_ltw == ltw)
                {
                    return;
                }

                _ltw = ltw;
                _chunkBoundsDirty = true;
            }

            if (autoUpdate)
            {
                UpdateParts().Forget();
            }
        }

        /// <summary>
        /// 执行分块构建与实例数据上传。
        /// </summary>
        public async UniTask UpdateParts()
        {
            if (!HasPartTemplates())
            {
                return;
            }

            // 高频触发时只保留“至少再来一轮更新”的信号，避免更新任务堆积。
            Interlocked.Exchange(ref _updateQueued, 1);
            if (Interlocked.CompareExchange(ref _updateLoopRunning, 1, 0) != 0)
            {
                return;
            }

            try
            {
                while (Interlocked.Exchange(ref _updateQueued, 0) == 1)
                {
                    InitializeChunksIfNeeded();
                    await UniTask.SwitchToThreadPool();
                    BuildChunkRenderInput();
                    await UniTask.SwitchToMainThread();

                    int taskCount = GetUpdateTaskCount();
                    EnsureTaskCacheCapacity(taskCount);
                    int taskIndex = 0;
                    foreach (var chunk in _chunks)
                    {
                        foreach (var part in chunk.Parts)
                        {
                            _updateTasksCache[taskIndex++] = part.UpdateItems(chunk.Transforms, chunk.States);
                        }
                    }

                    if (taskIndex > 0)
                    {
                        await UniTask.WhenAll(_updateTasksCache);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _updateLoopRunning, 0);
                if (Interlocked.Exchange(ref _updateQueued, 0) == 1)
                {
                    UpdateParts().Forget();
                }
            }
        }

        private void InitMeshes()
        {
            _partTemplates = new List<PartTemplate>();
            if (_prefab == null)
            {
                Debug.LogError("[Warehouse] CargoConfig 初始化失败：预制体为空。");
                return;
            }

            var meshes = _prefab.GetComponentsInChildren<MeshFilter>();

            foreach (var item in meshes)
            {
                if (item == null || item.sharedMesh == null)
                {
                    continue;
                }

                if (item.gameObject.TryGetComponent<MeshRenderer>(out var renderer))
                {
                    if (renderer == null || renderer.sharedMaterials == null)
                    {
                        continue;
                    }

                    if (item.sharedMesh.subMeshCount < renderer.sharedMaterials.Length)
                    {
                        Debug.LogWarning(
                            $"[Warehouse] 子网格数量不足，已跳过: {item.name}, subMesh={item.sharedMesh.subMeshCount}, materials={renderer.sharedMaterials.Length}");
                        continue;
                    }

                    for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                    {
                        if (renderer.sharedMaterials[i] == null)
                        {
                            continue;
                        }

                        _partTemplates.Add(new PartTemplate(
                            item.sharedMesh,
                            renderer.sharedMaterials[i],
                            _prefab.transform.worldToLocalMatrix * item.transform.localToWorldMatrix));
                    }
                }
            }
        }

        private void BuildChunkRenderInput()
        {
            lock (_updateLock)
            {
                Matrix4x4 ltw = _ltw;
                foreach (var chunk in _chunks)
                {
                    int index = 0;
                    for (int layer = chunk.LayerStart; layer < chunk.LayerEnd; layer++)
                    {
                        for (int column = chunk.ColumnStart; column < chunk.ColumnEnd; column++)
                        {
                            for (int row = chunk.RowStart; row < chunk.RowEnd; row++)
                            {
                                for (int depth = chunk.DepthStart; depth < chunk.DepthEnd; depth++)
                                {
                                    bool hasBin = _hasBins[layer, column, row, depth];
                                    chunk.Transforms[index] = ltw * _loadTrans[layer, column, row, depth];
                                    chunk.States[index] = hasBin && _loadStates[layer, column, row, depth];
                                    index++;
                                }
                            }
                        }
                    }
                }
            }
        }

        private void InitializeChunksIfNeeded()
        {
            if (_chunks != null)
            {
                return;
            }

            _chunks = new List<RenderChunk>();

            int chunkLayerSize = _chunkSize.X;
            int chunkColumnSize = _chunkSize.Y;
            int chunkRowSize = _chunkSize.Z;
            int chunkDepthSize = _chunkSize.W;

            for (int layerStart = 0; layerStart < _cellCount.X; layerStart += chunkLayerSize)
            {
                int layerEnd = Math.Min(layerStart + chunkLayerSize, _cellCount.X);
                for (int columnStart = 0; columnStart < _cellCount.Y; columnStart += chunkColumnSize)
                {
                    int columnEnd = Math.Min(columnStart + chunkColumnSize, _cellCount.Y);
                    for (int rowStart = 0; rowStart < _cellCount.Z; rowStart += chunkRowSize)
                    {
                        int rowEnd = Math.Min(rowStart + chunkRowSize, _cellCount.Z);
                        for (int depthStart = 0; depthStart < _cellCount.W; depthStart += chunkDepthSize)
                        {
                            int depthEnd = Math.Min(depthStart + chunkDepthSize, _cellCount.W);
                            var chunk = CreateChunk(layerStart, layerEnd, columnStart, columnEnd, rowStart, rowEnd,
                                depthStart, depthEnd);
                            if (chunk != null)
                            {
                                _chunks.Add(chunk);
                            }
                        }
                    }
                }
            }

            _chunkBoundsDirty = true;
        }

        private RenderChunk CreateChunk(
            int layerStart,
            int layerEnd,
            int columnStart,
            int columnEnd,
            int rowStart,
            int rowEnd,
            int depthStart,
            int depthEnd)
        {
            Bounds localBounds = BuildChunkLocalBounds(layerStart, layerEnd, columnStart, columnEnd, rowStart, rowEnd,
                depthStart, depthEnd, out bool hasValidCell);
            if (!hasValidCell)
            {
                return null;
            }

            int capacity = (layerEnd - layerStart) * (columnEnd - columnStart) * (rowEnd - rowStart) *
                           (depthEnd - depthStart);
            var transforms = new Matrix4x4[capacity];
            var states = new bool[capacity];
            var parts = new List<RenderObject>(_partTemplates.Count);
            foreach (var template in _partTemplates)
            {
                parts.Add(new RenderObject(template.Mesh, template.Material, template.Offset));
            }

            return new RenderChunk(
                layerStart,
                layerEnd,
                columnStart,
                columnEnd,
                rowStart,
                rowEnd,
                depthStart,
                depthEnd,
                transforms,
                states,
                parts,
                localBounds,
                hasValidCell);
        }

        private void EnsureTaskCacheCapacity(int length)
        {
            if (_updateTasksCache.Length == length)
            {
                return;
            }

            _updateTasksCache = new UniTask[length];
        }

        private int GetUpdateTaskCount()
        {
            if (!HasChunks() || !HasPartTemplates())
            {
                return 0;
            }

            return _chunks.Count * _partTemplates.Count;
        }

        private void UpdateChunkWorldBounds()
        {
            if (!HasChunks())
            {
                return;
            }

            foreach (var chunk in _chunks)
            {
                chunk.WorldBounds = TransformBounds(_ltw, chunk.LocalBounds);
            }
        }

        private bool IsChunkVisible(RenderChunk chunk, bool useCull, Vector3 cameraPosition, float maxCullDistanceSqr)
        {
            if (!chunk.HasValidCell)
            {
                return false;
            }

            if (!useCull)
            {
                return true;
            }

            if (_maxCullDistance > 0f)
            {
                Vector3 nearestPoint = chunk.WorldBounds.ClosestPoint(cameraPosition);
                if ((nearestPoint - cameraPosition).sqrMagnitude > maxCullDistanceSqr)
                {
                    return false;
                }
            }

            return GeometryUtility.TestPlanesAABB(_frustumPlanesCache, chunk.WorldBounds);
        }

        private Bounds BuildChunkLocalBounds(
            int layerStart,
            int layerEnd,
            int columnStart,
            int columnEnd,
            int rowStart,
            int rowEnd,
            int depthStart,
            int depthEnd,
            out bool hasValidCell)
        {
            Vector3 min = default;
            Vector3 max = default;
            bool hasAny = false;

            lock (_updateLock)
            {
                for (int layer = layerStart; layer < layerEnd; layer++)
                {
                    for (int column = columnStart; column < columnEnd; column++)
                    {
                        for (int row = rowStart; row < rowEnd; row++)
                        {
                            for (int depth = depthStart; depth < depthEnd; depth++)
                            {
                                if (!_hasBins[layer, column, row, depth])
                                {
                                    continue;
                                }

                                Vector3 pos = ExtractTranslation(_loadTrans[layer, column, row, depth]);
                                if (!hasAny)
                                {
                                    min = pos;
                                    max = pos;
                                    hasAny = true;
                                }
                                else
                                {
                                    min = Vector3.Min(min, pos);
                                    max = Vector3.Max(max, pos);
                                }
                            }
                        }
                    }
                }
            }

            hasValidCell = hasAny;
            if (!hasAny)
            {
                return new Bounds(Vector3.zero, Vector3.zero);
            }

            Vector3 size = (max - min) + Vector3.one * ChunkBoundsPadding;
            size.x = Mathf.Max(size.x, ChunkBoundsPadding);
            size.y = Mathf.Max(size.y, ChunkBoundsPadding);
            size.z = Mathf.Max(size.z, ChunkBoundsPadding);
            return new Bounds((min + max) * 0.5f, size);
        }

        private static Vector3 ExtractTranslation(Matrix4x4 matrix)
        {
            return new Vector3(matrix.m03, matrix.m13, matrix.m23);
        }

        private static Int4 NormalizeChunkSize(Int4 chunkSize)
        {
            return new Int4(
                Math.Max(1, chunkSize.X),
                Math.Max(1, chunkSize.Y),
                Math.Max(1, chunkSize.Z),
                Math.Max(1, chunkSize.W));
        }

        private static Bounds TransformBounds(Matrix4x4 matrix, Bounds localBounds)
        {
            Vector3 center = matrix.MultiplyPoint3x4(localBounds.center);
            Vector3 extents = localBounds.extents;
            Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
            Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
            Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));
            Vector3 worldExtents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(center, worldExtents * 2f);
        }

        private bool HasPartTemplates()
        {
            return _partTemplates != null && _partTemplates.Count > 0;
        }

        private bool HasChunks()
        {
            return _chunks != null && _chunks.Count > 0;
        }

        private readonly struct PartTemplate
        {
            public readonly Mesh Mesh;
            public readonly Material Material;
            public readonly Matrix4x4 Offset;

            public PartTemplate(Mesh mesh, Material material, Matrix4x4 offset)
            {
                Mesh = mesh;
                Material = material;
                Offset = offset;
            }
        }

        private sealed class RenderChunk
        {
            public readonly int LayerStart;
            public readonly int LayerEnd;
            public readonly int ColumnStart;
            public readonly int ColumnEnd;
            public readonly int RowStart;
            public readonly int RowEnd;
            public readonly int DepthStart;
            public readonly int DepthEnd;
            public readonly Matrix4x4[] Transforms;
            public readonly bool[] States;
            public readonly List<RenderObject> Parts;
            public readonly Bounds LocalBounds;
            public readonly bool HasValidCell;
            public Bounds WorldBounds;

            public RenderChunk(
                int layerStart,
                int layerEnd,
                int columnStart,
                int columnEnd,
                int rowStart,
                int rowEnd,
                int depthStart,
                int depthEnd,
                Matrix4x4[] transforms,
                bool[] states,
                List<RenderObject> parts,
                Bounds localBounds,
                bool hasValidCell)
            {
                LayerStart = layerStart;
                LayerEnd = layerEnd;
                ColumnStart = columnStart;
                ColumnEnd = columnEnd;
                RowStart = rowStart;
                RowEnd = rowEnd;
                DepthStart = depthStart;
                DepthEnd = depthEnd;
                Transforms = transforms;
                States = states;
                Parts = parts;
                LocalBounds = localBounds;
                HasValidCell = hasValidCell;
                WorldBounds = localBounds;
            }
        }
    }
}