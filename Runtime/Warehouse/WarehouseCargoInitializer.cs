using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    internal sealed class WarehouseCargoInitializer
    {
        private readonly WarehouseBinDataStore _binDataStore;
        private readonly GameObject[] _cargoPrefabs;
        private readonly WarehouseChunkLevel _chunkLevel;
        private readonly float _chunkCullDistance;
        private readonly object _buildLock = new object();

        public WarehouseCargoInitializer(
            WarehouseBinDataStore binDataStore,
            GameObject[] cargoPrefabs,
            WarehouseChunkLevel chunkLevel,
            float chunkCullDistance)
        {
            _binDataStore = binDataStore;
            _cargoPrefabs = cargoPrefabs;
            _chunkLevel = chunkLevel;
            _chunkCullDistance = chunkCullDistance;
        }

        public bool ValidateInputs(out string error)
        {
            error = null;
            if (_binDataStore == null || !_binDataStore.IsReady)
            {
                error = "[Warehouse] 仓位数据未初始化，无法创建货物配置。";
                return false;
            }

            if (_cargoPrefabs == null || _cargoPrefabs.Length == 0)
            {
                error = "[Warehouse] m_cargoPrefabs 为空，无法初始化渲染配置。";
                return false;
            }

            for (int i = 0; i < _cargoPrefabs.Length; i++)
            {
                if (_cargoPrefabs[i] == null)
                {
                    error = $"[Warehouse] m_cargoPrefabs[{i}] 为空，请检查配置。";
                    return false;
                }
            }

            return true;
        }

        public CargoConfig[] CreateConfigs(Matrix4x4 warehouseLtw)
        {
            Int4 chunkSize = WarehouseChunkStrategy.GetAutoChunkSize(_binDataStore.Size, _chunkLevel);
            Debug.Log(
                $"[Warehouse] ChunkLevel={_chunkLevel}, ChunkSize=({chunkSize.X},{chunkSize.Y},{chunkSize.Z},{chunkSize.W}), Dim=({_binDataStore.Size.X},{_binDataStore.Size.Y},{_binDataStore.Size.Z},{_binDataStore.Size.W})");

            var cargoConfigs = new CargoConfig[_cargoPrefabs.Length];
            for (int i = 0; i < _cargoPrefabs.Length; i++)
            {
                cargoConfigs[i] = new CargoConfig(_binDataStore.Size, _cargoPrefabs[i], chunkSize, _chunkCullDistance);
                cargoConfigs[i].UpdateWarehouseTransform(warehouseLtw, false);
            }

            return cargoConfigs;
        }

        public void BuildInitialStates(CargoConfig[] cargoConfigs, Quaternion warehouseRotation)
        {
            if (cargoConfigs == null || cargoConfigs.Length == 0)
            {
                return;
            }

            lock (_buildLock)
            {
                _binDataStore.ForEachBin((layer, column, row, depth, binData) =>
                {
                    bool show = TryBuildMatrix(binData, warehouseRotation, out Matrix4x4 matrix);
                    for (int i = 0; i < cargoConfigs.Length; i++)
                    {
                        cargoConfigs[i].SetNewState(layer, column, row, depth, matrix, show, false);
                    }
                });
            }
        }

        private static bool TryBuildMatrix(RuntimeBinData binData, Quaternion warehouseRotation, out Matrix4x4 matrix)
        {
            matrix = new Matrix4x4();
            if (!binData.ShowCargo)
            {
                if (binData.HasCachedMatrix)
                {
                    matrix = binData.CachedMatrix;
                }

                return false;
            }

            matrix = Matrix4x4.TRS(binData.Pos, warehouseRotation, Vector3.one);
            binData.CachedMatrix = matrix;
            binData.HasCachedMatrix = true;
            return true;
        }
    }
}
