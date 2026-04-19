using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Text;
using TSLite.Buffers;
using TSLite.Model;
using TSLite.Storage.Format;

namespace TSLite.Storage.Segments;

/// <summary>
/// 不可变 Segment 文件的只读访问器。
/// <para>
/// Open 时一次性加载并解析索引；Block payload 解码按需进行，零拷贝优先。
/// 线程安全（实例共享只读 <c>byte[]</c>）。
/// </para>
/// <para>
/// v1 选择 <see cref="File.ReadAllBytes"/> 整段加载策略，原因：
/// <list type="bullet">
///   <item><description><c>.tslseg</c> 体量 v1 通常 &lt; 16 MB（受 MemTableFlushPolicy.MaxBytes 限制）。</description></item>
///   <item><description>完全 Safe-only，无需 <c>MemoryMappedFile</c> + unsafe 指针。</description></item>
///   <item><description>后续 PR 可加 <c>MemoryMappedSegmentReader</c> 走 SafeMemoryMappedViewHandle 路径，本 PR 不做。</description></item>
/// </list>
/// 注意：v1 仅支持 little-endian 主机字节序。
/// </para>
/// </summary>
public sealed class SegmentReader : IDisposable
{
    private byte[]? _bytes;
    private readonly SegmentReaderOptions _options;
    private readonly BlockDescriptor[] _blocks;

    /// <summary>段文件路径。</summary>
    public string Path { get; }

    /// <summary>段文件头部。</summary>
    public SegmentHeader Header { get; }

    /// <summary>段文件尾部。</summary>
    public SegmentFooter Footer { get; }

    /// <summary>段文件内的 Block 数量。</summary>
    public int BlockCount => _blocks.Length;

    /// <summary>段文件总字节数。</summary>
    public long FileLength { get; }

    /// <summary>段内所有 Block 中最小的时间戳（毫秒 UTC）；无 Block 时为 <see cref="long.MaxValue"/>。</summary>
    public long MinTimestamp { get; }

    /// <summary>段内所有 Block 中最大的时间戳（毫秒 UTC）；无 Block 时为 <see cref="long.MinValue"/>。</summary>
    public long MaxTimestamp { get; }

    /// <summary>所有 <see cref="BlockDescriptor"/>，按写入顺序（即 (SeriesId, FieldName) 升序）。</summary>
    public IReadOnlyList<BlockDescriptor> Blocks => _blocks;

    private SegmentReader(
        string path,
        byte[] bytes,
        SegmentHeader header,
        SegmentFooter footer,
        BlockDescriptor[] blocks,
        SegmentReaderOptions options)
    {
        Path = path;
        _bytes = bytes;
        Header = header;
        Footer = footer;
        _blocks = blocks;
        FileLength = bytes.Length;
        _options = options;

        long minTs = long.MaxValue;
        long maxTs = long.MinValue;
        foreach (var b in blocks)
        {
            if (b.MinTimestamp < minTs) minTs = b.MinTimestamp;
            if (b.MaxTimestamp > maxTs) maxTs = b.MaxTimestamp;
        }
        MinTimestamp = minTs;
        MaxTimestamp = maxTs;
    }

    /// <summary>
    /// 打开并解析段文件。
    /// </summary>
    /// <param name="path">段文件路径（通常扩展名为 <c>.tslseg</c>）。</param>
    /// <param name="options">读取选项；为 null 时使用 <see cref="SegmentReaderOptions.Default"/>。</param>
    /// <returns>已初始化的 <see cref="SegmentReader"/> 实例。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> 为 null。</exception>
    /// <exception cref="SegmentCorruptedException">文件格式不合法或校验失败时抛出。</exception>
    /// <exception cref="IOException">文件 IO 错误时抛出。</exception>
    public static SegmentReader Open(string path, SegmentReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        options ??= SegmentReaderOptions.Default;

        byte[] bytes = LoadAll(path);
        int minLen = FormatSizes.SegmentHeaderSize + FormatSizes.SegmentFooterSize;

        if (bytes.Length < minLen)
            throw new SegmentCorruptedException(path, 0,
                $"文件过短（{bytes.Length} 字节），最小需要 {minLen} 字节。");

        // 读 SegmentHeader（offset 0）
        var header = MemoryMarshal.Read<SegmentHeader>(bytes.AsSpan(0, FormatSizes.SegmentHeaderSize));

        if (!header.IsValid())
            throw new SegmentCorruptedException(path, 0,
                $"SegmentHeader Magic 或 FormatVersion 不匹配（Magic={Encoding.ASCII.GetString(header.Magic.AsReadOnlySpan())}, Version={header.FormatVersion}）。");

        if (header.HeaderSize != FormatSizes.SegmentHeaderSize)
            throw new SegmentCorruptedException(path, 0,
                $"SegmentHeader.HeaderSize={header.HeaderSize} 不等于预期值 {FormatSizes.SegmentHeaderSize}。");

        // 读 SegmentFooter（文件末尾 64 字节）
        int footerStart = bytes.Length - FormatSizes.SegmentFooterSize;
        var footer = MemoryMarshal.Read<SegmentFooter>(bytes.AsSpan(footerStart, FormatSizes.SegmentFooterSize));

        if (!footer.IsValid())
            throw new SegmentCorruptedException(path, footerStart,
                "SegmentFooter Magic 或 FormatVersion 不匹配。");

        // 校验 FooterOffset + 64 == bytes.Length
        long expectedFooterStart = footer.IndexOffset + (long)footer.IndexCount * FormatSizes.BlockIndexEntrySize;
        if (expectedFooterStart != footerStart)
            throw new SegmentCorruptedException(path, footerStart,
                $"SegmentFooter 位置不一致：IndexOffset({footer.IndexOffset}) + IndexCount({footer.IndexCount}) * {FormatSizes.BlockIndexEntrySize} = {expectedFooterStart}，但实际 FooterOffset = {footerStart}。");

        // 校验 FileLength 字段
        if (footer.FileLength != bytes.Length)
            throw new SegmentCorruptedException(path, footerStart,
                $"SegmentFooter.FileLength={footer.FileLength} 与实际文件长度 {bytes.Length} 不一致。");

        // 校验 IndexEntrySize（必须等于 48）
        long expectedIndexEnd = footer.IndexOffset + (long)footer.IndexCount * FormatSizes.BlockIndexEntrySize;
        if (expectedIndexEnd > footerStart)
            throw new SegmentCorruptedException(path, (long)footer.IndexOffset,
                $"BlockIndexEntry 区域越界：IndexOffset({footer.IndexOffset}) + IndexCount({footer.IndexCount}) * {FormatSizes.BlockIndexEntrySize} = {expectedIndexEnd} > FooterOffset({footerStart})。");

        // 读 BlockIndexEntry[]
        int indexByteLen = footer.IndexCount * FormatSizes.BlockIndexEntrySize;
        ReadOnlySpan<byte> indexBytes = bytes.AsSpan((int)footer.IndexOffset, indexByteLen);
        ReadOnlySpan<BlockIndexEntry> indexEntries = MemoryMarshal.Cast<byte, BlockIndexEntry>(indexBytes);

        // 校验 IndexCrc32
        if (options.VerifyIndexCrc && footer.IndexCount > 0)
        {
            uint computedCrc = Crc32.HashToUInt32(indexBytes);
            if (computedCrc != footer.Crc32)
                throw new SegmentCorruptedException(path, (long)footer.IndexOffset,
                    $"BlockIndexEntry[] CRC32 校验失败（期望 0x{footer.Crc32:X8}，实际 0x{computedCrc:X8}）。");
        }

        // 遍历 BlockHeader 构建 BlockDescriptor[]
        var blocks = new BlockDescriptor[footer.IndexCount];
        for (int i = 0; i < footer.IndexCount; i++)
        {
            BlockIndexEntry entry = indexEntries[i];
            long headerStart = entry.FileOffset;

            if (headerStart + FormatSizes.BlockHeaderSize > bytes.Length)
                throw new SegmentCorruptedException(path, headerStart,
                    $"BlockIndexEntry[{i}].FileOffset={headerStart} 指向越界区域。");

            var bh = MemoryMarshal.Read<BlockHeader>(
                bytes.AsSpan((int)headerStart, FormatSizes.BlockHeaderSize));

            // 校验 BlockHeader 与 IndexEntry 一致性
            if (bh.SeriesId != entry.SeriesId)
                throw new SegmentCorruptedException(path, headerStart,
                    $"Block[{i}] SeriesId 不一致：BlockHeader={bh.SeriesId}, IndexEntry={entry.SeriesId}。");

            if (bh.MinTimestamp != entry.MinTimestamp)
                throw new SegmentCorruptedException(path, headerStart,
                    $"Block[{i}] MinTimestamp 不一致：BlockHeader={bh.MinTimestamp}, IndexEntry={entry.MinTimestamp}。");

            if (bh.MaxTimestamp != entry.MaxTimestamp)
                throw new SegmentCorruptedException(path, headerStart,
                    $"Block[{i}] MaxTimestamp 不一致：BlockHeader={bh.MaxTimestamp}, IndexEntry={entry.MaxTimestamp}。");

            // 校验 BlockLength 一致性
            int expectedBlockLen = FormatSizes.BlockHeaderSize
                + bh.FieldNameUtf8Length
                + bh.TimestampPayloadLength
                + bh.ValuePayloadLength;

            if (expectedBlockLen != entry.BlockLength)
                throw new SegmentCorruptedException(path, headerStart,
                    $"Block[{i}] BlockLength 不一致：根据 BlockHeader 计算 {expectedBlockLen}，IndexEntry={entry.BlockLength}。");

            // 读字段名
            int fieldNameStart = (int)headerStart + FormatSizes.BlockHeaderSize;
            if (fieldNameStart + bh.FieldNameUtf8Length > bytes.Length)
                throw new SegmentCorruptedException(path, fieldNameStart,
                    $"Block[{i}] FieldName 区域越界。");

            string fieldName = Encoding.UTF8.GetString(
                bytes.AsSpan(fieldNameStart, bh.FieldNameUtf8Length));

            blocks[i] = new BlockDescriptor
            {
                Index = i,
                SeriesId = bh.SeriesId,
                MinTimestamp = bh.MinTimestamp,
                MaxTimestamp = bh.MaxTimestamp,
                Count = bh.Count,
                FieldType = bh.FieldType,
                TimestampEncoding = bh.Encoding,
                ValueEncoding = bh.Encoding,
                FieldName = fieldName,
                FileOffset = headerStart,
                BlockLength = entry.BlockLength,
                Crc32 = bh.Crc32,
                FieldNameUtf8Length = bh.FieldNameUtf8Length,
                TimestampPayloadLength = bh.TimestampPayloadLength,
                ValuePayloadLength = bh.ValuePayloadLength,
            };
        }

        return new SegmentReader(path, bytes, header, footer, blocks, options);
    }

    /// <summary>
    /// 按 SeriesId 过滤；返回属于该序列的所有 <see cref="BlockDescriptor"/>（按写入顺序）。
    /// </summary>
    /// <param name="seriesId">目标序列 ID。</param>
    /// <returns>匹配的 BlockDescriptor 列表（可能为空）。</returns>
    public IReadOnlyList<BlockDescriptor> FindBySeries(ulong seriesId)
    {
        var result = new List<BlockDescriptor>();
        foreach (var b in _blocks)
        {
            if (b.SeriesId == seriesId)
                result.Add(b);
        }
        return result;
    }

    /// <summary>
    /// 按 (SeriesId, FieldName) 过滤；通常 0 或 1 个结果。
    /// </summary>
    /// <param name="seriesId">目标序列 ID。</param>
    /// <param name="fieldName">目标字段名。</param>
    /// <returns>匹配的 BlockDescriptor 列表（通常 0 或 1 个）。</returns>
    public IReadOnlyList<BlockDescriptor> FindBySeriesAndField(ulong seriesId, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(fieldName);

        var result = new List<BlockDescriptor>();
        foreach (var b in _blocks)
        {
            if (b.SeriesId == seriesId &&
                string.Equals(b.FieldName, fieldName, StringComparison.Ordinal))
                result.Add(b);
        }
        return result;
    }

    /// <summary>
    /// 按时间范围过滤：返回与 [<paramref name="from"/>, <paramref name="toInclusive"/>] 有重叠的所有 <see cref="BlockDescriptor"/>。
    /// </summary>
    /// <param name="from">查询起始时间戳（含，毫秒 UTC）。</param>
    /// <param name="toInclusive">查询结束时间戳（含，毫秒 UTC）。</param>
    /// <returns>时间范围重叠的 BlockDescriptor 列表。</returns>
    public IReadOnlyList<BlockDescriptor> FindByTimeRange(long from, long toInclusive)
    {
        var result = new List<BlockDescriptor>();
        foreach (var b in _blocks)
        {
            // 重叠条件：Block.Min <= toInclusive && Block.Max >= from
            if (b.MinTimestamp <= toInclusive && b.MaxTimestamp >= from)
                result.Add(b);
        }
        return result;
    }

    /// <summary>
    /// 读取一个 Block 的零拷贝 payload 视图（生命周期等同于 <see cref="SegmentReader"/>）。
    /// </summary>
    /// <param name="descriptor">目标 Block 描述符。</param>
    /// <returns>包含三段 payload 的 <see cref="BlockData"/> 零拷贝视图。</returns>
    /// <exception cref="ObjectDisposedException">Reader 已被释放。</exception>
    /// <exception cref="SegmentCorruptedException">Block CRC32 校验失败时抛出（当 VerifyBlockCrc = true）。</exception>
    public BlockData ReadBlock(in BlockDescriptor descriptor)
    {
        ThrowIfDisposed();

        var bytes = _bytes!.AsSpan();

        int nameOff = (int)descriptor.FileOffset + FormatSizes.BlockHeaderSize;
        int tsOff = nameOff + descriptor.FieldNameUtf8Length;
        int valOff = tsOff + descriptor.TimestampPayloadLength;

        ReadOnlySpan<byte> fieldNameUtf8 = bytes.Slice(nameOff, descriptor.FieldNameUtf8Length);
        ReadOnlySpan<byte> tsPayload = bytes.Slice(tsOff, descriptor.TimestampPayloadLength);
        ReadOnlySpan<byte> valPayload = bytes.Slice(valOff, descriptor.ValuePayloadLength);

        if (_options.VerifyBlockCrc)
        {
            var crc = new Crc32();
            crc.Append(fieldNameUtf8);
            crc.Append(tsPayload);
            crc.Append(valPayload);
            uint computed = crc.GetCurrentHashAsUInt32();

            if (computed != descriptor.Crc32)
                throw new SegmentCorruptedException(Path, descriptor.FileOffset,
                    $"Block[{descriptor.Index}] CRC32 校验失败（期望 0x{descriptor.Crc32:X8}，实际 0x{computed:X8}）。");
        }

        return new BlockData
        {
            Descriptor = descriptor,
            FieldNameUtf8 = fieldNameUtf8,
            TimestampPayload = tsPayload,
            ValuePayload = valPayload,
        };
    }

    /// <summary>
    /// 解码一个 Block 的全部 <see cref="DataPoint"/>（分配新数组）。
    /// </summary>
    /// <param name="descriptor">目标 Block 描述符。</param>
    /// <returns>按时间戳升序排列的 DataPoint 数组。</returns>
    /// <exception cref="ObjectDisposedException">Reader 已被释放。</exception>
    /// <exception cref="SegmentCorruptedException">Block CRC32 校验失败时抛出（当 VerifyBlockCrc = true）。</exception>
    public DataPoint[] DecodeBlock(in BlockDescriptor descriptor)
    {
        var data = ReadBlock(descriptor);
        return BlockDecoder.Decode(descriptor, data.TimestampPayload, data.ValuePayload);
    }

    /// <summary>
    /// 解码并按 [<paramref name="from"/>, <paramref name="toInclusive"/>] 时间裁剪。
    /// </summary>
    /// <param name="descriptor">目标 Block 描述符。</param>
    /// <param name="from">起始时间戳（含，毫秒 UTC）。</param>
    /// <param name="toInclusive">结束时间戳（含，毫秒 UTC）。</param>
    /// <returns>在时间范围内的 DataPoint 数组（可能为空）。</returns>
    /// <exception cref="ObjectDisposedException">Reader 已被释放。</exception>
    /// <exception cref="SegmentCorruptedException">Block CRC32 校验失败时抛出（当 VerifyBlockCrc = true）。</exception>
    public DataPoint[] DecodeBlockRange(in BlockDescriptor descriptor, long from, long toInclusive)
    {
        var data = ReadBlock(descriptor);
        return BlockDecoder.DecodeRange(descriptor, data.TimestampPayload, data.ValuePayload, from, toInclusive);
    }

    /// <summary>
    /// 释放内部 <c>byte[]</c> 引用以便 GC 回收。调用后不可再调用其他方法。
    /// </summary>
    public void Dispose()
    {
        _bytes = null;
    }

    // ── 受保护虚方法（供测试或未来 mmap 派生类替换） ──────────────────────────

    /// <summary>
    /// 加载段文件全部字节（供测试或未来 mmap 等路径复用）。
    /// </summary>
    /// <param name="path">段文件路径。</param>
    /// <returns>文件的全部字节。</returns>
    internal static byte[] LoadAll(string path) => File.ReadAllBytes(path);

    // ── 私有辅助 ──────────────────────────────────────────────────────────────

    private void ThrowIfDisposed()
    {
        if (_bytes is null)
            throw new ObjectDisposedException(nameof(SegmentReader));
    }
}
