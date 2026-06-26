using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using Cysharp.Threading.Tasks;
using NonsensicalKit.Core;
using NonsensicalKit.Tools;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine.Networking;
#endif

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    /// <summary>
    /// 单个货位的数据结构。
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)] // 紧凑排列，无对齐填充
    public struct BinData
    {
        public int Level;
        public int Column;
        public int Row;
        public int Depth;
        public float PosX;
        public float PosY;
        public float PosZ;
    }

    /// <summary>
    /// 仓库数据对象，包含货位数组与维度信息。
    /// </summary>
    public sealed class WarehouseData
    {
        public BinData[] Bins { get; }
        public Int4 Dimensions { get; }

        public WarehouseData(BinData[] bins, Int4 dimensions)
        {
            Bins = bins ?? Array.Empty<BinData>();
            Dimensions = dimensions;
        }

        public static WarehouseData Create(BinData[] bins)
        {
            var dimensions = BinDataIO.InferDimensions(bins);
            return new WarehouseData(bins, dimensions);
        }
    }

    /// <summary>
    /// 仓库二进制数据读写工具，支持同步、异步与 StreamingAssets 加载。
    /// </summary>
    public static class BinDataIO
    {
        private static readonly int StructSize = Marshal.SizeOf<BinData>();
        private const int IntByteSize = sizeof(int);
        private const int FileMagic = 0x314E4942; // "BIN1"
        private const int CurrentVersion = 1;
        private const int V1FixedHeaderSize = IntByteSize * 8;
        private const int HeaderMagicOffset = IntByteSize * 0;
        private const int HeaderVersionOffset = IntByteSize * 1;
        private const int HeaderSizeOffset = IntByteSize * 2;
        private const int HeaderCountOffset = IntByteSize * 3;
        private const int HeaderDimXOffset = IntByteSize * 4;
        private const int HeaderDimYOffset = IntByteSize * 5;
        private const int HeaderDimZOffset = IntByteSize * 6;
        private const int HeaderDimWOffset = IntByteSize * 7;

        private readonly struct V1Header
        {
            public readonly int Magic;
            public readonly int Version;
            public readonly int HeaderSize;
            public readonly int Count;
            public readonly Int4 Dimensions;

            public V1Header(int magic, int version, int headerSize, int count, Int4 dimensions)
            {
                Magic = magic;
                Version = version;
                HeaderSize = headerSize;
                Count = count;
                Dimensions = dimensions;
            }
        }

        #region 同步方法

        public static void SaveSync(BinData[] bins, string filePath)
        {
            var data = WarehouseData.Create(bins);
            ValidateFilePath(filePath);
            ValidateWarehouseData(data);
            var bytes = SerializeToBytes(data);
            FileTool.EnsureFileDir(filePath);
            File.WriteAllBytes(filePath, bytes);
        }

        public static WarehouseData LoadSync(string filePath)
        {
            ValidateFilePath(filePath);
            return DeserializeFromBytes(File.ReadAllBytes(filePath));
        }

        #endregion

        #region 异步方法

        public static async UniTask SaveAsync(BinData[] bins, string filePath)
        {
            var data = WarehouseData.Create(bins);

            ValidateFilePath(filePath);
            ValidateWarehouseData(data);
            var bytes = await SerializeToBytesAsync(data);
            EnsureParentDirectoryExists(filePath);
            await File.WriteAllBytesAsync(filePath, bytes);
        }

        public static async UniTask<WarehouseData> LoadAsync(string filePath)
        {
            ValidateFilePath(filePath);
            byte[] raw = await File.ReadAllBytesAsync(filePath);
            return await DeserializeFromBytesAsync(raw);
        }

        /// <summary>
        /// WebGL 兼容：从 StreamingAssets 加载（使用 UnityWebRequest）。
        /// </summary>
        public static async UniTask<WarehouseData> LoadFromStreamingAssetsAsync(string relativePath)
        {
            string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);
#if UNITY_WEBGL && !UNITY_EDITOR
            using (var request = UnityWebRequest.Get(fullPath))
            {
                var op = request.SendWebRequest();
                while (!op.isDone)
                    await UniTask.Yield();
                if (request.result != UnityWebRequest.Result.Success)
                    throw new IOException($"Load failed: {request.error}");
                return await DeserializeFromBytesAsync(request.downloadHandler.data);
            }
#else
            return await LoadAsync(fullPath);
#endif
        }

        #endregion

        #region 转换方法

        private static byte[] SerializeToBytes(WarehouseData data)
        {
            BinData[] bins = data.Bins;
            Int4 dimensions = data.Dimensions;
            int count = bins.Length;
            int dataByteSize = checked(count * StructSize);
            var header = new V1Header(FileMagic, CurrentVersion, V1FixedHeaderSize, count, dimensions);
            byte[] buffer = new byte[checked(header.HeaderSize + dataByteSize)];
            WriteV1Header(buffer.AsSpan(0, header.HeaderSize), in header);

            if (count > 0)
                CopyBinsToBuffer(bins, buffer, header.HeaderSize, dataByteSize);

            return buffer;
        }

        private static WarehouseData DeserializeFromBytes(byte[] buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (buffer.Length < IntByteSize)
                throw new InvalidDataException("Buffer length is too small to contain metadata.");

            int magic = ReadInt32(buffer, HeaderMagicOffset);
            if (magic != FileMagic)
            {
                // 兼容旧格式：首个 int 为条目数量，不包含 v1 头部信息。
                var binsFromLegacy = DeserializeLegacyBinData(buffer);
                var inferred = InferDimensions(binsFromLegacy);
                return new WarehouseData(binsFromLegacy, inferred);
            }

            var header = ReadV1Header(buffer);
            int dataByteSize = checked(header.Count * StructSize);
            int expectedLength = checked(header.HeaderSize + dataByteSize);
            if (buffer.Length < expectedLength)
                throw new InvalidDataException(
                    $"Buffer length mismatch. Expected at least {expectedLength}, got {buffer.Length}.");

            BinData[] bins = new BinData[header.Count];
            if (header.Count > 0)
                CopyBufferToBins(buffer, header.HeaderSize, bins, dataByteSize);

            return new WarehouseData(bins, header.Dimensions);
        }

        private static BinData[] DeserializeLegacyBinData(byte[] buffer)
        {
            int count = ReadInt32(buffer, 0);
            if (count < 0)
                throw new InvalidDataException($"Invalid item count: {count}.");

            int dataByteSize = checked(count * StructSize);
            int expectedLength = checked(IntByteSize + dataByteSize);
            if (buffer.Length < expectedLength)
                throw new InvalidDataException(
                    $"Legacy buffer length mismatch. Expected at least {expectedLength}, got {buffer.Length}.");

            BinData[] bins = new BinData[count];
            if (count > 0)
                CopyBufferToBins(buffer, IntByteSize, bins, dataByteSize);

            return bins;
        }

        private static UniTask<byte[]> SerializeToBytesAsync(WarehouseData data)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL 通常不支持线程池，直接执行避免额外调度开销。
            return UniTask.FromResult(SerializeToBytes(data));
#else
            return UniTask.RunOnThreadPool(() => SerializeToBytes(data));
#endif
        }

        private static UniTask<WarehouseData> DeserializeFromBytesAsync(byte[] buffer)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL 通常不支持线程池，直接执行避免额外调度开销。
            return UniTask.FromResult(DeserializeFromBytes(buffer));
#else
            return UniTask.RunOnThreadPool(() => DeserializeFromBytes(buffer));
#endif
        }

        public static Int4 InferDimensions(BinData[] bins)
        {
            if (bins == null || bins.Length == 0)
                return new Int4(0, 0, 0, 0);

            int maxRow = -1;
            int maxColumn = -1;
            int maxLevel = -1;
            int maxDepth = -1;
            for (int i = 0; i < bins.Length; i++)
            {
                if (bins[i].Row > maxRow) maxRow = bins[i].Row;
                if (bins[i].Column > maxColumn) maxColumn = bins[i].Column;
                if (bins[i].Level > maxLevel) maxLevel = bins[i].Level;
                if (bins[i].Depth > maxDepth) maxDepth = bins[i].Depth;
            }

            return new Int4(
                checked(maxLevel + 1),
                checked(maxColumn + 1),
                checked(maxRow + 1),
                checked(maxDepth + 1));
        }

        private static void ValidateDimensions(Int4 dimensions)
        {
            if (dimensions.X < 0 || dimensions.Y < 0 || dimensions.Z < 0 || dimensions.W < 0)
                throw new InvalidDataException("Warehouse dimensions cannot be negative.");
        }

        private static void ValidateWarehouseData(WarehouseData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.Bins == null)
                throw new ArgumentException("WarehouseData.Bins cannot be null.", nameof(data));
            ValidateDimensions(data.Dimensions);
        }

        private static int ReadInt32(byte[] buffer, int offset)
        {
            return BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset, IntByteSize));
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset, IntByteSize), value);
        }

        private static V1Header ReadV1Header(byte[] buffer)
        {
            if (buffer.Length < V1FixedHeaderSize)
                throw new InvalidDataException("Buffer length is too small for v1 header.");

            int version = ReadInt32(buffer, HeaderVersionOffset);
            if (version <= 0)
                throw new InvalidDataException($"Invalid header version: {version}.");

            int headerSize = ReadInt32(buffer, HeaderSizeOffset);
            if (headerSize < V1FixedHeaderSize || headerSize > buffer.Length)
                throw new InvalidDataException($"Invalid header size: {headerSize}.");

            int count = ReadInt32(buffer, HeaderCountOffset);
            if (count < 0)
                throw new InvalidDataException($"Invalid item count: {count}.");

            var dimensions = new Int4(
                ReadInt32(buffer, HeaderDimXOffset),
                ReadInt32(buffer, HeaderDimYOffset),
                ReadInt32(buffer, HeaderDimZOffset),
                ReadInt32(buffer, HeaderDimWOffset));
            ValidateDimensions(dimensions);

            return new V1Header(FileMagic, version, headerSize, count, dimensions);
        }

        private static void WriteV1Header(Span<byte> headerBuffer, in V1Header header)
        {
            if (headerBuffer.Length < V1FixedHeaderSize)
                throw new ArgumentException("Header buffer is too small.", nameof(headerBuffer));

            WriteInt32(headerBuffer, HeaderMagicOffset, header.Magic);
            WriteInt32(headerBuffer, HeaderVersionOffset, header.Version);
            WriteInt32(headerBuffer, HeaderSizeOffset, header.HeaderSize);
            WriteInt32(headerBuffer, HeaderCountOffset, header.Count);
            WriteInt32(headerBuffer, HeaderDimXOffset, header.Dimensions.X);
            WriteInt32(headerBuffer, HeaderDimYOffset, header.Dimensions.Y);
            WriteInt32(headerBuffer, HeaderDimZOffset, header.Dimensions.Z);
            WriteInt32(headerBuffer, HeaderDimWOffset, header.Dimensions.W);
        }

        private static void CopyBinsToBuffer(BinData[] bins, byte[] targetBuffer, int targetOffset, int dataByteSize)
        {
            var handle = GCHandle.Alloc(bins, GCHandleType.Pinned);
            try
            {
                Marshal.Copy(handle.AddrOfPinnedObject(), targetBuffer, targetOffset, dataByteSize);
            }
            finally
            {
                handle.Free();
            }
        }
        
        private static void CopyBufferToBins(byte[] sourceBuffer, int sourceOffset, BinData[] bins, int dataByteSize)
        {
            var handle = GCHandle.Alloc(bins, GCHandleType.Pinned);
            try
            {
                Marshal.Copy(sourceBuffer, sourceOffset, handle.AddrOfPinnedObject(), dataByteSize);
            }
            finally
            {
                handle.Free();
            }
        }

        private static void WriteInt32(Span<byte> buffer, int offset, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(offset, IntByteSize), value);
        }

        private static void ValidateFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
        }

        private static void EnsureParentDirectoryExists(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        #endregion

        #region Test

        /// <summary>
        /// 生成一个测试用规则货架数据（默认深度为 1）。
        /// </summary>
        public static WarehouseData CreateTestWarehouse(
            int rowCount,
            int columnCount,
            int levelCount,
            int depthCount = 1,
            Vector3 origin = default,
            Vector3? spacing = null)
        {
            if (rowCount <= 0 || columnCount <= 0 || levelCount <= 0 || depthCount <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(rowCount),
                    "All dimensions must be greater than zero.");

            Vector3 actualSpacing = spacing ?? Vector3.one;
            int totalCount = checked(rowCount * columnCount * levelCount * depthCount);
            var bins = new BinData[totalCount];

            int index = 0;
            for (int row = 0; row < rowCount; row++)
            {
                for (int column = 0; column < columnCount; column++)
                {
                    for (int level = 0; level < levelCount; level++)
                    {
                        for (int depth = 0; depth < depthCount; depth++)
                        {
                            bins[index++] = new BinData
                            {
                                Row = row,
                                Column = column,
                                Level = level,
                                Depth = depth,
                                PosX = origin.x + row * actualSpacing.x,
                                PosY = origin.y + level * actualSpacing.y,
                                PosZ = origin.z + column * actualSpacing.z,
                            };
                        }
                    }
                }
            }

            return new WarehouseData(bins, new Int4(levelCount, columnCount, rowCount, depthCount));
        }

        /// <summary>
        /// 快速生成一个 100x100x100 的测试货架（深度 1）。
        /// </summary>
        public static WarehouseData CreateTestWarehouse100x100x100(Vector3 origin = default, Vector3? spacing = null)
        {
            return CreateTestWarehouse(100, 100, 100, 1, origin, spacing);
        }

        #endregion
    }
}
