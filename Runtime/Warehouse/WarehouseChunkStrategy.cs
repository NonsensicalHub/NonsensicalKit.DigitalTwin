using System;
using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    internal static class WarehouseChunkStrategy
    {
        private const int LowTargetChunkCount = 700;
        private const int MediumTargetChunkCount = 1300;
        private const int HighTargetChunkCount = 2200;

        public static Int4 GetAutoChunkSize(Int4 dimensions, WarehouseChunkLevel level)
        {
            int targetChunkCount = GetTargetChunkCount(level);
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
