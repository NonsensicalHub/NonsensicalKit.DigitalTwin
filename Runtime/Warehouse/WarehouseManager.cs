using System;
using Cysharp.Threading.Tasks;
using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    /// <summary>
    /// 仓库分块粒度级别。
    /// </summary>
    public enum WarehouseChunkLevel
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    /// <summary>
    /// 运行时货位数据。
    /// </summary>
    public class RuntimeBinData
    {
        public Vector3 Pos;
        public bool ShowCargo;
        public Matrix4x4 CachedMatrix;
        public bool HasCachedMatrix;

        public RuntimeBinData(float posX, float posY, float posZ)
        {
            Pos = new Vector3(posX, posY, posZ);
            // 默认显示货物，避免初始化后全部不可见。
            ShowCargo = true;
            CachedMatrix = Matrix4x4.identity;
            HasCachedMatrix = false;
        }
    }

    /// <summary>
    /// 货位索引规则：x 为 layer，y 为 column，z 为 row，w 为 depth。
    /// 维度顺序为“层、列、排、深”。
    /// </summary>
    public class WarehouseManager : MonoBehaviour
    {
        private const int LowTargetChunkCount = 700;
        private const int MediumTargetChunkCount = 1300;
        private const int HighTargetChunkCount = 2200;

        [SerializeField] private string m_warehouseName;

        [SerializeField] private GameObject[] m_cargoPrefabs;
        [SerializeField] private GameObject m_highlightCargo;
        [SerializeField] private GameObject m_highlightIndicator;
        [SerializeField] private Camera m_renderCamera;
        [SerializeField] private WarehouseChunkLevel m_chunkLevel = WarehouseChunkLevel.Medium;
        [SerializeField, Min(0f)] private float m_chunkCullDistance = 0f;
        [SerializeField, Range(1, 6)] private int m_updateIntervalFrames = 2;
        [SerializeField] private bool m_logWarehousePerf;

        public bool Inited=>_inited;
        private bool _inited;
        
        private CargoConfig[] _cargoConfigs;
        private Matrix4x4 _ltwMatrix;
        private Array4<RuntimeBinData> _binData;
        private readonly object _lock1 = new object();
        private Quaternion _rot;
        private bool _binDataInited;
        private readonly Plane[] _frameFrustumPlanes = new Plane[6];
        private bool _hasPendingConfigUpdate;
        private int _nextConfigUpdateFrame;
        private int _dispatchedUpdateCount;
        private float _nextPerfLogTime;
        private bool _destroying;

        private void OnValidate()
        {
            if (m_chunkCullDistance < 0f) m_chunkCullDistance = 0f;
        }

        private void Awake()
        {
            Init().Forget();
        }

        private void Update()
        {
            if (_destroying)
            {
                return;
            }

            if (!HasCargoConfigs())
            {
                return;
            }

            Camera renderCamera = ResolveRenderCamera();
            var transformDirty = false;
            if (transform.localToWorldMatrix != _ltwMatrix)
            {
                _ltwMatrix = transform.localToWorldMatrix;
                transformDirty = true;
            }

            if (transformDirty)
            {
                RequestConfigUpdate();
            }

            Plane[] frustumPlanes = null;
            if (renderCamera != null)
            {
                GeometryUtility.CalculateFrustumPlanes(renderCamera, _frameFrustumPlanes);
                frustumPlanes = _frameFrustumPlanes;
            }

            foreach (var config in _cargoConfigs)
            {
                if (config == null)
                {
                    continue;
                }

                if (transformDirty)
                {
                    config.UpdateWarehouseTransform(_ltwMatrix, false);
                }

                config.RenderLoads(renderCamera, frustumPlanes);
            }

            TryExecuteScheduledConfigUpdate();
            LogPerfIfNeeded();
        }

        private void OnDestroy()
        {
            _destroying = true;
            _hasPendingConfigUpdate = false;
            if (!HasCargoConfigs())
            {
                return;
            }

            foreach (var item in _cargoConfigs)
            {
                if (item == null)
                {
                    continue;
                }

                item.Release();
            }
        }

        /// <summary>
        /// 设置某一列的偏移，不保存，一般用于移动动画
        /// </summary>
        public void SetColumnOffset(int columnIndex, Vector3 offset)
        {
            if (!CanProcessColumn(columnIndex))
            {
                return;
            }
            Quaternion rotation = transform.rotation;

            ApplyToColumn(columnIndex, (cellLocation, binData) =>
            {
                Matrix4x4 matrix = Matrix4x4.TRS(binData.Pos + offset, rotation, Vector3.one);
                for (var i = 0; i < _cargoConfigs.Length; i++)
                {
                    _cargoConfigs[i]?.SetNewState(cellLocation, matrix, binData.ShowCargo, false);
                }
            });

            QueueAllConfigsUpdate();
        }
        
        
        /// <summary>
        /// 设置某一列的显示状态，不保存，一般用于动画
        /// </summary>
        public void SetColumnState(int columnIndex, bool state)
        {
            if (!CanProcessColumn(columnIndex))
            {
                return;
            }
            Quaternion rotation = transform.rotation;

            ApplyToColumn(columnIndex, (cellLocation, binData) =>
            {
                Matrix4x4 matrix = Matrix4x4.TRS(binData.Pos, rotation, Vector3.one);
                for (var i = 0; i < _cargoConfigs.Length; i++)
                {
                    _cargoConfigs[i]?.SetNewState(cellLocation, matrix, state, false);
                }
            });

            QueueAllConfigsUpdate();
        }

        /// <summary>
        /// 批量更新格位坐标位置。
        /// </summary>
        public void SetCargoState(Int4[] cellsLocation, Vector3[] cellsPos, bool autoUpdate = false)
        {
            if (!HasCargoConfigs())
            {
                return;
            }

            if (!HasSameLengthData(cellsLocation, cellsPos))
            {
                Debug.LogWarning("[Warehouse] 批量位置更新参数无效，已忽略。");
                return;
            }

            for (int i = 0; i < cellsLocation.Length; i++)
            {
                SetCargoState(cellsLocation[i], cellsPos[i], false);
            }

            if (!autoUpdate)
            {
                return;
            }

            QueueAllConfigsUpdate();
        }

        /// <summary>
        /// 更新单个格位的坐标位置。
        /// </summary>
        public void SetCargoState(Int4 cellLocation, Vector3 cellPos, bool autoUpdate = false)
        {
            if (!HasCargoConfigs() || !TryGetBinData(cellLocation, out RuntimeBinData binData))
            {
                return;
            }

            Matrix4x4 matrix = Matrix4x4.TRS(cellPos, transform.rotation, Vector3.one);

            binData.Pos = cellPos;
            binData.CachedMatrix = matrix;
            binData.HasCachedMatrix = true;

            for (var i = 0; i < _cargoConfigs.Length; i++)
            {
                _cargoConfigs[i]?.SetNewState(cellLocation, matrix, binData.ShowCargo, false);
            }

            if (autoUpdate)
            {
                RequestConfigUpdate();
            }
        }

        /// <summary>
        /// 批量切换格位显示状态。
        /// </summary>
        public void SetCargoState(Int4[] cellsLocation, bool[] show, bool autoUpdate = false)
        {
            if (!HasCargoConfigs())
            {
                return;
            }

            if (!HasSameLengthData(cellsLocation, show))
            {
                Debug.LogWarning("[Warehouse] 批量显示更新参数无效，已忽略。");
                return;
            }

            for (int i = 0; i < cellsLocation.Length; i++)
            {
                SetCargoState(cellsLocation[i], show[i], false);
            }

            if (!autoUpdate)
            {
                return;
            }

            QueueAllConfigsUpdate();
        }

        /// <summary>
        /// 切换单个格位显示状态。
        /// </summary>
        public void SetCargoState(Int4 cellLocation, bool show, bool autoUpdate = true)
        {
            if (!HasCargoConfigs() || !TryGetBinData(cellLocation, out RuntimeBinData binData))
            {
                return;
            }

            if (binData.ShowCargo == show)
            {
                return;
            }

            Matrix4x4 matrix;
            if (show)
            {
                if (!binData.HasCachedMatrix)
                {
                    matrix = Matrix4x4.TRS(binData.Pos, transform.rotation, Vector3.one);
                    binData.CachedMatrix = matrix;
                    binData.HasCachedMatrix = true;
                }
                else
                {
                    matrix = binData.CachedMatrix;
                }
            }
            else
            {
                // 仅隐藏时不需要重新计算矩阵，直接复用缓存值。
                matrix = binData.CachedMatrix;
            }

            binData.ShowCargo = show;

            for (var i = 0; i < _cargoConfigs.Length; i++)
            {
                var config = _cargoConfigs[i];
                if (config == null)
                {
                    continue;
                }

                config.SetNewState(cellLocation, matrix, show, false);
            }

            if (autoUpdate)
            {
                RequestConfigUpdate();
            }
        }

        /// <summary>
        /// 通过货位坐标定位高光货位对象。
        /// </summary>
        /// <param name="cellLocation">货位坐标（层、列、排、深）。</param>
        /// <param name="setActive">是否自动设置高光对象显示状态。</param>
        /// <returns>定位成功返回 true。</returns>
        public bool LocateHighlightBin(Int4 cellLocation, bool setActive = true)
        {
            if (m_highlightCargo == null && m_highlightIndicator == null)
            {
                Debug.LogWarning("[Warehouse] 高光货位对象未配置（货物和指示物均为空）。");
                return false;
            }

            if (!TryGetBinData(cellLocation, out RuntimeBinData binData))
            {
                if (setActive)
                {
                    HideHighlightBin();
                }
                return false;
            }

            Vector3 worldPos = transform.TransformPoint(binData.Pos);
            Quaternion worldRot = transform.rotation;

            if (m_highlightCargo != null)
            {
                Transform highlightCargoTransform = m_highlightCargo.transform;
                highlightCargoTransform.position = worldPos;
                highlightCargoTransform.rotation = worldRot;
            }

            if (m_highlightIndicator != null)
            {
                Transform highlightIndicatorTransform = m_highlightIndicator.transform;
                highlightIndicatorTransform.position = worldPos;
                highlightIndicatorTransform.rotation = worldRot;
            }

            if (setActive)
            {
                if (m_highlightCargo != null)
                {
                    m_highlightCargo.SetActive(binData.ShowCargo);
                }

                if (m_highlightIndicator != null)
                {
                    m_highlightIndicator.SetActive(true);
                }
            }

            return true;
        }

        /// <summary>
        /// 通过四维坐标定位高光货位对象。
        /// </summary>
        public bool LocateHighlightBin(int layer, int column, int row, int depth, bool setActive = true)
        {
            return LocateHighlightBin(new Int4(layer, column, row, depth), setActive);
        }

        /// <summary>
        /// 隐藏高光货位对象。
        /// </summary>
        public void HideHighlightBin()
        {
            if (m_highlightCargo != null && m_highlightCargo.activeSelf)
            {
                m_highlightCargo.SetActive(false);
            }

            if (m_highlightIndicator != null && m_highlightIndicator.activeSelf)
            {
                m_highlightIndicator.SetActive(false);
            }
        }

        private void ApplyToColumn(int columnIndex, Action<Int4, RuntimeBinData> action)
        {
            for (int layer = 0; layer < _binData.Length0; layer++)
            {
                for (int row = 0; row < _binData.Length2; row++)
                {
                    for (int depth = 0; depth < _binData.Length3; depth++)
                    {
                        var cellLocation = new Int4(layer, columnIndex, row, depth);
                        if (TryGetBinData(cellLocation, out RuntimeBinData binData))
                        {
                            action(cellLocation, binData);
                        }
                    }
                }
            }
        }

        private void QueueAllConfigsUpdate()
        {
            RequestConfigUpdate();
        }

        private bool CanProcessColumn(int columnIndex)
        {
            if (!HasCargoConfigs() || !_binDataInited)
            {
                return false;
            }

            if (columnIndex < 0 || columnIndex >= _binData.Length1)
            {
                Debug.LogWarning($"[Warehouse] 非法列索引: {columnIndex}");
                return false;
            }

            return true;
        }

        private static bool HasSameLengthData<T1, T2>(T1[] first, T2[] second)
        {
            return first != null &&
                   second != null &&
                   first.Length > 0 &&
                   first.Length == second.Length;
        }

        private async UniTaskVoid Init()
        {
            await ReadDataFile();

            await InitCargo();
        }

        private async UniTask ReadDataFile()
        {
            var data = await BinDataIO.LoadFromStreamingAssetsAsync($"Warehouse/{m_warehouseName}.dat");
            if (data == null)
            {
                Debug.LogError($"[Warehouse] 读取仓库数据失败: {m_warehouseName}");
                return;
            }

            _binData = new Array4<RuntimeBinData>(data.Dimensions);


            Debug.Log(
                $"{m_warehouseName}尺寸为层{_binData.Length0}，列{_binData.Length1}，行{_binData.Length2}，深{_binData.Length3}");

            _binDataInited = true;
            foreach (var bin in data.Bins)
            {
                _binData[bin.Level, bin.Column, bin.Row, bin.Depth] = new RuntimeBinData(bin.PosX, bin.PosY, bin.PosZ);
            }
        }

        private async UniTask InitCargo()
        {
            try
            {
                if (!ValidateInitInputs())
                {
                    return;
                }

                _ltwMatrix = transform.localToWorldMatrix;
                _cargoConfigs = new CargoConfig[m_cargoPrefabs.Length];
                var chunkSize = GetAutoChunkSize(_binData.Size);
                Debug.Log(
                    $"[Warehouse] ChunkLevel={m_chunkLevel}, ChunkSize=({chunkSize.X},{chunkSize.Y},{chunkSize.Z},{chunkSize.W}), Dim=({_binData.Size.X},{_binData.Size.Y},{_binData.Size.Z},{_binData.Size.W})");
                for (int i = 0; i < m_cargoPrefabs.Length; i++)
                {
                    _cargoConfigs[i] =
                        new CargoConfig(_binData.Size, m_cargoPrefabs[i], chunkSize, m_chunkCullDistance);
                    _cargoConfigs[i].UpdateWarehouseTransform(transform.localToWorldMatrix, false);
                }

                _rot = transform.rotation;

                await UniTask.RunOnThreadPool(InitConfig);

                foreach (var item in _cargoConfigs)
                {
                    item?.UpdateParts().Forget();
                }

                await UniTask.SwitchToMainThread();
                _inited=true;
            }
            catch (Exception e)
            {
                Debug.LogError($"GPU实例模型初始化异常: {e}");
            }
        }
        
        private void InitConfig()
        {
            lock (_lock1)
            {
                for (var layer = 0; layer < _binData.Length0; layer++)
                {
                    for (var column = 0; column < _binData.Length1; column++)
                    {
                        for (var row = 0; row < _binData.Length2; row++)
                        {
                            for (var depth = 0; depth < _binData.Length3; depth++)
                            {
                                if (_binData[layer, column, row, depth] == null)
                                {
                                    // 该位置无货位，跳过后续处理。
                                    continue;
                                }

                                RuntimeBinData binData = _binData[layer, column, row, depth];
                                bool show = GetMatrix(binData, out var matrix);

                                for (int i = 0; i < m_cargoPrefabs.Length; i++)
                                {
                                    _cargoConfigs[i].SetNewState(layer, column, row, depth, matrix, show, false);
                                }
                            }
                        }
                    }
                }
            }
        }

        private bool GetMatrix(RuntimeBinData mapping, out Matrix4x4 m4X4)
        {
            m4X4 = new Matrix4x4();

            if (!mapping.ShowCargo)
            {
                if (mapping.HasCachedMatrix)
                {
                    m4X4 = mapping.CachedMatrix;
                }
                return false;
            }

            m4X4 = Matrix4x4.TRS(mapping.Pos, _rot, Vector3.one);
            mapping.CachedMatrix = m4X4;
            mapping.HasCachedMatrix = true;
            return true;
        }

        private bool HasCargoConfigs()
        {
            return _cargoConfigs != null && _cargoConfigs.Length > 0;
        }

        private bool ValidateInitInputs()
        {
            if (!_binDataInited)
            {
                Debug.LogError("[Warehouse] 仓位数据未初始化，无法创建货物配置。");
                return false;
            }

            if (m_cargoPrefabs == null || m_cargoPrefabs.Length == 0)
            {
                Debug.LogError("[Warehouse] m_cargoPrefabs 为空，无法初始化渲染配置。");
                return false;
            }

            for (int i = 0; i < m_cargoPrefabs.Length; i++)
            {
                if (m_cargoPrefabs[i] == null)
                {
                    Debug.LogError($"[Warehouse] m_cargoPrefabs[{i}] 为空，请检查配置。");
                    return false;
                }
            }

            return true;
        }

        private bool TryGetBinData(Int4 location, out RuntimeBinData binData)
        {
            binData = null;
            if (!_binDataInited)
            {
                Debug.LogWarning("[Warehouse] 仓位数据尚未加载完成。");
                return false;
            }

            if (!IsValidCellLocation(location))
            {
                Debug.LogWarning($"[Warehouse] 非法货位索引: {location}");
                return false;
            }

            binData = _binData[location];
            if (binData != null)
            {
                return true;
            }

            Debug.LogWarning($"[Warehouse] 目标货位不存在: {location}");
            return false;
        }

        private bool IsValidCellLocation(Int4 location)
        {
            return location.X >= 0 && location.X < _binData.Length0 &&
                   location.Y >= 0 && location.Y < _binData.Length1 &&
                   location.Z >= 0 && location.Z < _binData.Length2 &&
                   location.W >= 0 && location.W < _binData.Length3;
        }

        private Camera ResolveRenderCamera()
        {
            if (m_renderCamera != null)
            {
                return m_renderCamera;
            }

            if (Camera.main != null)
            {
                return Camera.main;
            }

            return Camera.current;
        }

        private void RequestConfigUpdate(bool immediate = false)
        {
            if (_destroying)
            {
                return;
            }

            if (!HasCargoConfigs())
            {
                return;
            }

            if (immediate || m_updateIntervalFrames <= 1)
            {
                ExecuteConfigUpdate();
                return;
            }

            _hasPendingConfigUpdate = true;
            int targetFrame = Time.frameCount + Mathf.Max(1, m_updateIntervalFrames) - 1;
            if (_nextConfigUpdateFrame < Time.frameCount)
            {
                _nextConfigUpdateFrame = targetFrame;
            }
        }

        private void TryExecuteScheduledConfigUpdate()
        {
            if (!_hasPendingConfigUpdate)
            {
                return;
            }

            if (Time.frameCount < _nextConfigUpdateFrame)
            {
                return;
            }

            ExecuteConfigUpdate();
        }

        private void ExecuteConfigUpdate()
        {
            _hasPendingConfigUpdate = false;
            _nextConfigUpdateFrame = Time.frameCount + Mathf.Max(1, m_updateIntervalFrames);
            _dispatchedUpdateCount++;

            for (var i = 0; i < _cargoConfigs.Length; i++)
            {
                _cargoConfigs[i]?.UpdateParts().Forget();
            }
        }

        private void LogPerfIfNeeded()
        {
            if (!m_logWarehousePerf || !HasCargoConfigs())
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < _nextPerfLogTime)
            {
                return;
            }

            int totalChunks = 0;
            int updatedChunks = 0;
            int updateTasks = 0;
            for (int i = 0; i < _cargoConfigs.Length; i++)
            {
                var config = _cargoConfigs[i];
                if (config == null)
                {
                    continue;
                }

                totalChunks += config.ChunkCount;
                updatedChunks += config.LastUpdatedChunkCount;
                updateTasks += config.LastUpdateTaskCount;
            }

            Debug.Log(
                $"[Warehouse][Perf] intervalFrames={Mathf.Max(1, m_updateIntervalFrames)}, dispatchedUpdates={_dispatchedUpdateCount}/s, updatedChunks={updatedChunks}, totalChunks={totalChunks}, updateTasks={updateTasks}, pending={_hasPendingConfigUpdate}");

            _dispatchedUpdateCount = 0;
            _nextPerfLogTime = now + 1f;
        }

        private Int4 GetAutoChunkSize(Int4 dimensions)
        {
            int targetChunkCount = GetTargetChunkCount(m_chunkLevel);
            return BuildChunkSizeByTarget(dimensions, targetChunkCount);
        }

        private static int GetTargetChunkCount(WarehouseChunkLevel level)
        {
            switch (level)
            {
                case WarehouseChunkLevel.Low:
                    return LowTargetChunkCount;
                case WarehouseChunkLevel.High:
                    return HighTargetChunkCount;
                default:
                    return MediumTargetChunkCount;
            }
        }

        private static Int4 BuildChunkSizeByTarget(Int4 dimensions, int targetChunkCount)
        {
            int[] dimArray =
            {
                Mathf.Max(1, dimensions.X),
                Mathf.Max(1, dimensions.Y),
                Mathf.Max(1, dimensions.Z),
                Mathf.Max(1, dimensions.W)
            };

            int activeAxisCount = 0;
            double activeCellCount = 1d;
            for (int i = 0; i < dimArray.Length; i++)
            {
                if (dimArray[i] <= 1)
                {
                    continue;
                }

                activeAxisCount++;
                activeCellCount *= dimArray[i];
            }

            if (activeAxisCount == 0)
            {
                return new Int4(1, 1, 1, 1);
            }

            double target = Mathf.Max(1, targetChunkCount);
            double chunkScale = Math.Pow(target / activeCellCount, 1d / activeAxisCount);

            int[] chunkSize = new int[4];
            for (int i = 0; i < dimArray.Length; i++)
            {
                int axisDim = dimArray[i];
                if (axisDim <= 1)
                {
                    chunkSize[i] = 1;
                    continue;
                }

                int desiredChunkCount = Mathf.Clamp(
                    Mathf.RoundToInt((float)(axisDim * chunkScale)),
                    1,
                    axisDim);
                chunkSize[i] = Mathf.Max(1, Mathf.CeilToInt(axisDim / (float)desiredChunkCount));
            }

            return new Int4(chunkSize[0], chunkSize[1], chunkSize[2], chunkSize[3]);
        }
    }
}
