using System;
using NonsensicalKit.Core;
using UnityEngine;

/// <summary>
/// 一批同色高亮：第 i 批对应 WarehouseInventoryReview 动态创建的第 i 层。
/// </summary>
[Serializable]
public class WarehouseInventoryHighlightBatch
{
    public Int4[] Locations;
    public bool[] HighlightStates;
    public Color Color;

    public WarehouseInventoryHighlightBatch()
    {
    }

    public WarehouseInventoryHighlightBatch(Int4[] locations, bool[] highlightStates, Color color)
    {
        Locations = locations;
        HighlightStates = highlightStates;
        Color = color;
    }

    public bool IsValid =>
        Locations != null
        && HighlightStates != null
        && Locations.Length > 0
        && Locations.Length == HighlightStates.Length;

    public static WarehouseInventoryHighlightBatch CreateAllHighlighted(Int4[] locations, Color color)
    {
        if (locations == null || locations.Length == 0)
        {
            return new WarehouseInventoryHighlightBatch(
                Array.Empty<Int4>(),
                Array.Empty<bool>(),
                color);
        }

        var states = new bool[locations.Length];
        Array.Fill(states, true);
        return new WarehouseInventoryHighlightBatch(locations, states, color);
    }

    public static WarehouseInventoryHighlightBatch[] CreateAllHighlighted(
        Int4[][] locationBatches,
        Color[] batchColors)
    {
        if (locationBatches == null || locationBatches.Length == 0
                                    || batchColors == null || batchColors.Length == 0)
        {
            return Array.Empty<WarehouseInventoryHighlightBatch>();
        }

        var batches = new WarehouseInventoryHighlightBatch[locationBatches.Length];
        for (int i = 0; i < locationBatches.Length; i++)
        {
            Color color = batchColors[Mathf.Min(i, batchColors.Length - 1)];
            batches[i] = CreateAllHighlighted(locationBatches[i], color);
        }

        return batches;
    }
}
