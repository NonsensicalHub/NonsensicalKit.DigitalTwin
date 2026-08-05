using System;
using System.Collections.Generic;
using NonsensicalKit.Core;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine;

/// <summary>
/// 单色盘点叠加层：一个 WarehouseManager + 运行时材质 + 当前高亮格位。
/// </summary>
[Serializable]
public class WarehouseInventory
{
    #region 字段

    private static readonly int s_tintPropertyId = Shader.PropertyToID("_Color");

    [SerializeField] private WarehouseManager m_warehouseManager;
    [SerializeField] private Material m_material;
    [SerializeField] private Color m_color = Color.cyan;

    private readonly HashSet<Int4> _highlightedLocations = new();
    private readonly Dictionary<int, int> _highlightCountsByColumn = new();
    private Material[] _runtimeMaterials;
    private GameObject[] _runtimeCargoPrefabs;
    private bool _ownedRuntime;

    #endregion

    #region 属性

    public WarehouseManager WarehouseManager => m_warehouseManager;
    public Color Color => m_color;
    public bool HasHighlights => _highlightedLocations.Count > 0;
    public bool IsValid => m_warehouseManager != null;
    public bool Inited => m_warehouseManager != null && m_warehouseManager.Inited;
    public IReadOnlyCollection<Int4> HighlightedLocations => _highlightedLocations;

    #endregion

    #region 初始化

    public void SetupRuntime(
        WarehouseManager warehouseManager,
        Material[] runtimeMaterials,
        GameObject[] runtimeCargoPrefabs,
        Color color)
    {
        m_warehouseManager = warehouseManager;
        _runtimeMaterials = runtimeMaterials;
        _runtimeCargoPrefabs = runtimeCargoPrefabs;
        _ownedRuntime = true;
        m_material = runtimeMaterials != null && runtimeMaterials.Length > 0 ? runtimeMaterials[0] : null;
        ChangeColor(color);
    }

    public void ChangeColor(Color color)
    {
        if (ColorsEqual(m_color, color))
        {
            return;
        }

        m_color = color;
        if (_runtimeMaterials != null)
        {
            for (int i = 0; i < _runtimeMaterials.Length; i++)
            {
                _runtimeMaterials[i]?.SetColor(s_tintPropertyId, color);
            }

            return;
        }

        m_material?.SetColor(s_tintPropertyId, color);
    }

    #endregion

    #region 高亮状态

    public void AddHighlight(Int4 location)
    {
        if (!_highlightedLocations.Add(location))
        {
            return;
        }

        _highlightCountsByColumn.TryGetValue(location.Y, out int count);
        _highlightCountsByColumn[location.Y] = count + 1;
    }

    public void RemoveHighlight(Int4 location)
    {
        if (!_highlightedLocations.Remove(location)
            || !_highlightCountsByColumn.TryGetValue(location.Y, out int count))
        {
            return;
        }

        if (count <= 1)
        {
            _highlightCountsByColumn.Remove(location.Y);
        }
        else
        {
            _highlightCountsByColumn[location.Y] = count - 1;
        }
    }

    public bool HasHighlightsInColumn(int column) =>
        _highlightCountsByColumn.TryGetValue(column, out int count) && count > 0;

    public void CopyHighlightsInColumn(int column, List<Int4> target)
    {
        if (target == null || !HasHighlightsInColumn(column))
        {
            return;
        }

        foreach (Int4 location in _highlightedLocations)
        {
            if (location.Y == column)
            {
                target.Add(location);
            }
        }
    }

    public void ClearHighlights()
    {
        _highlightedLocations.Clear();
        _highlightCountsByColumn.Clear();
    }

    #endregion

    #region 仓库控制

    public void SetEnabled(bool enabled)
    {
        if (m_warehouseManager != null)
        {
            m_warehouseManager.enabled = enabled;
        }
    }

    public void SetColumnState(int column, bool visible)
    {
        // 静默层也要同步列显隐，否则之后重新启用时状态已过期。
        if (IsValid && Inited)
        {
            m_warehouseManager.SetColumnState(column, visible);
        }
    }

    public void SetColumnOffset(int column, Vector3 offset)
    {
        if (IsValid && Inited)
        {
            m_warehouseManager.SetColumnOffset(column, offset);
        }
    }

    #endregion

    #region 销毁释放

    public void DisposeRuntime()
    {
        ClearHighlights();

        if (!_ownedRuntime)
        {
            return;
        }

        if (m_warehouseManager != null)
        {
            UnityEngine.Object.Destroy(m_warehouseManager.gameObject);
            m_warehouseManager = null;
        }

        if (_runtimeCargoPrefabs != null)
        {
            for (int i = 0; i < _runtimeCargoPrefabs.Length; i++)
            {
                if (_runtimeCargoPrefabs[i] != null)
                {
                    UnityEngine.Object.Destroy(_runtimeCargoPrefabs[i]);
                }
            }

            _runtimeCargoPrefabs = null;
        }

        if (_runtimeMaterials != null)
        {
            for (int i = 0; i < _runtimeMaterials.Length; i++)
            {
                if (_runtimeMaterials[i] != null)
                {
                    UnityEngine.Object.Destroy(_runtimeMaterials[i]);
                }
            }

            _runtimeMaterials = null;
        }

        m_material = null;
        _ownedRuntime = false;
    }

    #endregion

    #region 工具方法

    public static bool ColorsEqual(Color a, Color b, float tolerance = 0.002f)
    {
        return Mathf.Abs(a.r - b.r) <= tolerance
               && Mathf.Abs(a.g - b.g) <= tolerance
               && Mathf.Abs(a.b - b.b) <= tolerance
               && Mathf.Abs(a.a - b.a) <= tolerance;
    }

    #endregion
}
