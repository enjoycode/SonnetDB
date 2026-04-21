using System.Buffers;
using System.Diagnostics;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using TSLite.Memory;
using TSLite.Model;
using TSLite.Storage.Format;

namespace TSLite.Storage.Segments;

/// <summary>
/// 不可变 Segment 文件构建器：把 <see cref="MemTable"/> 中的有序 (SeriesId, FieldName) 桶
/// 写成 <c>.tslseg</c> 文件，使用临时文件 + 原子 rename 保证崩溃安全。
/// <para>
/// 单次构建一次性生效（不支持增量写入；要新增数据请构建新的 Segment）。
/// </para>
/// <para>
/// 文件物理布局（所有多字节整数使用 little-endian）：
/// <code>
/// ┌─────────────────────────────────────────────────────────────────┐
/// │  SegmentHeader  (固定 64 字节，offset = 0)                       │
/// │    Magic = "TSLSEGv1"                                           │
/// │    FormatVersion = 1                                            │
/// │    SegmentId                                                    │
/// │    CreatedAtUtcTicks                                            │
/// │    BlockCount（回填）                                            │
/// ├─────────────────────────────────────────────────────────────────┤
/// │  Block 1 ... Block N  （每个 (SeriesId, FieldName) 桶 = 1 Block） │
/// │  ┌───────────────────────────────────────────────────────────┐  │
/// │  │ BlockHeader (固定 64 字节)                                  │  │
/// │  │   SeriesId / Min/MaxTimestamp / Count                      │  │
/// │  │   FieldNameUtf8Length / TimestampPayloadLength             │  │
/// │  │   ValuePayloadLength / Encoding=None / FieldType           │  │
/// │  │   Crc32 = CRC32(FieldNameUtf8 ++ TsPayload ++ ValPayload)  │  │
/// │  ├───────────────────────────────────────────────────────────┤  │
/// │  │ FieldNameUtf8          (变长)                               │  │
/// │  ├───────────────────────────────────────────────────────────┤  │
/// │  │ TimestampPayload       (Count × 8B int64 LE)               │  │
/// │  ├───────────────────────────────────────────────────────────┤  │
/// │  │ ValuePayload           (按 FieldType 编码)                  │  │
/// │  │   Float64: Count×8B  Int64: Count×8B  Bool: Count×1B      │  │
/// │  │   String: 重复 Count 次 (int32 len + UTF-8 bytes)           │  │
/// │  └───────────────────────────────────────────────────────────┘  │
/// ├─────────────────────────────────────────────────────────────────┤
/// │  BlockIndexEntry[BlockCount]  (每项 48 字节)                     │
/// │    SeriesId / Min/MaxTimestamp / FileOffset / BlockLength       │
/// │    FieldNameHash = XxHash32(FieldNameUtf8)                      │
/// ├─────────────────────────────────────────────────────────────────┤
/// │  SegmentFooter  (固定 64 字节)                                   │
/// │    Magic = "TSLSEGv1"                                           │
/// │    IndexOffset / IndexCount                                     │
/// │    FileLength  (= FooterOffset + 64)                            │
/// │    Crc32 = CRC32(整个 BlockIndexEntry[] 字节)                    │
/// └─────────────────────────────────────────────────────────────────┘
///
/// 不变量：
///   1. IndexOffset == SegmentHeaderSize + Σ BlockLength
///   2. FooterOffset == IndexOffset + IndexCount × 48
///   3. FooterOffset + 64 == FileLength
///   4. BlockIndexEntry[i].FileOffset 指向第 i 个 BlockHeader 起点
///   5. 文件以 SegmentFooter.Magic == "TSLSEGv1" 收尾
/// </code>
/// </para>
/// </summary>
public sealed class SegmentWriter
{
    /// <summary>段文件扩展名。</summary>
    public const string FileExtension = ".tslseg";

    private readonly SegmentWriterOptions _options;

    /// <summary>
    /// 创建 <see cref="SegmentWriter"/> 实例。
    /// </summary>
    /// <param name="options">写入选项；为 null 时使用 <see cref="SegmentWriterOptions.Default"/>。</param>
    public SegmentWriter(SegmentWriterOptions? options = null)
    {
        _options = options ?? SegmentWriterOptions.Default;
    }

    /// <summary>
    /// 直接把 <see cref="MemTable"/> 写到指定路径。
    /// </summary>
    /// <param name="memTable">要写入的 MemTable 实例。</param>
    /// <param name="segmentId">段唯一标识符（单调递增）。</param>
    /// <param name="path">目标文件路径（扩展名通常为 <c>.tslseg</c>）。</param>
    /// <returns>构建结果，含文件路径、Block 数量、时间范围、偏移等信息。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="memTable"/> 或 <paramref name="path"/> 为 null。</exception>
    public SegmentBuildResult WriteFrom(MemTable memTable, long segmentId, string path)
    {
        ArgumentNullException.ThrowIfNull(memTable);
        return Write(memTable.SnapshotAll(), segmentId, path);
    }

    /// <summary>
    /// 通用入口：从外部提供有序桶序列写入。供测试与未来的 Compaction 复用。
    /// </summary>
    /// <param name="series">要写入的 <see cref="MemTableSeries"/> 列表（允许包含空桶，将被自动过滤）。</param>
    /// <param name="segmentId">段唯一标识符（单调递增）。</param>
    /// <param name="path">目标文件路径（扩展名通常为 <c>.tslseg</c>）。</param>
    /// <returns>构建结果，含文件路径、Block 数量、时间范围、偏移等信息。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="series"/> 或 <paramref name="path"/> 为 null。</exception>
    /// <exception cref="IOException">临时文件已存在（路径冲突）或 IO 错误时抛出。</exception>
    public SegmentBuildResult Write(IReadOnlyList<MemTableSeries> series, long segmentId, string path)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(path);

        // Sort by (SeriesId, FieldName Ordinal)，过滤空桶
        var sorted = new List<MemTableSeries>(series.Count);
        foreach (var s in series)
        {
            if (s.Count > 0)
                sorted.Add(s);
        }
        sorted.Sort(static (a, b) =>
        {
            int cmp = a.Key.SeriesId.CompareTo(b.Key.SeriesId);
            return cmp != 0 ? cmp : string.Compare(a.Key.FieldName, b.Key.FieldName, StringComparison.Ordinal);
        });

        string tempPath = path + _options.TempFileSuffix;
        var sw = Stopwatch.StartNew();

        // State tracked across phases (declared before try so accessible in return)
        long segMinTs = long.MaxValue;
        long segMaxTs = long.MinValue;
        long indexOffset = 0L;
        long footerOffset = 0L;
        var indexEntries = new List<BlockIndexEntry>(sorted.Count);
        bool tempFileCreated = false;

        try
        {
            using var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            tempFileCreated = true;
            var bs = new BufferedStream(fs, _options.BufferSize);

            // ─── 阶段 A：写占位 SegmentHeader ──────────────────────────────
            var placeholderHeader = SegmentHeader.CreateNew(segmentId);
            WriteStructToStream(bs, in placeholderHeader);
            long currentOffset = FormatSizes.SegmentHeaderSize;

            // ─── 阶段 B：逐 Block 写入 ─────────────────────────────────────
            foreach (var bucket in sorted)
            {
                // 崩溃注入钩子（仅测试使用）
                _options.FailAt?.Invoke(currentOffset);

                long blockOffset = currentOffset;
                var points = bucket.Snapshot();

                // 编码字段名 UTF-8
                int fieldNameMaxBytes = Encoding.UTF8.GetMaxByteCount(bucket.Key.FieldName.Length);
                byte[] fieldNameBuf = ArrayPool<byte>.Shared.Rent(Math.Max(fieldNameMaxBytes, 1));
                try
                {
                    int fieldNameLen = Encoding.UTF8.GetBytes(bucket.Key.FieldName, fieldNameBuf);
                    ReadOnlySpan<byte> fieldNameSpan = fieldNameBuf.AsSpan(0, fieldNameLen);

                    // 编码时间戳载荷：根据 SegmentWriterOptions.TimestampEncoding 选择 V1 或 V2 编码
                    bool useDeltaTs = (_options.TimestampEncoding & BlockEncoding.DeltaTimestamp) != 0
                        && points.Length > 0;

                    int tsPayloadLen;
                    if (useDeltaTs)
                    {
                        // 先把时间戳收集到临时 long[]，再调用 TimestampCodec
                        long[] tsArr = ArrayPool<long>.Shared.Rent(points.Length);
                        try
                        {
                            for (int i = 0; i < points.Length; i++)
                                tsArr[i] = points.Span[i].Timestamp;
                            tsPayloadLen = TimestampCodec.MeasureDeltaOfDelta(tsArr.AsSpan(0, points.Length));
                            byte[] tsBuf = ArrayPool<byte>.Shared.Rent(Math.Max(tsPayloadLen, 1));
                            try
                            {
                                if (tsPayloadLen > 0)
                                    TimestampCodec.WriteDeltaOfDelta(tsArr.AsSpan(0, points.Length), tsBuf.AsSpan(0, tsPayloadLen));
                                ReadOnlySpan<byte> tsSpan = tsBuf.AsSpan(0, tsPayloadLen);
                                WriteOneBlock(bs, bucket, points, fieldNameSpan, tsSpan, BlockEncoding.DeltaTimestamp,
                                    blockOffset, indexEntries, ref segMinTs, ref segMaxTs, ref currentOffset);
                            }
                            finally { ArrayPool<byte>.Shared.Return(tsBuf); }
                        }
                        finally { ArrayPool<long>.Shared.Return(tsArr); }
                        continue;
                    }

                    // V1：Count × 8B int64 LE
                    tsPayloadLen = points.Length * 8;
                    byte[] tsBufV1 = ArrayPool<byte>.Shared.Rent(Math.Max(tsPayloadLen, 1));
                    try
                    {
                        if (tsPayloadLen > 0)
                        {
                            var tsWriter = new IO.SpanWriter(tsBufV1.AsSpan(0, tsPayloadLen));
                            foreach (var dp in points.Span)
                                tsWriter.WriteInt64(dp.Timestamp);
                        }

                        ReadOnlySpan<byte> tsSpan = tsBufV1.AsSpan(0, tsPayloadLen);
                        WriteOneBlock(bs, bucket, points, fieldNameSpan, tsSpan, BlockEncoding.None,
                            blockOffset, indexEntries, ref segMinTs, ref segMaxTs, ref currentOffset);
                    }
                    finally { ArrayPool<byte>.Shared.Return(tsBufV1); }
                }
                finally { ArrayPool<byte>.Shared.Return(fieldNameBuf); }
            }

            // ─── 阶段 C：写 BlockIndexEntry[] + 计算 IndexCrc32 ────────────
            indexOffset = currentOffset;
            var indexCrc = new Crc32();
            foreach (var entry in indexEntries)
                WriteStructToStreamAndHash(bs, in entry, indexCrc);

            uint indexCrc32 = indexCrc.GetCurrentHashAsUInt32();

            // ─── 阶段 D：写 SegmentFooter ──────────────────────────────────
            footerOffset = indexOffset + (long)indexEntries.Count * FormatSizes.BlockIndexEntrySize;
            long fileLength = footerOffset + FormatSizes.SegmentFooterSize;

            var footer = SegmentFooter.CreateNew(indexEntries.Count, indexOffset, fileLength);
            footer.Crc32 = indexCrc32;
            WriteStructToStream(bs, in footer);

            // ─── 阶段 E：Seek(0) 回填 SegmentHeader ───────────────────────
            bs.Flush();
            bs.Seek(0, SeekOrigin.Begin);

            var finalHeader = SegmentHeader.CreateNew(segmentId);
            finalHeader.BlockCount = indexEntries.Count;
            WriteStructToStream(bs, in finalHeader);

            bs.Flush();
            if (_options.FsyncOnCommit)
                fs.Flush(true);

            bs.Dispose();
            // using var fs ensures fs is disposed when the try block exits (idempotent if bs already closed it)
        }
        catch
        {
            // 仅删除我们创建的临时文件，不删除原本已存在的文件
            if (tempFileCreated)
                try { File.Delete(tempPath); } catch { }
            throw;
        }

        // 原子替换：临时文件 → 目标文件
        File.Move(tempPath, path, overwrite: true);

        // 崩溃注入钩子（仅测试使用）：rename 完成后、Checkpoint 写入之前
        _options.PostRenameAction?.Invoke();

        sw.Stop();
        long durationMicros = sw.ElapsedTicks * 1_000_000L / Stopwatch.Frequency;

        return new SegmentBuildResult(
            Path: path,
            SegmentId: segmentId,
            BlockCount: indexEntries.Count,
            TotalBytes: new FileInfo(path).Length,
            MinTimestamp: segMinTs,
            MaxTimestamp: segMaxTs,
            IndexOffset: indexOffset,
            FooterOffset: footerOffset,
            DurationMicros: durationMicros);
    }

    // ── 私有辅助 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 把已编码的时间戳载荷连同字段名/值载荷写入流，并构造对应的 BlockHeader 与 BlockIndexEntry。
    /// </summary>
    private void WriteOneBlock(
        Stream bs,
        MemTableSeries bucket,
        ReadOnlyMemory<TSLite.Model.DataPoint> points,
        ReadOnlySpan<byte> fieldNameSpan,
        ReadOnlySpan<byte> tsSpan,
        BlockEncoding tsEncoding,
        long blockOffset,
        List<BlockIndexEntry> indexEntries,
        ref long segMinTs,
        ref long segMaxTs,
        ref long currentOffset)
    {
        bool useV2Val = (_options.ValueEncoding & BlockEncoding.DeltaValue) != 0
            && points.Length > 0;
        int valPayloadLen = useV2Val
            ? ValuePayloadCodecV2.Measure(bucket.FieldType, points)
            : ValuePayloadCodec.MeasureValuePayload(bucket.FieldType, points);
        byte[] valBuf = ArrayPool<byte>.Shared.Rent(Math.Max(valPayloadLen, 1));
        try
        {
            if (valPayloadLen > 0)
            {
                if (useV2Val)
                    ValuePayloadCodecV2.Write(bucket.FieldType, points, valBuf.AsSpan(0, valPayloadLen));
                else
                    ValuePayloadCodec.WritePayload(bucket.FieldType, points, valBuf.AsSpan(0, valPayloadLen));
            }

            ReadOnlySpan<byte> valSpan = valBuf.AsSpan(0, valPayloadLen);

            // CRC32(FieldNameUtf8 ++ TsPayload ++ ValPayload)
            var blockCrc = new Crc32();
            blockCrc.Append(fieldNameSpan);
            blockCrc.Append(tsSpan);
            blockCrc.Append(valSpan);
            uint crc32 = blockCrc.GetCurrentHashAsUInt32();

            int fieldNameHash = FieldNameHash.Compute(fieldNameSpan);

            int blockLength = FormatSizes.BlockHeaderSize + fieldNameSpan.Length + tsSpan.Length + valSpan.Length;
            var bh = BlockHeader.CreateNew(
                seriesId: bucket.Key.SeriesId,
                min: bucket.MinTimestamp,
                max: bucket.MaxTimestamp,
                count: points.Length,
                fieldType: bucket.FieldType,
                fieldNameLen: fieldNameSpan.Length,
                tsLen: tsSpan.Length,
                valLen: valSpan.Length);
            bh.Crc32 = crc32;
            bh.Encoding = tsEncoding | (useV2Val ? BlockEncoding.DeltaValue : BlockEncoding.None);
            if (TryBuildAggregateMetadata(bucket.FieldType, points.Span, out var aggregateSum, out var aggregateMin, out var aggregateMax))
            {
                bh.AggregateFlags = 1;
                bh.AggregateSum = aggregateSum;
                bh.AggregateMinBits = aggregateMin;
                bh.AggregateMaxBits = aggregateMax;
            }

            WriteStructToStream(bs, in bh);
            bs.Write(fieldNameSpan);
            bs.Write(tsSpan);
            bs.Write(valSpan);

            if (bucket.MinTimestamp < segMinTs) segMinTs = bucket.MinTimestamp;
            if (bucket.MaxTimestamp > segMaxTs) segMaxTs = bucket.MaxTimestamp;

            indexEntries.Add(new BlockIndexEntry
            {
                SeriesId = bucket.Key.SeriesId,
                MinTimestamp = bucket.MinTimestamp,
                MaxTimestamp = bucket.MaxTimestamp,
                FileOffset = blockOffset,
                BlockLength = blockLength,
                FieldNameHash = fieldNameHash,
            });

            currentOffset += blockLength;
        }
        finally { ArrayPool<byte>.Shared.Return(valBuf); }
    }

    private static bool TryBuildAggregateMetadata(
        FieldType fieldType,
        ReadOnlySpan<DataPoint> points,
        out double sum,
        out int minBits,
        out int maxBits)
    {
        sum = 0;
        minBits = 0;
        maxBits = 0;

        if (points.IsEmpty)
            return false;

        switch (fieldType)
        {
            case FieldType.Float64:
            {
                double min = double.PositiveInfinity;
                double max = double.NegativeInfinity;
                for (int i = 0; i < points.Length; i++)
                {
                    double value = points[i].Value.AsDouble();
                    sum += value;
                    if (value < min) min = value;
                    if (value > max) max = value;
                }

                minBits = BitConverter.SingleToInt32Bits((float)min);
                maxBits = BitConverter.SingleToInt32Bits((float)max);
                return true;
            }

            case FieldType.Int64:
            {
                long min = long.MaxValue;
                long max = long.MinValue;
                for (int i = 0; i < points.Length; i++)
                {
                    long value = points[i].Value.AsLong();
                    sum += value;
                    if (value < min) min = value;
                    if (value > max) max = value;
                }

                if (min < int.MinValue || min > int.MaxValue || max < int.MinValue || max > int.MaxValue)
                    return false;

                minBits = (int)min;
                maxBits = (int)max;
                return true;
            }

            case FieldType.Boolean:
            {
                int min = 1;
                int max = 0;
                for (int i = 0; i < points.Length; i++)
                {
                    int value = points[i].Value.AsBool() ? 1 : 0;
                    sum += value;
                    if (value < min) min = value;
                    if (value > max) max = value;
                }

                minBits = min;
                maxBits = max;
                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>将 unmanaged 结构体序列化写入流。</summary>
    private static void WriteStructToStream<T>(Stream stream, in T value) where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        byte[] buf = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            MemoryMarshal.Write(buf.AsSpan(0, size), in value);
            stream.Write(buf, 0, size);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    /// <summary>将 unmanaged 结构体序列化写入流，同时追加到 CRC32 计算器。</summary>
    private static void WriteStructToStreamAndHash<T>(Stream stream, in T value, Crc32 crc) where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        byte[] buf = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            MemoryMarshal.Write(buf.AsSpan(0, size), in value);
            stream.Write(buf, 0, size);
            crc.Append(buf.AsSpan(0, size));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }
}
