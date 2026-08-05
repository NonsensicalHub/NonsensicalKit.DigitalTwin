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
    /// 布局编辑器用的深度方向配置。正式运行时忽略；写入 .dat 扩展头，供下次配置回读。
    /// </summary>
    public enum WarehouseDepthDirectionMode
    {
        Detailed = 0,
        OddEven = 1,
    }

    /// <summary>
    /// 布局编辑器用的深度方向配置快照。
    /// </summary>
    public sealed class WarehouseLayoutDepthConfig
    {
        public WarehouseDepthDirectionMode Mode { get; set; }
        public Vector3 EvenColumnDepthDirection { get; set; }
        public Vector3 OddColumnDepthDirection { get; set; }
        public Vector3[] ColumnDepthDirections { get; set; }

        public WarehouseLayoutDepthConfig(
            WarehouseDepthDirectionMode mode,
            Vector3 evenColumnDepthDirection,
            Vector3 oddColumnDepthDirection,
            Vector3[] columnDepthDirections)
        {
            Mode = mode;
            EvenColumnDepthDirection = evenColumnDepthDirection;
            OddColumnDepthDirection = oddColumnDepthDirection;
            ColumnDepthDirections = columnDepthDirections ?? Array.Empty<Vector3>();
        }
    }

    /// <summary>
    /// 仓库数据对象，包含货位数组与维度信息。
    /// </summary>
    public sealed class WarehouseData
    {
        public BinData[] Bins { get; }
        public Int4 Dimensions { get; }

        /// <summary>可选：布局深度配置（仅编辑器回读使用，运行时可不读）。</summary>
        public WarehouseLayoutDepthConfig LayoutDepthConfig { get; }

        /// <summary>可选：货位禁用规则（加载时应用到 SlotEnabled / ShowCargo）。</summary>
        public WarehouseSlotDisableRule[] SlotDisableRules { get; }

        public WarehouseData(
            BinData[] bins,
            Int4 dimensions,
            WarehouseLayoutDepthConfig layoutDepthConfig = null,
            WarehouseSlotDisableRule[] slotDisableRules = null)
        {
            Bins = bins ?? Array.Empty<BinData>();
            Dimensions = dimensions;
            LayoutDepthConfig = layoutDepthConfig;
            SlotDisableRules = slotDisableRules;
        }

        public static WarehouseData Create(
            BinData[] bins,
            WarehouseLayoutDepthConfig layoutDepthConfig = null,
            WarehouseSlotDisableRule[] slotDisableRules = null)
        {
            var dimensions = BinDataIO.InferDimensions(bins);
            return new WarehouseData(bins, dimensions, layoutDepthConfig, slotDisableRules);
        }
    }

    /// <summary>
    /// 仓库二进制数据读写工具，支持同步、异步与 StreamingAssets 加载。
    /// </summary>
    public static class BinDataIO
    {
        private static readonly int StructSize = Marshal.SizeOf<BinData>();
        private const int IntByteSize = sizeof(int);
        private const int FloatByteSize = sizeof(float);
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

        /// <summary>布局深度配置扩展段魔数 "DLAY"。</summary>
        private const int LayoutDepthMagic = 0x59414C44;

        private const int LayoutDepthVersion = 1;

        /// <summary>扩展段固定前缀：magic + version + mode + even3 + odd3 + detailedCount。</summary>
        private const int LayoutDepthFixedSize = IntByteSize * 4 + FloatByteSize * 6;

        /// <summary>货位禁用规则扩展段魔数 "DSBL"。</summary>
        private const int SlotDisableMagic = 0x4C425344;

        private const int SlotDisableVersion = 1;

        /// <summary>单条规则：flags + 8 个 int。</summary>
        private const int SlotDisableRuleStride = IntByteSize * 9;

        /// <summary>禁用规则段固定头：magic + version + ruleCount。</summary>
        private const int SlotDisableFixedHeaderSize = IntByteSize * 3;

        /// <summary>单轴上限，防止异常头字段直接撑爆内存。</summary>
        private const int MaxDimensionAxis = 10000;

        /// <summary>货位总数上限（Array4 元素数）。100³ 测试仓约 1e6，此处留余量。</summary>
        private const long MaxTotalCells = 16_000_000L;

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

        public static void SaveSync(
            BinData[] bins,
            string filePath,
            WarehouseLayoutDepthConfig layoutDepthConfig = null,
            WarehouseSlotDisableRule[] slotDisableRules = null)
        {
            var data = WarehouseData.Create(bins, layoutDepthConfig, slotDisableRules);
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

        public static async UniTask SaveAsync(
            BinData[] bins,
            string filePath,
            WarehouseLayoutDepthConfig layoutDepthConfig = null,
            WarehouseSlotDisableRule[] slotDisableRules = null)
        {
            var data = WarehouseData.Create(bins, layoutDepthConfig, slotDisableRules);

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

            byte[] extensionSection = SerializeExtensionSections(data.LayoutDepthConfig, data.SlotDisableRules);
            int extensionSize = extensionSection?.Length ?? 0;

            int headerSize = checked(V1FixedHeaderSize + extensionSize);
            var header = new V1Header(FileMagic, CurrentVersion, headerSize, count, dimensions);
            byte[] buffer = new byte[checked(headerSize + dataByteSize)];
            WriteV1Header(buffer.AsSpan(0, V1FixedHeaderSize), in header);

            if (extensionSize > 0)
            {
                Buffer.BlockCopy(extensionSection, 0, buffer, V1FixedHeaderSize, extensionSize);
            }

            if (count > 0)
            {
                CopyBinsToBuffer(bins, buffer, headerSize, dataByteSize);
            }

            return buffer;
        }

        private static byte[] SerializeExtensionSections(
            WarehouseLayoutDepthConfig layoutDepthConfig,
            WarehouseSlotDisableRule[] slotDisableRules)
        {
            byte[] depthSection = layoutDepthConfig != null
                ? SerializeLayoutDepthConfig(layoutDepthConfig)
                : null;
            byte[] disableSection = HasDisableRules(slotDisableRules)
                ? SerializeSlotDisableRules(slotDisableRules)
                : null;

            int depthSize = depthSection?.Length ?? 0;
            int disableSize = disableSection?.Length ?? 0;
            int total = checked(depthSize + disableSize);
            if (total == 0)
            {
                return null;
            }

            byte[] buffer = new byte[total];
            int offset = 0;
            if (depthSize > 0)
            {
                Buffer.BlockCopy(depthSection, 0, buffer, offset, depthSize);
                offset += depthSize;
            }

            if (disableSize > 0)
            {
                Buffer.BlockCopy(disableSection, 0, buffer, offset, disableSize);
            }

            return buffer;
        }

        private static bool HasDisableRules(WarehouseSlotDisableRule[] rules)
        {
            if (rules == null || rules.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < rules.Length; i++)
            {
                if (rules[i] != null && rules[i].HasAnyConstraint)
                {
                    return true;
                }
            }

            return false;
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
                var legacyData = new WarehouseData(binsFromLegacy, inferred);
                ValidateWarehouseData(legacyData);
                return legacyData;
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

            WarehouseLayoutDepthConfig layoutDepthConfig = null;
            WarehouseSlotDisableRule[] slotDisableRules = null;
            int extensionSize = header.HeaderSize - V1FixedHeaderSize;
            if (extensionSize > 0)
            {
                ReadExtensionSections(
                    buffer,
                    V1FixedHeaderSize,
                    extensionSize,
                    out layoutDepthConfig,
                    out slotDisableRules);
            }

            var data = new WarehouseData(bins, header.Dimensions, layoutDepthConfig, slotDisableRules);
            ValidateWarehouseData(data);
            return data;
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

            if (dimensions.X > MaxDimensionAxis || dimensions.Y > MaxDimensionAxis ||
                dimensions.Z > MaxDimensionAxis || dimensions.W > MaxDimensionAxis)
            {
                throw new InvalidDataException(
                    $"Warehouse dimension axis exceeds limit {MaxDimensionAxis}: {dimensions}.");
            }

            long totalCells = (long)dimensions.X * dimensions.Y * dimensions.Z * dimensions.W;
            if (totalCells > MaxTotalCells)
            {
                throw new InvalidDataException(
                    $"Warehouse cell count {totalCells} exceeds limit {MaxTotalCells}: {dimensions}.");
            }
        }

        private static void ValidateWarehouseData(WarehouseData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.Bins == null)
                throw new ArgumentException("WarehouseData.Bins cannot be null.", nameof(data));
            ValidateDimensions(data.Dimensions);
            ValidateBinsAgainstDimensions(data);
        }

        private static void ValidateBinsAgainstDimensions(WarehouseData data)
        {
            Int4 dimensions = data.Dimensions;
            BinData[] bins = data.Bins;
            for (int i = 0; i < bins.Length; i++)
            {
                BinData bin = bins[i];
                if (bin.Level < 0 || bin.Level >= dimensions.X ||
                    bin.Column < 0 || bin.Column >= dimensions.Y ||
                    bin.Row < 0 || bin.Row >= dimensions.Z ||
                    bin.Depth < 0 || bin.Depth >= dimensions.W)
                {
                    throw new InvalidDataException(
                        $"Bin[{i}] index out of range: ({bin.Level},{bin.Column},{bin.Row},{bin.Depth}), dimensions={dimensions}.");
                }
            }
        }

        /// <summary>
        /// 运行时加载前校验维度与货位索引（供 <see cref="WarehouseBinDataStore"/> 使用）。
        /// </summary>
        internal static void EnsureValidWarehouseData(WarehouseData data)
        {
            ValidateWarehouseData(data);
        }

        private static void ReadExtensionSections(
            byte[] buffer,
            int offset,
            int availableSize,
            out WarehouseLayoutDepthConfig layoutDepthConfig,
            out WarehouseSlotDisableRule[] slotDisableRules)
        {
            layoutDepthConfig = null;
            slotDisableRules = null;

            int cursor = offset;
            int end = checked(offset + availableSize);
            while (cursor + IntByteSize <= end)
            {
                int magic = ReadInt32(buffer, cursor);
                int remaining = end - cursor;
                if (magic == LayoutDepthMagic)
                {
                    if (!TryReadLayoutDepthConfig(buffer, cursor, remaining, out var config, out int consumed))
                    {
                        break;
                    }

                    layoutDepthConfig = config;
                    cursor += consumed;
                    continue;
                }

                if (magic == SlotDisableMagic)
                {
                    if (!TryReadSlotDisableRules(buffer, cursor, remaining, out var rules, out int consumed))
                    {
                        break;
                    }

                    slotDisableRules = rules;
                    cursor += consumed;
                    continue;
                }

                // 未知扩展段，停止解析以保持兼容。
                break;
            }
        }

        private static byte[] SerializeLayoutDepthConfig(WarehouseLayoutDepthConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            Vector3[] detailed = config.ColumnDepthDirections ?? Array.Empty<Vector3>();
            if (detailed.Length > MaxDimensionAxis)
            {
                throw new InvalidDataException(
                    $"Layout depth detailed count {detailed.Length} exceeds limit {MaxDimensionAxis}.");
            }

            int byteSize = checked(LayoutDepthFixedSize + detailed.Length * FloatByteSize * 3);
            byte[] buffer = new byte[byteSize];
            int offset = 0;

            WriteInt32(buffer, offset, LayoutDepthMagic);
            offset += IntByteSize;
            WriteInt32(buffer, offset, LayoutDepthVersion);
            offset += IntByteSize;
            WriteInt32(buffer, offset, (int)config.Mode);
            offset += IntByteSize;
            WriteVector3(buffer, offset, config.EvenColumnDepthDirection);
            offset += FloatByteSize * 3;
            WriteVector3(buffer, offset, config.OddColumnDepthDirection);
            offset += FloatByteSize * 3;
            WriteInt32(buffer, offset, detailed.Length);
            offset += IntByteSize;

            for (int i = 0; i < detailed.Length; i++)
            {
                WriteVector3(buffer, offset, detailed[i]);
                offset += FloatByteSize * 3;
            }

            return buffer;
        }

        private static bool TryReadLayoutDepthConfig(
            byte[] buffer,
            int offset,
            int availableSize,
            out WarehouseLayoutDepthConfig config,
            out int bytesConsumed)
        {
            config = null;
            bytesConsumed = 0;
            if (availableSize < LayoutDepthFixedSize)
                return false;

            int magic = ReadInt32(buffer, offset);
            if (magic != LayoutDepthMagic)
                return false;

            int version = ReadInt32(buffer, offset + IntByteSize);
            if (version != LayoutDepthVersion)
                return false;

            int modeValue = ReadInt32(buffer, offset + IntByteSize * 2);
            if (modeValue != (int)WarehouseDepthDirectionMode.Detailed &&
                modeValue != (int)WarehouseDepthDirectionMode.OddEven)
            {
                return false;
            }

            int cursor = offset + IntByteSize * 3;
            Vector3 even = ReadVector3(buffer, cursor);
            cursor += FloatByteSize * 3;
            Vector3 odd = ReadVector3(buffer, cursor);
            cursor += FloatByteSize * 3;
            int detailedCount = ReadInt32(buffer, cursor);
            cursor += IntByteSize;

            if (detailedCount < 0 || detailedCount > MaxDimensionAxis)
                return false;

            int required = checked(LayoutDepthFixedSize + detailedCount * FloatByteSize * 3);
            if (availableSize < required)
                return false;

            var detailed = new Vector3[detailedCount];
            for (int i = 0; i < detailedCount; i++)
            {
                detailed[i] = ReadVector3(buffer, cursor);
                cursor += FloatByteSize * 3;
            }

            config = new WarehouseLayoutDepthConfig(
                (WarehouseDepthDirectionMode)modeValue,
                even,
                odd,
                detailed);
            bytesConsumed = required;
            return true;
        }

        private static byte[] SerializeSlotDisableRules(WarehouseSlotDisableRule[] rules)
        {
            int ruleCount = rules?.Length ?? 0;
            if (ruleCount > MaxDimensionAxis)
            {
                throw new InvalidDataException(
                    $"Slot disable rule count {ruleCount} exceeds limit {MaxDimensionAxis}.");
            }

            int byteSize = checked(SlotDisableFixedHeaderSize + ruleCount * SlotDisableRuleStride);
            byte[] buffer = new byte[byteSize];
            int offset = 0;
            WriteInt32(buffer, offset, SlotDisableMagic);
            offset += IntByteSize;
            WriteInt32(buffer, offset, SlotDisableVersion);
            offset += IntByteSize;
            WriteInt32(buffer, offset, ruleCount);
            offset += IntByteSize;

            for (int i = 0; i < ruleCount; i++)
            {
                WarehouseSlotDisableRule rule = rules[i] ?? new WarehouseSlotDisableRule();
                int flags = 0;
                if (rule.Enabled) flags |= 1 << 0;
                if (rule.ConstrainRow) flags |= 1 << 1;
                if (rule.ConstrainColumn) flags |= 1 << 2;
                if (rule.ConstrainLevel) flags |= 1 << 3;
                if (rule.ConstrainDepth) flags |= 1 << 4;

                WriteInt32(buffer, offset, flags);
                offset += IntByteSize;
                WriteInt32(buffer, offset, rule.RowMin);
                offset += IntByteSize;
                WriteInt32(buffer, offset, rule.RowMax);
                offset += IntByteSize;
                WriteInt32(buffer, offset, rule.ColumnMin);
                offset += IntByteSize;
                WriteInt32(buffer, offset, rule.ColumnMax);
                offset += IntByteSize;
                WriteInt32(buffer, offset, rule.LevelMin);
                offset += IntByteSize;
                WriteInt32(buffer, offset, rule.LevelMax);
                offset += IntByteSize;
                WriteInt32(buffer, offset, rule.DepthMin);
                offset += IntByteSize;
                WriteInt32(buffer, offset, rule.DepthMax);
                offset += IntByteSize;
            }

            return buffer;
        }

        private static bool TryReadSlotDisableRules(
            byte[] buffer,
            int offset,
            int availableSize,
            out WarehouseSlotDisableRule[] rules,
            out int bytesConsumed)
        {
            rules = null;
            bytesConsumed = 0;
            if (availableSize < SlotDisableFixedHeaderSize)
            {
                return false;
            }

            int magic = ReadInt32(buffer, offset);
            if (magic != SlotDisableMagic)
            {
                return false;
            }

            int version = ReadInt32(buffer, offset + IntByteSize);
            if (version != SlotDisableVersion)
            {
                return false;
            }

            int ruleCount = ReadInt32(buffer, offset + IntByteSize * 2);
            if (ruleCount < 0 || ruleCount > MaxDimensionAxis)
            {
                return false;
            }

            int required = checked(SlotDisableFixedHeaderSize + ruleCount * SlotDisableRuleStride);
            if (availableSize < required)
            {
                return false;
            }

            var result = new WarehouseSlotDisableRule[ruleCount];
            int cursor = offset + SlotDisableFixedHeaderSize;
            for (int i = 0; i < ruleCount; i++)
            {
                int flags = ReadInt32(buffer, cursor);
                cursor += IntByteSize;
                var rule = new WarehouseSlotDisableRule
                {
                    Enabled = (flags & (1 << 0)) != 0,
                    ConstrainRow = (flags & (1 << 1)) != 0,
                    ConstrainColumn = (flags & (1 << 2)) != 0,
                    ConstrainLevel = (flags & (1 << 3)) != 0,
                    ConstrainDepth = (flags & (1 << 4)) != 0,
                    RowMin = ReadInt32(buffer, cursor),
                };
                cursor += IntByteSize;
                rule.RowMax = ReadInt32(buffer, cursor);
                cursor += IntByteSize;
                rule.ColumnMin = ReadInt32(buffer, cursor);
                cursor += IntByteSize;
                rule.ColumnMax = ReadInt32(buffer, cursor);
                cursor += IntByteSize;
                rule.LevelMin = ReadInt32(buffer, cursor);
                cursor += IntByteSize;
                rule.LevelMax = ReadInt32(buffer, cursor);
                cursor += IntByteSize;
                rule.DepthMin = ReadInt32(buffer, cursor);
                cursor += IntByteSize;
                rule.DepthMax = ReadInt32(buffer, cursor);
                cursor += IntByteSize;
                result[i] = rule;
            }

            rules = result;
            bytesConsumed = required;
            return true;
        }

        private static int ReadInt32(byte[] buffer, int offset)
        {
            return BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset, IntByteSize));
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset, IntByteSize), value);
        }

        private static float ReadSingle(byte[] buffer, int offset)
        {
            // 当前 Unity 目标 API 无 BinaryPrimitives.ReadSingleLittleEndian，经 Int32 位型转读保证小端。
            int bits = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset, FloatByteSize));
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }

        private static void WriteSingle(byte[] buffer, int offset, float value)
        {
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset, FloatByteSize), bits);
        }

        private static Vector3 ReadVector3(byte[] buffer, int offset)
        {
            return new Vector3(
                ReadSingle(buffer, offset),
                ReadSingle(buffer, offset + FloatByteSize),
                ReadSingle(buffer, offset + FloatByteSize * 2));
        }

        private static void WriteVector3(byte[] buffer, int offset, Vector3 value)
        {
            WriteSingle(buffer, offset, value.x);
            WriteSingle(buffer, offset + FloatByteSize, value.y);
            WriteSingle(buffer, offset + FloatByteSize * 2, value.z);
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
        /// columnDepthDirections 为每列 Depth 每 +1 的偏移；为 null 时不加深度偏移。
        /// Depth=0 在基准点：Pos = origin + 排/层/列间距 + 列深度方向 × Depth。
        /// </summary>
        public static WarehouseData CreateTestWarehouse(
            int rowCount,
            int columnCount,
            int levelCount,
            int depthCount = 1,
            Vector3 origin = default,
            Vector3? spacing = null,
            Vector3[] columnDepthDirections = null)
        {
            if (rowCount <= 0 || columnCount <= 0 || levelCount <= 0 || depthCount <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(rowCount),
                    "All dimensions must be greater than zero.");

            if (columnDepthDirections != null && columnDepthDirections.Length != columnCount)
            {
                throw new ArgumentException(
                    $"columnDepthDirections length ({columnDepthDirections.Length}) must equal columnCount ({columnCount}).",
                    nameof(columnDepthDirections));
            }

            Vector3 actualSpacing = spacing ?? Vector3.one;
            int totalCount = checked(rowCount * columnCount * levelCount * depthCount);
            var bins = new BinData[totalCount];

            int index = 0;
            for (int row = 0; row < rowCount; row++)
            {
                for (int column = 0; column < columnCount; column++)
                {
                    Vector3 depthStep = columnDepthDirections != null
                        ? columnDepthDirections[column]
                        : Vector3.zero;

                    for (int level = 0; level < levelCount; level++)
                    {
                        var basePos = new Vector3(
                            origin.x + row * actualSpacing.x,
                            origin.y + level * actualSpacing.y,
                            origin.z + column * actualSpacing.z);

                        for (int depth = 0; depth < depthCount; depth++)
                        {
                            Vector3 pos = basePos + depthStep * depth;
                            bins[index++] = new BinData
                            {
                                Row = row,
                                Column = column,
                                Level = level,
                                Depth = depth,
                                PosX = pos.x,
                                PosY = pos.y,
                                PosZ = pos.z,
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

        public static WarehouseData CreateTestWarehouse10x10x10( Vector3 origin = default, Vector3? spacing = null)
        {
            return CreateTestWarehouse(10, 10, 10,  1, origin, spacing);
        }

        #endregion
    }
}
