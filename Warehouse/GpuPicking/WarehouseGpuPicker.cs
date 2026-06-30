using Cysharp.Threading.Tasks;
using NonsensicalKit.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    /// <summary>
    /// 仓库 GPU Picking：离屏渲染 Pick ID 到 RT，再按屏幕像素读回颜色并解码货位。
    /// <para>
    /// <b>渲染管线：</b>当前仅支持 URP。Pick Pass 使用
    /// <c>Warehouse/GpuPicking/WarehousePick.shader</c>（Tags: UniversalPipeline，HLSL + URP Core）。
    /// 内置管线 / HDRP 需替换对应 Pick Shader，并重新标定下方坐标翻转常量。
    /// </para>
    /// <para>
    /// <b>坐标系（跨项目最易混淆）：</b>
    /// <list type="bullet">
    /// <item><c>Input.mousePosition</c>、<c>Camera.pixelRect</c>、<c>ReadPixels</c>：左下角为原点，Y 向上。</item>
    /// <item>OnGUI / <c>GUI.DrawTexture</c>：左上角为原点，Y 向下。</item>
    /// <item>Pick RT 由 <see cref="RenderPickingPass"/> 写入，投影使用
    /// <c>GL.GetGPUProjectionMatrix(projectionMatrix, renderIntoTexture: true)</c>，
    /// 与 Unity 写入 RenderTexture 的约定一致；读回像素时一般<b>不要再翻 Y</b>。</item>
    /// </list>
    /// 因此「点击采样」与「调试预览显示」使用<b>两套</b>翻转开关，切勿共用同一常量。
    /// 若换项目/图形 API 后点击上下或左右颠倒，只改 <see cref="FlipPickSampleX"/> /
    /// <see cref="FlipPickSampleY"/>；若仅右下角 RT 预览方向不对，只改
    /// <see cref="FlipPickPreviewX"/> / <see cref="FlipPickPreviewY"/>（并同步 Demo 里十字准星绘制）。
    /// </para>
    /// </summary>
    internal sealed class WarehouseGpuPicker
    {
        private const string PickShaderName = "NonsensicalKit/DigitalTwin/WarehousePick";
        private const string PickMaterialResourcesPath = "WarehouseGpuPick";

        // --- 点击读回 RT 时的像素映射（影响 Pick 命中，与预览无关）---
        // URP + GetGPUProjectionMatrix(..., true) 下，mouse 像素与 RT 像素通常 1:1，保持 false。
        private const bool FlipPickSampleX = false;
        private const bool FlipPickSampleY = false;

        // --- 调试预览在 OnGUI 上绘制时的 UV 翻转（仅影响 LastPickPreview 显示）---
        // GUI 坐标系 Y 向下，RT 内容 Y 向上，故预览常需 FlipPreviewY = true。
        private const bool FlipPickPreviewX = false;
        private const bool FlipPickPreviewY = true;
        private static readonly int DitherVisibilityPropertyId = Shader.PropertyToID("_DitherVisibility");
        private static readonly int ZTestPropertyId = Shader.PropertyToID("_ZTest");

        private Material _pickMaterial;
        private RenderTexture _target;
        private Texture2D _syncReadbackTexture;
        private Texture2D _debugPreviewTexture;
        private bool _debugEnabled;

        /// <summary>
        /// 开启后会：每帧离屏 Pick 预览（<see cref="SetContinuousPreview"/>）、
        /// 点击时整屏 ReadPixels 到 <see cref="LastPickPreview"/>、记录 <see cref="LastDebugInfo"/>。
        /// 关闭时不做上述操作，并释放预览纹理。
        /// </summary>
        public bool DebugEnabled
        {
            get => _debugEnabled;
            set
            {
                if (_debugEnabled == value)
                {
                    return;
                }

                _debugEnabled = value;
                if (!_debugEnabled)
                {
                    ReleaseDebugPreview();
                    LastDebugInfo = WarehouseGpuPickDebugInfo.Empty("gpu picking debug disabled");
                }
            }
        }

        public Texture2D LastPickPreview => _debugEnabled ? _debugPreviewTexture : null;
        public bool FlipPreviewX => FlipPickPreviewX;
        public bool FlipPreviewY => FlipPickPreviewY;
        public WarehouseGpuPickDebugInfo LastDebugInfo { get; private set; } =
            WarehouseGpuPickDebugInfo.Empty(string.Empty);

        public void SetContinuousPreview(Camera camera, CargoConfig[] cargoConfigs, float globalCargoVisibility)
        {
            if (!DebugEnabled || camera == null || cargoConfigs == null || cargoConfigs.Length == 0)
            {
                return;
            }

            if (!EnsureResources(camera.pixelWidth, camera.pixelHeight))
            {
                return;
            }

            RenderPickingPass(camera, cargoConfigs, globalCargoVisibility);
            CaptureDebugPreview();
        }

        public UniTask<WarehousePickResult> PickAsync(
            Vector2 screenPosition,
            Camera camera,
            CargoConfig[] cargoConfigs,
            Int4 dimensions,
            float globalCargoVisibility,
            WarehouseBinDataStore binDataStore)
        {
            if (camera == null)
            {
                return UniTask.FromResult(LogMiss(screenPosition, failReason: "camera is null"));
            }

            if (cargoConfigs == null || cargoConfigs.Length == 0)
            {
                return UniTask.FromResult(LogMiss(screenPosition, failReason: "cargo configs not ready"));
            }

            if (!TryResolvePixel(camera, screenPosition, out int pixelX, out int pixelY))
            {
                return UniTask.FromResult(LogMiss(
                    screenPosition,
                    failReason: $"pixel out of range, camera={camera.name}, pixelRect={camera.pixelRect}, size=({camera.pixelWidth},{camera.pixelHeight})"));
            }

            if (!EnsureResources(camera.pixelWidth, camera.pixelHeight))
            {
                return UniTask.FromResult(LogMiss(
                    screenPosition,
                    pixelX,
                    pixelY,
                    failReason: "pick resources not ready (shader/material/rt)",
                    hasSample: true));
            }

            RenderPickingPass(camera, cargoConfigs, globalCargoVisibility);
            if (DebugEnabled)
            {
                CaptureDebugPreview();
            }

            Color32 color = ReadPixelSync(pixelX, pixelY);
            WarehousePickResult result = ResolvePickResult(
                screenPosition,
                pixelX,
                pixelY,
                color,
                dimensions,
                binDataStore);
            return UniTask.FromResult(result);
        }

        public void Release()
        {
            if (_target != null)
            {
                _target.Release();
                Object.Destroy(_target);
                _target = null;
            }

            if (_syncReadbackTexture != null)
            {
                Object.Destroy(_syncReadbackTexture);
                _syncReadbackTexture = null;
            }

            if (_debugPreviewTexture != null)
            {
                ReleaseDebugPreview();
            }

            if (_pickMaterial != null)
            {
                Object.Destroy(_pickMaterial);
                _pickMaterial = null;
            }
        }

        private WarehousePickResult ResolvePickResult(
            Vector2 screenPosition,
            int pixelX,
            int pixelY,
            Color32 color,
            Int4 dimensions,
            WarehouseBinDataStore binDataStore)
        {
            uint pickId = WarehousePickId.DecodeColor(color);
            if (!WarehousePickId.TryDecode(pickId, dimensions, out Int4 decoded))
            {
                return LogMiss(
                    screenPosition,
                    pixelX,
                    pixelY,
                    color,
                    pickId,
                    failReason: pickId == 0
                        ? "pickId=0, pick pass drew nothing at clicked pixel"
                        : $"pickId decode failed, dimensions={dimensions}",
                    hasSample: true);
            }

            bool binFound = false;
            bool showCargo = false;
            RuntimeBinData binData = null;
            if (binDataStore != null)
            {
                binFound = binDataStore.TryGet(decoded, out binData);
                showCargo = binFound && binData != null && binData.ShowCargo;
            }

            if (!binFound)
            {
                return LogMiss(
                    screenPosition,
                    pixelX,
                    pixelY,
                    color,
                    pickId,
                    decodeOk: true,
                    decoded: decoded,
                    failReason: $"decoded location not found in store: {decoded}",
                    hasSample: true);
            }

            if (!showCargo)
            {
                return LogMiss(screenPosition, pixelX, pixelY,
                    color,
                    pickId,
                    decodeOk: true,
                    decoded: decoded,
                    binFound: true,
                    failReason: $"location exists but ShowCargo=false: {decoded}",
                    hasSample: true);
            }

            if (DebugEnabled)
            {
                LastDebugInfo = new WarehouseGpuPickDebugInfo(
                    true,
                    screenPosition,
                    pixelX,
                    pixelY,
                    color,
                    pickId,
                    true,
                    decoded,
                    true,
                    true,
                    "hit");
            }

            return new WarehousePickResult(true, decoded);
        }

        /// <summary>
        /// 将屏幕坐标（左下原点）转为相机 pixelRect 内像素索引，供 <see cref="ReadPixelSync"/> 使用。
        /// </summary>
        private static bool TryResolvePixel(Camera camera, Vector2 screenPosition, out int pixelX, out int pixelY)
        {
            Rect pixelRect = camera.pixelRect;
            Vector2 local = screenPosition - pixelRect.position;
            pixelX = Mathf.FloorToInt(local.x);
            pixelY = Mathf.FloorToInt(local.y);
            return pixelX >= 0 && pixelX < camera.pixelWidth && pixelY >= 0 && pixelY < camera.pixelHeight;
        }

        private bool EnsureResources(int width, int height)
        {
            if (_pickMaterial == null && !TryCreatePickMaterial())
            {
                return false;
            }

            if (_target != null && _target.width == width && _target.height == height)
            {
                return true;
            }

            if (_target != null)
            {
                _target.Release();
                Object.Destroy(_target);
            }

            _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "Warehouse GPU Pick RT",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                useMipMap = false,
                autoGenerateMips = false
            };
            _target.Create();
            return true;
        }

        /// <summary>
        /// 优先从 Resources 材质加载 Shader，避免 Player 构建时 <c>Shader.Find</c> / Instancing 变体被裁切。
        /// </summary>
        private bool TryCreatePickMaterial()
        {
            Material template = Resources.Load<Material>(PickMaterialResourcesPath);
            Shader shader = template != null ? template.shader : null;
            if (shader == null)
            {
                shader = Shader.Find(PickShaderName);
            }

            if (shader == null)
            {
                Debug.LogError(
                    $"[Warehouse] 找不到 GPU Picking Shader: {PickShaderName}。" +
                    $"请确认包内 Warehouse/GpuPicking/Resources/{PickMaterialResourcesPath}.mat 存在，" +
                    "或将 Warehouse/GpuPicking/WarehousePick.shader 加入 Graphics Settings → Always Included Shaders。");
                return false;
            }

            if (!shader.isSupported)
            {
                Debug.LogError($"[Warehouse] GPU Picking Shader 当前平台不支持: {PickShaderName}");
                return false;
            }

            _pickMaterial = template != null ? new Material(template) : new Material(shader);
            _pickMaterial.name = "Warehouse GPU Pick Material";
            _pickMaterial.hideFlags = HideFlags.HideAndDontSave;
            _pickMaterial.enableInstancing = true;
            _pickMaterial.SetFloat(DitherVisibilityPropertyId, 1f);
            return true;
        }

        private void RenderPickingPass(Camera sourceCamera, CargoConfig[] cargoConfigs, float globalCargoVisibility)
        {
            _pickMaterial.SetFloat(DitherVisibilityPropertyId, Mathf.Clamp01(globalCargoVisibility));

            if (sourceCamera == null || cargoConfigs == null || _target == null)
            {
                return;
            }

            bool reversedZ = SystemInfo.usesReversedZBuffer;
            _pickMaterial.SetInt(
                ZTestPropertyId,
                (int)(reversedZ ? CompareFunction.GreaterEqual : CompareFunction.LessEqual));

            Matrix4x4 view = sourceCamera.worldToCameraMatrix;
            // renderIntoTexture=true：与 SetRenderTarget(RT) 配套，保证 RT 内像素与屏幕 mouse 像素对齐。
            // 若此处改为 false 或换管线，需同步检查 FlipPickSampleX/Y。
            Matrix4x4 proj = GL.GetGPUProjectionMatrix(sourceCamera.projectionMatrix, true);

            CommandBuffer cmd = new CommandBuffer { name = "WarehouseGpuPick" };
            cmd.SetRenderTarget(_target);
            cmd.ClearRenderTarget(true, true, Color.black, reversedZ ? 0f : 1f);
            cmd.SetViewProjectionMatrices(view, proj);

            for (int i = 0; i < cargoConfigs.Length; i++)
            {
                CargoConfig config = cargoConfigs[i];
                if (config == null)
                {
                    continue;
                }

                config.RenderPickLoads(cmd, _pickMaterial);
            }

            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Dispose();
        }

        private Color32 ReadPixelSync(int pixelX, int pixelY)
        {
            if (_syncReadbackTexture == null)
            {
                _syncReadbackTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true)
                {
                    name = "Warehouse GPU Pick Readback",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point
                };
            }

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = _target;
            // pixelX/pixelY 来自 Input.mousePosition（左下原点）；ReadPixels 同样左下原点。
            // 仅当「点击位置与读回颜色上下/左右对调」时，才打开 FlipPickSampleX/Y。
            int sampleX = FlipPickSampleX ? _target.width - 1 - pixelX : pixelX;
            int sampleY = FlipPickSampleY ? _target.height - 1 - pixelY : pixelY;
            _syncReadbackTexture.ReadPixels(new Rect(sampleX, sampleY, 1, 1), 0, 0, false);
            _syncReadbackTexture.Apply(false, false);
            RenderTexture.active = previousActive;
            return _syncReadbackTexture.GetPixels32()[0];
        }

        private WarehousePickResult LogMiss(
            Vector2 screenPosition,
            int pixelX = 0,
            int pixelY = 0,
            Color32 color = default,
            uint pickId = 0,
            bool decodeOk = false,
            Int4 decoded = default,
            bool binFound = false,
            bool showCargo = false,
            string failReason = null,
            bool hasSample = false)
        {
            if (DebugEnabled)
            {
                LastDebugInfo = new WarehouseGpuPickDebugInfo(
                    hasSample,
                    screenPosition,
                    pixelX,
                    pixelY,
                    color,
                    pickId,
                    decodeOk,
                    decoded,
                    binFound,
                    showCargo,
                    failReason ?? string.Empty);
            }

            return WarehousePickResult.Miss;
        }

        private void ReleaseDebugPreview()
        {
            if (_debugPreviewTexture == null)
            {
                return;
            }

            Object.Destroy(_debugPreviewTexture);
            _debugPreviewTexture = null;
        }

        /// <summary>
        /// 将 Pick RT 整张贴图复制到 CPU 纹理，供调试预览；不做 Y 翻转（翻转由
        /// <see cref="FlipPreviewY"/> + Demo 侧 DrawTextureWithTexCoords 负责）。
        /// 仅在 <see cref="DebugEnabled"/> 时调用。
        /// </summary>
        private void CaptureDebugPreview()
        {
            if (!DebugEnabled || _target == null)
            {
                return;
            }

            if (_debugPreviewTexture == null ||
                _debugPreviewTexture.width != _target.width ||
                _debugPreviewTexture.height != _target.height)
            {
                if (_debugPreviewTexture != null)
                {
                    Object.Destroy(_debugPreviewTexture);
                }

                _debugPreviewTexture = new Texture2D(_target.width, _target.height, TextureFormat.RGBA32, false, true)
                {
                    name = "Warehouse GPU Pick Preview",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point
                };
            }

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = _target;
            _debugPreviewTexture.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0, false);
            _debugPreviewTexture.Apply(false, false);
            RenderTexture.active = previousActive;
        }
    }

    internal readonly struct WarehousePickResult
    {
        public readonly bool Hit;
        public readonly Int4 Location;

        public WarehousePickResult(bool hit, Int4 location)
        {
            Hit = hit;
            Location = location;
        }

        public static WarehousePickResult Miss => new WarehousePickResult(false, default);
    }
}
