using TSLite.Storage.Format;

namespace TSLite.Storage.Segments;

/// <summary>
/// 对外只读地描述段文件内一个 Block 的元数据与物理位置。
/// 不承诺解析 payload；实际 payload 数据通过 <see cref="SegmentReader.ReadBlock"/> 按需获取。
/// </summary>
public readonly struct BlockDescriptor
{
    /// <summary>在段文件内的序号（[0, BlockCount)）。</summary>
    public int Index { get; init; }

    /// <summary>所属序列的唯一 ID（XxHash64 值）。</summary>
    public ulong SeriesId { get; init; }

    /// <summary>本 Block 内最小时间戳（毫秒 UTC）。</summary>
    public long MinTimestamp { get; init; }

    /// <summary>本 Block 内最大时间戳（毫秒 UTC）。</summary>
    public long MaxTimestamp { get; init; }

    /// <summary>本 Block 包含的数据点数量。</summary>
    public int Count { get; init; }

    /// <summary>字段数据类型。</summary>
    public FieldType FieldType { get; init; }

    /// <summary>时间戳载荷的编码方式。</summary>
    public BlockEncoding TimestampEncoding { get; init; }

    /// <summary>值载荷的编码方式。</summary>
    public BlockEncoding ValueEncoding { get; init; }

    /// <summary>已解码的 UTF-8 字段名称。</summary>
    public string FieldName { get; init; }

    /// <summary>BlockHeader 在文件中的字节偏移（文件起始 = 0）。</summary>
    public long FileOffset { get; init; }

    /// <summary>Block 总字节数（BlockHeader + FieldNameUtf8 + TsPayload + ValPayload）。</summary>
    public int BlockLength { get; init; }

    /// <summary>BlockHeader 中记录的 CRC32 校验值。</summary>
    public uint Crc32 { get; init; }

    /// <summary>字段名 UTF-8 编码的字节数（跟在 BlockHeader 之后）。</summary>
    internal int FieldNameUtf8Length { get; init; }

    /// <summary>时间戳载荷字节数（跟在 FieldNameUtf8 之后）。</summary>
    internal int TimestampPayloadLength { get; init; }

    /// <summary>值载荷字节数（跟在 TimestampPayload 之后）。</summary>
    internal int ValuePayloadLength { get; init; }
}
