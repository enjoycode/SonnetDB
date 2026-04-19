using System.Buffers;
using System.IO.Hashing;
using TSLite.IO;
using TSLite.Model;
using TSLite.Storage.Format;

namespace TSLite.Wal;

/// <summary>
/// WAL（Write-Ahead Log）写入器，支持 append-only 写入，含 CRC32 校验与 fsync 持久化。
/// </summary>
/// <remarks>
/// 写入流程：为每条记录计算 payload → 计算 CRC32 → 写 WalRecordHeader → 写 Payload。
/// 调用 <see cref="Sync"/> 或 <see cref="Dispose"/> 可确保数据落盘。
/// </remarks>
public sealed class WalWriter : IDisposable
{
    private FileStream? _fileStream;
    private BufferedStream? _stream;
    private long _nextLsn;
    private long _bytesWritten;
    private bool _disposed;

    /// <summary>WAL 文件的完整路径。</summary>
    public string Path { get; }

    /// <summary>下一个将分配的 LSN（日志序列号）。</summary>
    public long NextLsn => _nextLsn;

    /// <summary>累计写入的字节数（包括文件头和所有记录）。</summary>
    public long BytesWritten => _bytesWritten;

    /// <summary>写入器是否处于打开状态。</summary>
    public bool IsOpen => !_disposed;

    private WalWriter(string path, FileStream fileStream, BufferedStream stream, long nextLsn, long bytesWritten)
    {
        Path = path;
        _fileStream = fileStream;
        _stream = stream;
        _nextLsn = nextLsn;
        _bytesWritten = bytesWritten;
    }

    /// <summary>
    /// 打开（或创建）一个 WAL 文件用于追加写入。
    /// </summary>
    /// <param name="path">WAL 文件路径（扩展名通常为 .tslwal）。</param>
    /// <param name="startLsn">新文件时的起始 LSN（默认为 1）。</param>
    /// <param name="bufferSize">写缓冲区大小（默认 64KB）。</param>
    /// <returns>已初始化的 <see cref="WalWriter"/> 实例。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> 为 null。</exception>
    /// <exception cref="InvalidDataException">文件存在但 magic 或版本不合法时抛出。</exception>
    public static WalWriter Open(string path, long startLsn = 1, int bufferSize = 64 * 1024)
    {
        ArgumentNullException.ThrowIfNull(path);

        bool fileExists = File.Exists(path) && new FileInfo(path).Length > 0;
        long nextLsn = startLsn;
        long bytesWritten = 0L;

        var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        try
        {
            if (fileExists && fs.Length >= FormatSizes.WalFileHeaderSize)
            {
                // Read existing file header to validate
                byte[] headerBuf = ArrayPool<byte>.Shared.Rent(FormatSizes.WalFileHeaderSize);
                try
                {
                    fs.Position = 0;
                    ReadExact(fs, headerBuf, 0, FormatSizes.WalFileHeaderSize);
                    var reader = new SpanReader(headerBuf.AsSpan(0, FormatSizes.WalFileHeaderSize));
                    var fileHeader = reader.ReadStruct<WalFileHeader>();

                    if (!fileHeader.IsValid())
                        throw new InvalidDataException("WAL file header is invalid: magic or version mismatch.");

                    // Scan existing records to determine next LSN
                    fs.Position = FormatSizes.WalFileHeaderSize;
                    bytesWritten = FormatSizes.WalFileHeaderSize;
                    long lastLsn = ScanForLastLsn(fs, fileHeader.FirstLsn - 1, ref bytesWritten);
                    nextLsn = lastLsn + 1;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(headerBuf);
                }
            }
            else if (!fileExists || fs.Length == 0)
            {
                // New file: write header
                fs.Position = 0;
                WriteFileHeader(fs, startLsn);
                bytesWritten = FormatSizes.WalFileHeaderSize;
                nextLsn = startLsn;
            }
            else
            {
                throw new InvalidDataException("WAL file is truncated: header is incomplete.");
            }

            // Seek to end for appending
            fs.Position = fs.Length;
            var bs = new BufferedStream(fs, bufferSize);
            return new WalWriter(path, fs, bs, nextLsn, bytesWritten);
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 追加一条 WritePoint 记录，返回分配的 LSN。
    /// </summary>
    /// <param name="seriesId">序列唯一标识。</param>
    /// <param name="pointTimestamp">数据点时间戳（Unix 毫秒）。</param>
    /// <param name="fieldName">字段名称。</param>
    /// <param name="value">字段值。</param>
    /// <returns>分配的 LSN。</returns>
    /// <exception cref="ObjectDisposedException">写入器已关闭时抛出。</exception>
    public long AppendWritePoint(ulong seriesId, long pointTimestamp, string fieldName, FieldValue value)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(fieldName);

        int payloadSize = WalPayloadCodec.MeasureWritePoint(fieldName, value);
        return AppendRecord(WalRecordType.WritePoint, payloadSize, (ref SpanWriter w) =>
            WalPayloadCodec.WriteWritePointPayload(ref w, seriesId, pointTimestamp, fieldName, value));
    }

    /// <summary>
    /// 追加一条 CreateSeries 记录，返回分配的 LSN。
    /// </summary>
    /// <param name="seriesId">序列唯一标识。</param>
    /// <param name="measurement">Measurement 名称。</param>
    /// <param name="tags">Tag 键值对。</param>
    /// <returns>分配的 LSN。</returns>
    /// <exception cref="ObjectDisposedException">写入器已关闭时抛出。</exception>
    public long AppendCreateSeries(ulong seriesId, string measurement, IReadOnlyDictionary<string, string> tags)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(measurement);
        ArgumentNullException.ThrowIfNull(tags);

        int payloadSize = WalPayloadCodec.MeasureCreateSeries(measurement, tags);
        return AppendRecord(WalRecordType.CreateSeries, payloadSize, (ref SpanWriter w) =>
            WalPayloadCodec.WriteCreateSeriesPayload(ref w, seriesId, measurement, tags));
    }

    /// <summary>
    /// 追加一条 Checkpoint 记录，返回分配的 LSN。
    /// </summary>
    /// <param name="checkpointLsn">检查点 LSN（截止该 LSN 的数据已落盘）。</param>
    /// <returns>分配的 LSN。</returns>
    /// <exception cref="ObjectDisposedException">写入器已关闭时抛出。</exception>
    public long AppendCheckpoint(long checkpointLsn)
    {
        ThrowIfDisposed();
        return AppendRecord(WalRecordType.Checkpoint, 8, (ref SpanWriter w) =>
            WalPayloadCodec.WriteCheckpointPayload(ref w, checkpointLsn));
    }

    /// <summary>
    /// 追加一条 Delete 记录，返回分配的 LSN。
    /// </summary>
    /// <param name="seriesId">序列唯一标识。</param>
    /// <param name="fieldName">字段名称。</param>
    /// <param name="fromTimestamp">删除时间窗起始时间戳（Unix 毫秒，闭区间）。</param>
    /// <param name="toTimestamp">删除时间窗结束时间戳（Unix 毫秒，闭区间）。</param>
    /// <returns>分配的 LSN。</returns>
    /// <exception cref="ObjectDisposedException">写入器已关闭时抛出。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="fieldName"/> 为 null 时抛出。</exception>
    public long AppendDelete(ulong seriesId, string fieldName, long fromTimestamp, long toTimestamp)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(fieldName);

        int payloadSize = WalPayloadCodec.MeasureDelete(fieldName);
        return AppendRecord(WalRecordType.Delete, payloadSize, (ref SpanWriter w) =>
            WalPayloadCodec.WriteDeletePayload(ref w, seriesId, fieldName, fromTimestamp, toTimestamp));
    }

    /// <summary>
    /// 把缓冲区刷到 OS（不强制 fsync）。
    /// </summary>
    /// <exception cref="ObjectDisposedException">写入器已关闭时抛出。</exception>
    public void Flush()
    {
        ThrowIfDisposed();
        _stream!.Flush();
    }

    /// <summary>
    /// 强制 fsync，确保数据持久化到磁盘。
    /// </summary>
    /// <exception cref="ObjectDisposedException">写入器已关闭时抛出。</exception>
    public void Sync()
    {
        ThrowIfDisposed();
        _stream!.Flush();
        _fileStream!.Flush(true);
    }

    /// <summary>
    /// 关闭写入器并刷盘（fsync）。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            _stream?.Flush();
            _fileStream?.Flush(true);
        }
        finally
        {
            _stream?.Dispose();
            _fileStream?.Dispose();
            _stream = null;
            _fileStream = null;
        }
    }

    // ── 私有辅助 ─────────────────────────────────────────────────────────────

    private delegate void PayloadWriter(ref SpanWriter writer);

    private long AppendRecord(WalRecordType recordType, int payloadSize, PayloadWriter writePayload)
    {
        long lsn = _nextLsn;
        long now = DateTime.UtcNow.Ticks;

        byte[] payloadBuf = ArrayPool<byte>.Shared.Rent(Math.Max(payloadSize, 1)); // Defensive: Rent(0) is valid, but avoids zero-length array edge cases
        try
        {
            // Write payload
            var payloadWriter = new SpanWriter(payloadBuf.AsSpan(0, payloadSize));
            writePayload(ref payloadWriter);

            // Calculate CRC32
            uint crc32 = Crc32.HashToUInt32(payloadBuf.AsSpan(0, payloadSize));

            // Build and write header
            Span<byte> headerBuf = stackalloc byte[FormatSizes.WalRecordHeaderSize];
            var headerWriter = new SpanWriter(headerBuf);
            var header = WalRecordHeader.CreateNew(recordType, payloadSize, crc32, now, lsn);
            headerWriter.WriteStruct(in header);

            _stream!.Write(headerBuf);
            _stream.Write(payloadBuf, 0, payloadSize);

            int recordSize = FormatSizes.WalRecordHeaderSize + payloadSize;
            _bytesWritten += recordSize;
            _nextLsn++;
            return lsn;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(payloadBuf);
        }
    }

    private static void WriteFileHeader(Stream stream, long firstLsn)
    {
        byte[] buf = ArrayPool<byte>.Shared.Rent(FormatSizes.WalFileHeaderSize);
        try
        {
            var header = WalFileHeader.CreateNew(firstLsn);
            var writer = new SpanWriter(buf.AsSpan(0, FormatSizes.WalFileHeaderSize));
            writer.WriteStruct(in header);
            stream.Write(buf, 0, FormatSizes.WalFileHeaderSize);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    private static long ScanForLastLsn(FileStream fs, long initialLastLsn, ref long bytesWritten)
    {
        long lastLsn = initialLastLsn;
        byte[] headerBuf = ArrayPool<byte>.Shared.Rent(FormatSizes.WalRecordHeaderSize);
        try
        {
            while (true)
            {
                int headerRead = ReadExact(fs, headerBuf, 0, FormatSizes.WalRecordHeaderSize);
                if (headerRead < FormatSizes.WalRecordHeaderSize)
                    break;

                var headerReader = new SpanReader(headerBuf.AsSpan(0, FormatSizes.WalRecordHeaderSize));
                var header = headerReader.ReadStruct<WalRecordHeader>();

                if (!header.IsMagicValid())
                    break;

                if (header.PayloadLength < 0)
                    break;

                // Skip payload
                long remaining = header.PayloadLength;
                bool truncated = false;
                while (remaining > 0)
                {
                    byte[] skipBuf = ArrayPool<byte>.Shared.Rent((int)Math.Min(remaining, 4096));
                    try
                    {
                        int toRead = (int)Math.Min(remaining, skipBuf.Length);
                        int read = ReadExact(fs, skipBuf, 0, toRead);
                        if (read < toRead)
                        {
                            truncated = true;
                            break;
                        }
                        remaining -= read;
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(skipBuf);
                    }
                }

                if (truncated)
                    break;

                lastLsn = header.Lsn;
                bytesWritten += FormatSizes.WalRecordHeaderSize + header.PayloadLength;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuf);
        }
        return lastLsn;
    }

    private static int ReadExact(Stream stream, byte[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = stream.Read(buffer, offset + total, count - total);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WalWriter));
    }
}
