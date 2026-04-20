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

        public RuntimeBinData(float posX, float posY, float posZ)
        {
            Pos = new Vector3(posX, posY, posZ);
            // 默认显示货物，避免初始化后全部不可见。
            ShowCargo = true;
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
        [SerializeField] private Camera m_renderCamera;
        [SerializeField] private WarehouseChunkLevel m_chunkLevel = WarehouseChunkLevel.Medium;
        [SerializeField, Min(0f)] private float m_chunkCullDistance = 0f;

        private CargoConfig[] _cargoConfigs;
        private Matrix4x4 _ltwMatrix;
        private Array4<RuntimeBinData> _binData;
        private readonly object _lock1 = new object();
        private Quaternion _rot;
        private bool _binDataInited;

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

            foreach (var config in _cargoConfigs)
            {
                if (config == null)
                {
                    continue;
                }

                if (transformDirty)
                {
                    config.UpdateWarehouseTransform(_ltwMatrix, true);
                }

                config.RenderLoads(renderCamera);
            }
        }

        private void OnDestroy()
        {
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
        /// 批量更新格位坐标位置。
        /// </summary>
        public void SetCargoState(Int4[] cellsLocation, Vector3[] cellsPos, bool autoUpdate = false)
        {
            if (!HasCargoConfigs())
            {
                return;
            }

            if (cellsLocation == null || cellsPos == null || cellsLocation.Length == 0 || cellsLocation.Length != cellsPos.Length)
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

            for (var i = 0; i < _cargoConfigs.Length; i++)
            {
                _cargoConfigs[i]?.UpdateParts().Forget();
            }
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

            for (var i = 0; i < _cargoConfigs.Length; i++)
            {
                _cargoConfigs[i]?.SetNewState(cellLocation, matrix, binData.ShowCargo, autoUpdate);
            }
        }

        /// <summary>
        /// 批量切换格位显示状态。
        /// </summary>
        public void SetCargoState(Int4[] cellsLocation, bool show, bool autoUpdate = false)
        {
            if (!HasCargoConfigs() || cellsLocation == null || cellsLocation.Length == 0)
            {
                return;
            }

            foreach (var item in cellsLocation)
            {
                SetCargoState(item, show, false);
            }

            if (!autoUpdate)
            {
                return;
            }

            for (var i = 0; i < _cargoConfigs.Length; i++)
            {
                _cargoConfigs[i]?.UpdateParts().Forget();
            }
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

            Matrix4x4 matrix = Matrix4x4.TRS(binData.Pos, transform.rotation, Vector3.one);

            binData.ShowCargo = show;

            for (var i = 0; i < _cargoConfigs.Length; i++)
            {
                var config = _cargoConfigs[i];
                if (config == null)
                {
                    continue;
                }

                config.SetNewState(cellLocation, matrix, show, false);
                if (autoUpdate)
                {
                    config.UpdateParts().Forget();
                }
            }
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
            _binDataInited = true;
            foreach (var bin in data.Bins)
            {
                _binData[bin.Row, bin.Column, bin.Level, bin.Depth] = new RuntimeBinData(bin.PosX, bin.PosY, bin.PosZ);
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
                await UniTask.SwitchToTaskPool();
                await UniTask.SwitchToThreadPool();

                await UniTask.RunOnThreadPool(InitConfig);

                Debug.Log(_binData.Size);
                foreach (var item in _cargoConfigs)
                {
                    item?.UpdateParts().Forget();
                }
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

                                for (int i = 0; i < m_cargoPrefabs.Length; i++)
                                {
                                    bool show = GetMatrix(_binData[layer, column, row, depth], out var matrix);
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
                return false;
            }

            m4X4 = Matrix4x4.TRS(mapping.Pos, _rot, Vector3.one);
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
