using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    /// <summary>
    /// Encodes warehouse cell coordinates into a non-zero 32-bit id for GPU picking.
    /// </summary>
    internal static class WarehousePickId
    {
        public const uint Miss = 0u;

        public static uint Encode(Int4 location, Int4 dimensions)
        {
            return Encode(location.X, location.Y, location.Z, location.W, dimensions);
        }

        public static uint Encode(int layer, int column, int row, int depth, Int4 dimensions)
        {
            if (!IsInRange(layer, column, row, depth, dimensions))
            {
                return Miss;
            }

            ulong linear =
                (ulong)depth +
                (ulong)dimensions.W * ((ulong)row +
                (ulong)dimensions.Z * ((ulong)column +
                (ulong)dimensions.Y * (ulong)layer));

            ulong id = linear + 1UL;
            return id > uint.MaxValue ? Miss : (uint)id;
        }

        public static bool TryDecode(uint pickId, Int4 dimensions, out Int4 location)
        {
            location = default;
            if (pickId == Miss || dimensions.X <= 0 || dimensions.Y <= 0 || dimensions.Z <= 0 || dimensions.W <= 0)
            {
                return false;
            }

            ulong linear = pickId - 1UL;
            int depth = (int)(linear % (ulong)dimensions.W);
            linear /= (ulong)dimensions.W;
            int row = (int)(linear % (ulong)dimensions.Z);
            linear /= (ulong)dimensions.Z;
            int column = (int)(linear % (ulong)dimensions.Y);
            linear /= (ulong)dimensions.Y;
            int layer = (int)linear;

            if (!IsInRange(layer, column, row, depth, dimensions))
            {
                return false;
            }

            location = new Int4(layer, column, row, depth);
            return true;
        }

        public static Color32 EncodeColor(uint pickId)
        {
            return new Color32(
                (byte)(pickId & 0xFFu),
                (byte)((pickId >> 8) & 0xFFu),
                (byte)((pickId >> 16) & 0xFFu),
                (byte)((pickId >> 24) & 0xFFu));
        }

        public static uint DecodeColor(Color32 color)
        {
            return (uint)color.r |
                   ((uint)color.g << 8) |
                   ((uint)color.b << 16) |
                   ((uint)color.a << 24);
        }

        private static bool IsInRange(int layer, int column, int row, int depth, Int4 dimensions)
        {
            return layer >= 0 && layer < dimensions.X &&
                   column >= 0 && column < dimensions.Y &&
                   row >= 0 && row < dimensions.Z &&
                   depth >= 0 && depth < dimensions.W;
        }
    }
}
