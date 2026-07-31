using System.Collections.Generic;
using NaughtyAttributes;
using NonsensicalKit.Core;
using UnityEngine;

/// <summary>
/// 正式多批盘点高亮：检索得到的多批 Int4[] + 对应颜色 → WarehouseInventoryReview。
/// </summary>
[RequireComponent(typeof(WarehouseInventoryReview))]
public class WarehouseInventoryMultiHighlight : NonsensicalMono
{
    [SerializeField] private string ID;
    [SerializeField] private WarehouseInventoryReview m_review;

    private Int4[][] _lastLocationBatches;
    private Color[] _lastBatchColors;

    #region Unity 生命周期

    private void Awake()
    {
        m_review ??= GetComponent<WarehouseInventoryReview>();
        IOCC.Set(ID, this);
    }

    #endregion

    #region 公开接口

    /// <summary>底层 Review 是否正在更新高亮（协程未结束）。</summary>
    public bool IsHighlightUpdating => m_review != null && m_review.IsUpdating;

    /// <summary>
    /// 应用检索结果：locationBatches[i] 与 batchColors[i] 一一对应成一批。
    /// </summary>
    public void ApplySearchHighlight(Int4[][] locationBatches, Color[] batchColors)
    {
        #region Check

        if (m_review == null)
        {
            Debug.LogWarning($"{nameof(WarehouseInventoryMultiHighlight)} 未配置 {nameof(WarehouseInventoryReview)}。");
            return;
        }

        if (locationBatches == null || locationBatches.Length == 0)
        {
            Debug.LogWarning($"{nameof(WarehouseInventoryMultiHighlight)} 检索批次为空。");
            return;
        }

        if (batchColors == null || batchColors.Length == 0)
        {
            Debug.LogWarning($"{nameof(WarehouseInventoryMultiHighlight)} 批次颜色为空。");
            return;
        }

        #endregion

        _lastLocationBatches = locationBatches;
        _lastBatchColors = batchColors;

        WarehouseInventoryHighlightBatch[] batches =
            WarehouseInventoryHighlightBatch.CreateAllHighlighted(locationBatches, batchColors);

        bool anyValid = false;
        for (int i = 0; i < batches.Length; i++)
        {
            if (batches[i] != null && batches[i].IsValid)
            {
                anyValid = true;
                break;
            }
        }

        if (!anyValid)
        {
            Debug.Log($"{nameof(WarehouseInventoryMultiHighlight)} 无有效高亮格位。");
            return;
        }

        m_review.ReplaceHighlight(batches);
    }

    /// <summary>
    /// 列表重载：多批格位 + 颜色数组。
    /// </summary>
    public void ApplySearchHighlight(IReadOnlyList<Int4[]> locationBatches, Color[] batchColors)
    {
        ApplySearchHighlight(ToArray(locationBatches), batchColors);
    }

    /// <summary>
    /// 列表重载：多批格位 + 颜色列表。
    /// </summary>
    public void ApplySearchHighlight(IReadOnlyList<Int4[]> locationBatches, IReadOnlyList<Color> batchColors)
    {
        ApplySearchHighlight(ToArray(locationBatches), ToArray(batchColors));
    }

    /// <summary>
    /// 清除异色状态
    /// </summary>
    [Button("恢复正常")]
    public void RestoreNormal()
    {
        m_review?.RestoreNormal();
    }

    /// <summary>
    /// 退出盘点：清本地检索缓存，并交给 Review 做会话级清理（保留盘点层）。
    /// </summary>
    [Button("退出盘点")]
    public void ExitReview()
    {
        _lastLocationBatches = null;
        _lastBatchColors = null;
        m_review?.ExitReview();
    }

    #endregion

    #region 编辑器按钮

    [Button("应用高亮")]
    private void ApplyHighlight()
    {
        if (_lastLocationBatches == null || _lastBatchColors == null)
        {
            Debug.LogWarning($"{nameof(WarehouseInventoryMultiHighlight)} 尚无检索结果，请先调用 {nameof(ApplySearchHighlight)}。");
            return;
        }

        ApplySearchHighlight(_lastLocationBatches, _lastBatchColors);
    }

    #endregion

    private Int4[][] ToArray(IReadOnlyList<Int4[]> locationBatches)
    {
        if (locationBatches == null || locationBatches.Count == 0)
        {
            return null;
        }

        var array = new Int4[locationBatches.Count][];
        for (int i = 0; i < locationBatches.Count; i++)
        {
            array[i] = locationBatches[i];
        }

        return array;
    }

    private Color[] ToArray(IReadOnlyList<Color> colors)
    {
        if (colors == null || colors.Count == 0)
        {
            return null;
        }

        var array = new Color[colors.Count];
        for (int i = 0; i < colors.Count; i++)
        {
            array[i] = colors[i];
        }

        return array;
    }
}
