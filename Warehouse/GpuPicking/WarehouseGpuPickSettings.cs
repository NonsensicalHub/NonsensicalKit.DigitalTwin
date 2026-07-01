using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    /// <summary>
    /// GPU Picking 采样坐标策略。版本与 URP 是否在类型加载时解析一次，Pick 热路径不再重复判断。
    /// <para>
    /// <b>文档依据（哪代 URP 容易出 Y 颠倒）：</b>
    /// <list type="bullet">
    /// <item><b>URP 17 / Unity 6（6000.x）</b>：Render Graph 成为主路径；
    /// SRP Core 引入 <c>TextureUVOrigin</c>，区分中间 RT（BottomLeft）与 Backbuffer（TopLeft）。
    /// 自定义 CommandBuffer RT 经 <c>ReadPixels</c> 读回时，与 <c>Input.mousePosition</c> 的 Y 在 URP 17 上常需翻转。
    /// 见 <a href="https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.6/api/UnityEngine.Rendering.RenderGraphModule.TextureUVOrigin.html">TextureUVOrigin</a>、
    /// <a href="https://docs.unity3d.com/6000.6/Documentation/Manual/urp/upgrade-guide-unity-6.html">Upgrade to URP 17</a>。</item>
    /// <item><b>URP 12–16 / Unity 2021.2–2023.x</b>：在
    /// <c>GL.GetGPUProjectionMatrix(..., renderIntoTexture: true)</c> 配套自建 RT 时，
    /// mouse 与 RT 像素通常 1:1，<see cref="DefaultFlipPickSampleY"/> 即可。</item>
    /// <item><b>URP 12（Unity 2021.2）</b>：Universal Renderer 迁移 RTHandle，changelog 有多处 RT/depth Y-flip 修复
    ///（如 depth texture flipped case 1225362）。部分平台或旧补丁仍可能颠倒，请用
    /// <see cref="FlipPickSampleYOverride"/> 强制。</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class WarehouseGpuPickSettings
    {
        /// <summary>
        /// 点击读回 RT 时是否翻转 Y 的手动默认值（原 <c>FlipPickSampleY</c> 常量，URP 12–16 路径为 false）。
        /// 自动判定为 Unity 6 时会由 <see cref="AutoFlipPickSampleY"/> 覆盖，除非设置了
        /// <see cref="FlipPickSampleYOverride"/>。
        /// </summary>
        public const bool DefaultFlipPickSampleY = false;

        /// <summary>
        /// 强制是否翻转点击采样 Y。<c>null</c> 表示使用 <see cref="AutoFlipPickSampleY"/>。
        /// 应在首次 GPU Pick 之前设置。
        /// </summary>
        public static bool? FlipPickSampleYOverride { get; set; }

        /// <summary>解析 <see cref="Application.unityVersion"/> 得到的主版本号（如 6000、2022）。</summary>
        public static readonly int UnityMajorVersion = ParseUnityMajorVersion(Application.unityVersion);

        /// <summary>当前是否将 Universal RP Asset 设为活动渲染管线。</summary>
        public static readonly bool IsUniversalRenderPipelineActive = DetectUniversalRenderPipelineActive();

        /// <summary>类型加载时根据 Unity 版本解析，整个进程内不变。</summary>
        public static readonly bool AutoFlipPickSampleY = ResolveAutoFlipPickSampleY(
            UnityMajorVersion,
            IsUniversalRenderPipelineActive);

        /// <summary>实际用于 <c>ReadPixels</c> 的 Y 翻转：Override 优先，否则 Auto。</summary>
        public static bool FlipPickSampleY => FlipPickSampleYOverride ?? AutoFlipPickSampleY;

        static WarehouseGpuPickSettings()
        {
            if (!IsUniversalRenderPipelineActive)
            {
                Debug.LogWarning(
                    "[Warehouse] GPU Picking 需要 Universal Render Pipeline。" +
                    "GraphicsSettings / QualitySettings 未配置 URP Asset。");
            }

#if UNITY_EDITOR
            if (FlipPickSampleYOverride == null && AutoFlipPickSampleY)
            {
                Debug.Log(
                    $"[Warehouse] GPU Picking：Unity {UnityMajorVersion}（URP 17+），" +
                    $"AutoFlipPickSampleY=true。可设 {nameof(FlipPickSampleYOverride)} 覆盖。");
            }
#endif
        }

        private static bool ResolveAutoFlipPickSampleY(int unityMajor, bool urpActive)
        {
            if (!urpActive)
            {
                return DefaultFlipPickSampleY;
            }

            // Unity 6 绑定 URP 17；此处用 unityVersion 判据，Editor/Player 均可靠。
            if (unityMajor >= 6000)
            {
                return true;
            }

            return DefaultFlipPickSampleY;
        }

        private static int ParseUnityMajorVersion(string unityVersion)
        {
            if (string.IsNullOrEmpty(unityVersion))
            {
                return 0;
            }

            int end = 0;
            while (end < unityVersion.Length && char.IsDigit(unityVersion[end]))
            {
                end++;
            }

            return end > 0 && int.TryParse(unityVersion.Substring(0, end), out int major) ? major : 0;
        }

        private static bool DetectUniversalRenderPipelineActive()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.defaultRenderPipeline;
            if (pipeline == null)
            {
                pipeline = QualitySettings.renderPipeline;
            }

            if (pipeline == null)
            {
                return false;
            }

            string typeName = pipeline.GetType().FullName;
            return typeName != null &&
                   typeName.IndexOf("Universal", StringComparison.Ordinal) >= 0;
        }
    }
}
