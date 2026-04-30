using System.Buffers.Binary;

namespace SonnetDB.Storage.Segments;

/// <summary>
/// `.SDBVIDX` 向量索引 sidecar 文件的读写工具。
/// </summary>
internal static class SegmentVectorIndexFile
{
    private static readonly byte[] Magic = "SDBVIDX1"u8.ToArray();
    private const int FormatVersion = 1;
    private const int HeaderSize = 32;

    /// <summary>
    /// 把多个 block 的 HNSW 索引写入 sidecar 文件。
    /// </summary>
    /// <param name="path">目标 sidecar 文件路径。</param>
    /// <param name="blocks">待写入的 block 索引集合。</param>
    public static void Write(string path, IReadOnlyList<HnswVectorBlockIndex> blocks)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(blocks);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        WriteHeader(fs, blocks.Count);
        foreach (var block in blocks)
            block.WriteTo(fs);
        fs.Flush(true);
    }

    /// <summary>
    /// 尝试读取指定段文件对应的 sidecar 文件；若文件不存在或损坏，则返回空字典。
    /// </summary>
    /// <param name="segmentPath">段文件路径。</param>
    /// <param name="descriptors">段内 block 描述符。</param>
    /// <returns>按 block index 建立的 HNSW 索引字典。</returns>
    public static IReadOnlyDictionary<int, HnswVectorBlockIndex> TryLoad(
        string segmentPath,
        IReadOnlyList<BlockDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(segmentPath);
        ArgumentNullException.ThrowIfNull(descriptors);

        string path = Engine.TsdbPaths.VectorIndexPathForSegment(segmentPath);
        if (!File.Exists(path))
            return new Dictionary<int, HnswVectorBlockIndex>();

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            int blockCount = ReadHeader(fs);
            var result = new Dictionary<int, HnswVectorBlockIndex>(blockCount);
            for (int i = 0; i < blockCount; i++)
            {
                var index = HnswVectorBlockIndex.ReadFrom(fs);
                ValidateBlockIndex(index.BlockIndex, descriptors);
                result[index.BlockIndex] = index;
            }

            return result;
        }
        catch
        {
            return new Dictionary<int, HnswVectorBlockIndex>();
        }
    }

    /// <summary>
    /// 尝试读取 sidecar 中各 block 索引的文件偏移；不反序列化 HNSW 图。
    /// </summary>
    /// <param name="segmentPath">段文件路径。</param>
    /// <param name="descriptors">段内 block 描述符。</param>
    /// <returns>按 block index 建立的 sidecar 偏移表。</returns>
    public static IReadOnlyDictionary<int, long> TryLoadOffsets(
        string segmentPath,
        IReadOnlyList<BlockDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(segmentPath);
        ArgumentNullException.ThrowIfNull(descriptors);

        string path = Engine.TsdbPaths.VectorIndexPathForSegment(segmentPath);
        if (!File.Exists(path))
            return new Dictionary<int, long>();

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            int blockCount = ReadHeader(fs);
            var result = new Dictionary<int, long>(blockCount);
            for (int i = 0; i < blockCount; i++)
            {
                long offset = fs.Position;
                int blockIndex = HnswVectorBlockIndex.ReadBlockIndexAndSkip(fs);
                ValidateBlockIndex(blockIndex, descriptors);
                result[blockIndex] = offset;
            }

            return result;
        }
        catch
        {
            return new Dictionary<int, long>();
        }
    }

    /// <summary>
    /// 尝试从指定 sidecar 偏移读取单个 block 的 HNSW 索引。
    /// </summary>
    /// <param name="segmentPath">段文件路径。</param>
    /// <param name="offset">sidecar 内索引起始偏移。</param>
    /// <param name="descriptors">段内 block 描述符。</param>
    /// <param name="targetBlockIndex">目标 block index。</param>
    /// <param name="index">读取成功时返回的 HNSW 索引。</param>
    /// <returns>读取成功返回 true，否则返回 false。</returns>
    public static bool TryLoadBlockAt(
        string segmentPath,
        long offset,
        IReadOnlyList<BlockDescriptor> descriptors,
        int targetBlockIndex,
        out HnswVectorBlockIndex index)
    {
        ArgumentNullException.ThrowIfNull(segmentPath);
        ArgumentNullException.ThrowIfNull(descriptors);

        index = null!;
        if (targetBlockIndex < 0 || targetBlockIndex >= descriptors.Count)
            return false;
        if (descriptors[targetBlockIndex].FieldType != Storage.Format.FieldType.Vector)
            return false;
        if (offset < HeaderSize)
            return false;

        string path = Engine.TsdbPaths.VectorIndexPathForSegment(segmentPath);
        if (!File.Exists(path))
            return false;

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _ = ReadHeader(fs);
            if (offset >= fs.Length)
                return false;

            fs.Seek(offset, SeekOrigin.Begin);
            var loaded = HnswVectorBlockIndex.ReadFrom(fs);
            if (loaded.BlockIndex != targetBlockIndex)
                return false;
            ValidateBlockIndex(loaded.BlockIndex, descriptors);

            index = loaded;
            return true;
        }
        catch
        {
            index = null!;
            return false;
        }
    }

    private static void WriteHeader(Stream stream, int blockCount)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        header.Clear();
        Magic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..12], FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..16], HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..20], blockCount);
        stream.Write(header);
    }

    private static int ReadHeader(Stream stream)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        FillBuffer(stream, header);
        if (!header[..8].SequenceEqual(Magic))
            throw new InvalidDataException("SDBVIDX magic 不匹配。");

        int version = BinaryPrimitives.ReadInt32LittleEndian(header[8..12]);
        if (version != FormatVersion)
            throw new InvalidDataException($"SDBVIDX 版本不支持：{version}。");

        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(header[12..16]);
        if (headerSize != HeaderSize)
            throw new InvalidDataException($"SDBVIDX HeaderSize={headerSize} 非法。");

        int blockCount = BinaryPrimitives.ReadInt32LittleEndian(header[16..20]);
        if (blockCount < 0)
            throw new InvalidDataException("SDBVIDX blockCount 不能为负。");

        return blockCount;
    }

    private static void ValidateBlockIndex(int blockIndex, IReadOnlyList<BlockDescriptor> descriptors)
    {
        if (blockIndex < 0 || blockIndex >= descriptors.Count)
            throw new InvalidDataException("SDBVIDX 中的 blockIndex 越界。");
        if (descriptors[blockIndex].FieldType != Storage.Format.FieldType.Vector)
            throw new InvalidDataException("SDBVIDX 指向了非 VECTOR block。");
    }

    private static void FillBuffer(Stream stream, Span<byte> buffer)
    {
        int readTotal = 0;
        while (readTotal < buffer.Length)
        {
            int read = stream.Read(buffer[readTotal..]);
            if (read == 0)
                throw new InvalidDataException("SDBVIDX 文件截断。");
            readTotal += read;
        }
    }
}
