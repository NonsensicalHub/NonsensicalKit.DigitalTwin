using System;
using Cysharp.Threading.Tasks;
using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    /// <summary>
    /// 货位索引规则：x 为 layer，y 为 column，z 为 row，w 为 depth。
    /// 维度顺序为“层、列、排、深”。
    /// </summary>
    public sealed class WarehouseManager : NonsensicalMono
    {
        [SerializeField] private string m_warehouseName;
        [SerializeField] private GameObject[] m_cargoPrefabs;
        [SerializeField] private GameObject m_highlightCargo;
        [SerializeField] private GameObject m_highlightIndicator;
        [SerializeField] private Camera m_renderCamera;
        [SerializeField] private WarehouseChunkLevel m_chunkLevel = WarehouseChunkLevel.Medium;
        [SerializeField, Min(0f)] private float m_chunkCullDistance = 0f;
        [SerializeField, Range(1, 6)] private int m_updateIntervalFrames = 2;
        [SerializeField] private bool m_logWarehousePerf;

        public bool Inited => _inited;

        private bool _inited;
        private CargoConfig[] _cargoConfigs;
        private Matrix4x4 _ltwMatrix;
        private readonly Plane[] _frameFrustumPlanes = new Plane[6];
        private readonly WarehouseBinDataStore _binDataStore = new WarehouseBinDataStore();
        private readonly WarehouseUpdateScheduler _updateScheduler = new WarehouseUpdateScheduler();
        private WarehouseHighlightController _highlightController;
        private bool _destroying; 

        private float _nextPerfLogTime;

        private void OnValidate()
        {
            if (m_chunkCullDistance < 0f) m_chunkCullDistance = 0f;
        }

        private void Awake()
        {
            _highlightController = new WarehouseHighlightController(m_highlightCargo, m_highlightIndicator);


            Init().Forget();
        }

        private void Update()
        {
            if (_destroying || !HasCargoConfigs()) return;

            Camera renderCamera = ResolveRenderCamera();
            bool transformDirty = false;
            if (transform.localToWorldMatrix != _ltwMatrix)
            {
                _ltwMatrix = transform.localToWorldMatrix;
                transformDirty = true;
            }

            if (transformDirty) RequestConfigUpdate();

            Plane[] frustumPlanes = null;
            if (renderCamera != null)
            {
                GeometryUtility.CalculateFrustumPlanes(renderCamera, _frameFrustumPlanes);
                frustumPlanes = _frameFrustumPlanes;
            }

            foreach (var config in _cargoConfigs)
            {
                if (config == null) continue;
                if (transformDirty) config.UpdateWarehouseTransform(_ltwMatrix, false);
                config.RenderLoads(renderCamera, frustumPlanes);
            }

            TryExecuteScheduledConfigUpdate();
            LogPerfIfNeeded();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _destroying = true;
            _updateScheduler.ClearPending();
            if (!HasCargoConfigs()) return;
            foreach (var item in _cargoConfigs) item?.Release();
        }

        public void SetColumnOffset(int columnIndex, Vector3 offset)
        {
            if (!CanProcessColumn(columnIndex)) return;
            Quaternion rotation = transform.rotation;
            _binDataStore.ApplyToColumn(columnIndex, (cellLocation, binData) =>
            {
                Matrix4x4 matrix = Matrix4x4.TRS(binData.Pos + offset, rotation, Vector3.one);
                ApplyToConfigs(cellLocation, matrix, binData.ShowCargo);
            });

            RequestConfigUpdate();
        }

        public void SetColumnState(int columnIndex, bool state)
        {
            if (!CanProcessColumn(columnIndex)) return;
            Quaternion rotation = transform.rotation;
            _binDataStore.ApplyToColumn(columnIndex, (cellLocation, binData) =>
            {
                Matrix4x4 matrix = Matrix4x4.TRS(binData.Pos, rotation, Vector3.one);
                ApplyToConfigs(cellLocation, matrix, state);
            });

            RequestConfigUpdate();
        }

        public void SetCargoState(Int4[] cellsLocation, Vector3[] cellsPos, bool autoUpdate = false)
        {
            ApplyBatchCargoState(
                cellsLocation,
                cellsPos,
                "[Warehouse] 批量位置更新参数无效，已忽略。",
                (location, pos) => SetCargoState(location, pos, false),
                autoUpdate);
        }

        public void SetCargoState(Int4 cellLocation, Vector3 cellPos, bool autoUpdate = false)
        {
            if (!HasCargoConfigs() || !_binDataStore.TryGet(cellLocation, out RuntimeBinData binData)) return;
            Matrix4x4 matrix = Matrix4x4.TRS(cellPos, transform.rotation, Vector3.one);
            binData.Pos = cellPos;
            binData.CachedMatrix = matrix;
            binData.HasCachedMatrix = true;
            ApplyToConfigs(cellLocation, matrix, binData.ShowCargo);

            if (autoUpdate) RequestConfigUpdate();
        }

        public void SetCargoState(Int4[] cellsLocation, bool[] show, bool autoUpdate = false)
        {
            ApplyBatchCargoState(
                cellsLocation,
                show,
                "[Warehouse] 批量显示更新参数无效，已忽略。",
                (location, visible) => SetCargoState(location, visible, false),
                autoUpdate);
        }

        public void SetCargoState(Int4 cellLocation, bool show, bool autoUpdate = true)
        {
            if (!HasCargoConfigs() || !_binDataStore.TryGet(cellLocation, out RuntimeBinData binData)) return;
            if (binData.ShowCargo == show) return;

            Matrix4x4 matrix = ResolveCargoStateMatrix(binData, show);

            binData.ShowCargo = show;
            ApplyToConfigs(cellLocation, matrix, show);

            if (autoUpdate) RequestConfigUpdate();
        }

        public bool LocateHighlightBin(Int4 cellLocation)
        {
            if (!_highlightController.CanHighlight())
            {
                Debug.LogWarning("[Warehouse] 高光货位对象未配置（货物和指示物均为空）。");
                return false;
            }

            if (!_binDataStore.TryGet(cellLocation, out RuntimeBinData binData))
            {
                HideHighlightBin();
                return false;
            }

            return _highlightController.Locate(transform, binData);
        }

        public bool LocateHighlightBin(int layer, int column, int row, int depth)
        {
            return LocateHighlightBin(new Int4(layer, column, row, depth));
        }

        public void HideHighlightBin()
        {
            _highlightController.Hide();
        }

        private bool CanProcessColumn(int columnIndex)
        {
            if (!HasCargoConfigs() || !_binDataStore.IsReady) return false;
            if (_binDataStore.IsColumnInRange(columnIndex)) return true;
            Debug.LogWarning($"[Warehouse] 非法列索引: {columnIndex}");
            return false;
        }

        private static bool HasSameLengthData<T1, T2>(T1[] first, T2[] second)
        {
            return first != null && second != null && first.Length > 0 && first.Length == second.Length;
        }

        private void ApplyBatchCargoState<TValue>(
            Int4[] cellsLocation,
            TValue[] values,
            string invalidMessage,
            Action<Int4, TValue> applyAction,
            bool autoUpdate)
        {
            if (!HasCargoConfigs()) return;
            if (!HasSameLengthData(cellsLocation, values))
            {
                Debug.LogWarning(invalidMessage);
                return;
            }

            for (int i = 0; i < cellsLocation.Length; i++)
            {
                applyAction(cellsLocation[i], values[i]);
            }

            if (autoUpdate) RequestConfigUpdate();
        }

        private void ApplyToConfigs(Int4 cellLocation, Matrix4x4 matrix, bool show)
        {
            for (int i = 0; i < _cargoConfigs.Length; i++)
            {
                _cargoConfigs[i]?.SetNewState(cellLocation, matrix, show, false);
            }
        }

        private Matrix4x4 ResolveCargoStateMatrix(RuntimeBinData binData, bool show)
        {
            if (!show)
            {
                return binData.CachedMatrix;
            }

            if (binData.HasCachedMatrix)
            {
                return binData.CachedMatrix;
            }

            Matrix4x4 matrix = Matrix4x4.TRS(binData.Pos, transform.rotation, Vector3.one);
            binData.CachedMatrix = matrix;
            binData.HasCachedMatrix = true;
            return matrix;
        }

        private async UniTaskVoid Init()
        {
            if (_inited) return;
            bool loaded = await ReadDataFile();
            if (!loaded) return;
            await InitCargo();

            Subscribe();
        }
        
        private void Subscribe()
        {
            AddHandler<Int4, bool>("LocateHighlightBin", m_warehouseName, LocateHighlightBin);
            Subscribe("HideHighlightBin", m_warehouseName, HideHighlightBin);
        }


        private async UniTask<bool> ReadDataFile()
        {
            return await _binDataStore.LoadAsync(m_warehouseName);
        }

        private async UniTask InitCargo()
        {
            try
            {
                var initializer = new WarehouseCargoInitializer(
                    _binDataStore,
                    m_cargoPrefabs,
                    m_chunkLevel,
                    m_chunkCullDistance);
                if (!initializer.ValidateInputs(out string error))
                {
                    Debug.LogError(error);
                    return;
                }

                _ltwMatrix = transform.localToWorldMatrix;
                _cargoConfigs = initializer.CreateConfigs(_ltwMatrix);
                Quaternion initRotation = transform.rotation;
                await UniTask.RunOnThreadPool(() => initializer.BuildInitialStates(_cargoConfigs, initRotation));

                foreach (var item in _cargoConfigs) item?.UpdateParts().Forget();
                await UniTask.SwitchToMainThread();
                _inited = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"GPU实例模型初始化异常: {e}");
            }
        }

        private bool HasCargoConfigs()
        {
            return _cargoConfigs != null && _cargoConfigs.Length > 0;
        }

        private Camera ResolveRenderCamera()
        {
            if (m_renderCamera != null) return m_renderCamera;
            if (Camera.main != null) return Camera.main;
            return Camera.current;
        }

        private void RequestConfigUpdate(bool immediate = false)
        {
            if (_destroying || !HasCargoConfigs()) return;
            _updateScheduler.Request(m_updateIntervalFrames, immediate, ExecuteConfigUpdate);
        }

        private void TryExecuteScheduledConfigUpdate()
        {
            _updateScheduler.TryExecuteScheduled(m_updateIntervalFrames, ExecuteConfigUpdate);
        }

        private void ExecuteConfigUpdate()
        {
            _updateScheduler.NotifyExecuted(m_updateIntervalFrames);
            for (int i = 0; i < _cargoConfigs.Length; i++)
            {
                _cargoConfigs[i]?.UpdateParts().Forget();
            }
        }

        private void LogPerfIfNeeded()
        {
            if (!m_logWarehousePerf || !HasCargoConfigs()) return;
            float now = Time.unscaledTime;
            if (now < _nextPerfLogTime) return;

            int totalChunks = 0;
            int updatedChunks = 0;
            int updateTasks = 0;
            for (int i = 0; i < _cargoConfigs.Length; i++)
            {
                CargoConfig config = _cargoConfigs[i];
                if (config == null) continue;
                totalChunks += config.ChunkCount;
                updatedChunks += config.LastUpdatedChunkCount;
                updateTasks += config.LastUpdateTaskCount;
            }

            int dispatchedUpdateCount = _updateScheduler.ConsumeDispatchedCount();
            Debug.Log(
                $"[Warehouse][Perf] intervalFrames={Mathf.Max(1, m_updateIntervalFrames)}, dispatchedUpdates={dispatchedUpdateCount}/s, updatedChunks={updatedChunks}, totalChunks={totalChunks}, updateTasks={updateTasks}, pending={_updateScheduler.HasPendingUpdate}");
            _nextPerfLogTime = now + 1f;
        }
    }
}
