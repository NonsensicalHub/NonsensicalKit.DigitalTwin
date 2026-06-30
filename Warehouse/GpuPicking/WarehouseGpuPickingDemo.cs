using System;
using Cysharp.Threading.Tasks;
using NonsensicalKit.Core;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine;

public sealed class WarehouseGpuPickingDemo : MonoBehaviour
{
    [SerializeField] private WarehouseManager m_warehouseManager;
    [SerializeField] private Camera m_camera;
    [SerializeField] private int m_mouseButton;
    [SerializeField] private bool m_locateHighlight = true;
    [SerializeField] private bool m_logMiss = true;
    [SerializeField] private bool m_enableGpuPicking = true;
    [SerializeField] private bool m_debugPicking = true;
    [SerializeField] private bool m_showPickPreview = true;
    [SerializeField, Range(0.15f, 0.6f)] private float m_previewScreenWidthRatio = 0.35f;

    private bool _picking;

    private void Reset()
    {
        m_warehouseManager = FindObjectOfType<WarehouseManager>();
        m_camera = Camera.main;
    }

    private void OnValidate()
    {
        if (m_warehouseManager != null)
        {
            m_warehouseManager.EnableGpuPicking = m_enableGpuPicking;
            m_warehouseManager.DebugGpuPicking = m_debugPicking;
        }
    }

    private void Start()
    {
        if (m_warehouseManager == null) return;
        m_warehouseManager.EnableGpuPicking = m_enableGpuPicking;
        m_warehouseManager.DebugGpuPicking = m_debugPicking;
    }

    private void Update()
    {
        if (_picking || m_warehouseManager == null || !m_warehouseManager.Inited || !Input.GetMouseButtonDown(m_mouseButton))
        {
            return;
        }

        PickAsync(Input.mousePosition).Forget();
    }

    private void OnGUI()
    {
        if (!m_showPickPreview || m_warehouseManager == null)
        {
            return;
        }

        Texture2D preview = m_warehouseManager.LastGpuPickPreview;
        if (preview == null)
        {
            return;
        }

        float width = Screen.width * m_previewScreenWidthRatio;
        float height = width * preview.height / Mathf.Max(1, preview.width);
        var rect = new Rect(Screen.width - width - 10f, Screen.height - height - 10f, width, height);

        GUI.Box(new Rect(rect.x - 4f, rect.y - 22f, rect.width + 8f, rect.height + 26f), "GPU Pick RT");
        GUI.DrawTextureWithTexCoords(rect, preview, GetPreviewTexCoords());

        WarehouseGpuPickDebugInfo info = m_warehouseManager.LastGpuPickDebugInfo;
        if (info.HasSample)
        {
            // 十字准星：GUI 为左上原点，故 Y 用 (1 - normalized) 换算，与 FlipGpuPickPreviewY 无关。
            float nx = rect.x + rect.width * (info.PixelX / (float)Mathf.Max(1, preview.width));
            float ny = rect.y + rect.height * (1f - (info.PixelY + 1f) / Mathf.Max(1, preview.height));
            const float size = 6f;
            GUI.color = Color.yellow;
            GUI.DrawTexture(new Rect(nx - size, ny - 1f, size * 2f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(nx - 1f, ny - size, 2f, size * 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }

    private async UniTaskVoid PickAsync(Vector2 screenPosition)
    {
        _picking = true;
        try
        {
            m_warehouseManager.DebugGpuPicking = m_debugPicking;
            Camera pickCamera = m_camera != null ? m_camera : Camera.main;

            bool hit = await m_warehouseManager.TryPickCargoAsync(
                screenPosition,
                pickCamera,
                location => Debug.Log($"[WarehouseGpuPickingDemo] Picked: {Format(location)}"),
                m_locateHighlight);

            if (!hit && m_logMiss)
            {
                Debug.Log($"[WarehouseGpuPickingDemo] 未命中货物。{m_warehouseManager.LastGpuPickDebugInfo}");
            }
        }
        finally
        {
            _picking = false;
        }
    }

    private static string Format(Int4 location)
    {
        return $"layer={location.X}, column={location.Y}, row={location.Z}, depth={location.W}";
    }

    /// <summary>
    /// 将 <see cref="WarehouseManager.FlipGpuPickPreviewX"/> / Y 转为 GUI UV，
    /// 使 RT 预览方向与主视图一致；与 Pick 采样翻转无关。
    /// </summary>
    private Rect GetPreviewTexCoords()
    {
        float x = m_warehouseManager.FlipGpuPickPreviewX ? 1f : 0f;
        float y = m_warehouseManager.FlipGpuPickPreviewY ? 1f : 0f;
        float width = m_warehouseManager.FlipGpuPickPreviewX ? -1f : 1f;
        float height = m_warehouseManager.FlipGpuPickPreviewY ? -1f : 1f;
        return new Rect(x, y, width, height);
    }
}
