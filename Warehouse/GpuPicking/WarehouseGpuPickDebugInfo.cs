using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    public readonly struct WarehouseGpuPickDebugInfo
    {
        public readonly bool HasSample;
        public readonly Vector2 ScreenPosition;
        public readonly int PixelX;
        public readonly int PixelY;
        public readonly Color32 Color;
        public readonly uint PickId;
        public readonly bool DecodeOk;
        public readonly Int4 DecodedLocation;
        public readonly bool BinFound;
        public readonly bool ShowCargo;
        public readonly string FailReason;

        public WarehouseGpuPickDebugInfo(
            bool hasSample,
            Vector2 screenPosition,
            int pixelX,
            int pixelY,
            Color32 color,
            uint pickId,
            bool decodeOk,
            Int4 decodedLocation,
            bool binFound,
            bool showCargo,
            string failReason)
        {
            HasSample = hasSample;
            ScreenPosition = screenPosition;
            PixelX = pixelX;
            PixelY = pixelY;
            Color = color;
            PickId = pickId;
            DecodeOk = decodeOk;
            DecodedLocation = decodedLocation;
            BinFound = binFound;
            ShowCargo = showCargo;
            FailReason = failReason;
        }

        public static WarehouseGpuPickDebugInfo Empty(string failReason)
        {
            return new WarehouseGpuPickDebugInfo(
                false,
                default,
                0,
                0,
                default,
                0,
                false,
                default,
                false,
                false,
                failReason);
        }

        public override string ToString()
        {
            if (!HasSample)
            {
                return $"[Warehouse][PickDebug] {FailReason}";
            }

            return
                $"[Warehouse][PickDebug] screen={ScreenPosition}, pixel=({PixelX},{PixelY}), color={Color}, pickId={PickId} (0x{PickId:X8}), decodeOk={DecodeOk}, location={DecodedLocation}, binFound={BinFound}, showCargo={ShowCargo}, fail={FailReason}";
        }
    }
}
