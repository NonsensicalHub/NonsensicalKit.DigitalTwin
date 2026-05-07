using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    /// <summary>
    /// WebGL 运行时无稳定后台线程池；<see cref="UniTask.SwitchToThreadPool"/> / <see cref="UniTask.RunOnThreadPool"/>
    /// 可能导致 CPU 构建步骤未按预期执行，进而 <see cref="RenderObject.UpdateItems"/> 从未上传、画面空白。
    /// 桌面端仍可用线程池做大矩阵构建以降低主线程卡顿。
    /// </summary>
    internal static class WarehousePlatformCompat
    {
        public static bool CpuInstancingBuildMustUseMainThread =>
            Application.platform == RuntimePlatform.WebGLPlayer;
    }
}
