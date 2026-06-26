using System;
using Cysharp.Threading.Tasks;
using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    internal sealed class WarehouseBinDataStore
    {
        private Array4<RuntimeBinData> _binData;
        private bool HasBinData => _binData.m_Array != null;

        public bool IsReady { get; private set; }
        public Int4 Size => HasBinData ? _binData.Size : new Int4(0, 0, 0, 0);
        public int LayerCount => HasBinData ? _binData.Length0 : 0;
        public int ColumnCount => HasBinData ? _binData.Length1 : 0;
        public int RowCount => HasBinData ? _binData.Length2 : 0;
        public int DepthCount => HasBinData ? _binData.Length3 : 0;
        
        public async UniTask<bool> LoadAsync(string warehouseName)
        {
            var data = await BinDataIO.LoadFromStreamingAssetsAsync($"Warehouse/{warehouseName}.dat");
            if (data == null)
            {
                Debug.LogError($"[Warehouse] 读取仓库数据失败: {warehouseName}");
                IsReady = false;
                _binData = default;
                return false;
            }

            SetData(data);
            Debug.Log($"{warehouseName}尺寸为层{LayerCount}，列{ColumnCount}，行{RowCount}，深{DepthCount}");
            return true;
        }

        internal void SetData(WarehouseData data)
        {
            if (data == null)
            {
                IsReady = false;
                _binData = default;
                return;
            }

            _binData = new Array4<RuntimeBinData>(data.Dimensions);
            foreach (var bin in data.Bins)
            {
                _binData[bin.Level, bin.Column, bin.Row, bin.Depth] = new RuntimeBinData(bin.PosX, bin.PosY, bin.PosZ);
            }

            IsReady = true;
        }

        public bool TryGet(Int4 location, out RuntimeBinData binData)
        {
            binData = null;
            if (!IsReady)
            {
                Debug.LogWarning("[Warehouse] 仓位数据尚未加载完成。");
                return false;
            }

            if (!IsValidLocation(location))
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

        public bool IsColumnInRange(int columnIndex)
        {
            return columnIndex >= 0 && columnIndex < ColumnCount;
        }

        public bool IsValidLocation(Int4 location)
        {
            if (!IsReady)
            {
                return false;
            }

            return location.X >= 0 && location.X < LayerCount &&
                   location.Y >= 0 && location.Y < ColumnCount &&
                   location.Z >= 0 && location.Z < RowCount &&
                   location.W >= 0 && location.W < DepthCount;
        }

        public void ApplyToColumn(int columnIndex, Action<Int4, RuntimeBinData> action)
        {
            if (!IsReady || action == null || !IsColumnInRange(columnIndex))
            {
                return;
            }

            for (int layer = 0; layer < LayerCount; layer++)
            {
                for (int row = 0; row < RowCount; row++)
                {
                    for (int depth = 0; depth < DepthCount; depth++)
                    {
                        RuntimeBinData binData = _binData[layer, columnIndex, row, depth];
                        if (binData == null)
                        {
                            continue;
                        }

                        action(new Int4(layer, columnIndex, row, depth), binData);
                    }
                }
            }
        }

        public void ForEachBin(Action<int, int, int, int, RuntimeBinData> action)
        {
            if (!IsReady || action == null)
            {
                return;
            }

            for (int layer = 0; layer < LayerCount; layer++)
            {
                for (int column = 0; column < ColumnCount; column++)
                {
                    for (int row = 0; row < RowCount; row++)
                    {
                        for (int depth = 0; depth < DepthCount; depth++)
                        {
                            RuntimeBinData binData = _binData[layer, column, row, depth];
                            if (binData == null)
                            {
                                continue;
                            }

                            action(layer, column, row, depth, binData);
                        }
                    }
                }
            }
        }
    }
}
