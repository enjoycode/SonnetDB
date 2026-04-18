using System.Runtime.InteropServices;
using TSLite.Buffers;

namespace TSLite.Storage.Format;

/// <summary>
/// TSLite 段文件中每个 Block 的头部（固定 64 字节）。
/// <para>
/// 一个 Block 由 BlockHeader + FieldNameUtf8 + TimestampPayload + ValuePayload 组成。
/// </para>
/// <para>
/// 二进制布局（little-endian）：
/// <code>
/// Offset  Size  Field
/// 0       8     SeriesId                (序列唯一 ID)
/// 8       8     MinTimestamp            (本 Block 最小时间戳，毫秒 UTC)
/// 16      8     MaxTimestamp            (本 Block 最大时间戳，毫秒 UTC)
/// 24      4     Count                   (数据点数量)
/// 28      4     TimestampPayloadLength  (时间戳载荷字节数)
/// 32      4     ValuePayloadLength      (值载荷字节数)
/// 36      4     FieldNameUtf8Length     (字段名 UTF-8 字节数)
/// 40      1     Encoding                (<see cref="BlockEncoding"/>)
/// 41      1     FieldType               (<see cref="Format.FieldType"/>)
/// 42      2     Reserved0
/// 44      16    Reserved16
/// 60      4     Reserved4
/// ─────────────────────────────────
/// Total  64
/// </code>
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BlockHeader
{
    /// <summary>序列唯一 ID（由 SeriesKey 哈希得到）。</summary>
    public ulong SeriesId;

    /// <summary>本 Block 内最小时间戳（毫秒 UTC）。</summary>
    public long MinTimestamp;

    /// <summary>本 Block 内最大时间戳（毫秒 UTC）。</summary>
    public long MaxTimestamp;

    /// <summary>本 Block 包含的数据点数量。</summary>
    public int Count;

    /// <summary>时间戳载荷字节数（跟在 FieldNameUtf8 之后）。</summary>
    public int TimestampPayloadLength;

    /// <summary>值载荷字节数（跟在 TimestampPayload 之后）。</summary>
    public int ValuePayloadLength;

    /// <summary>字段名 UTF-8 编码的字节数（跟在 BlockHeader 之后）。</summary>
    public int FieldNameUtf8Length;

    /// <summary>数据编码方式（见 <see cref="BlockEncoding"/>）。</summary>
    public BlockEncoding Encoding;

    /// <summary>字段数据类型（见 <see cref="Format.FieldType"/>）。</summary>
    public FieldType FieldType;

    /// <summary>保留字段（0）。</summary>
    public short Reserved0;

    /// <summary>保留字节（全 0）。</summary>
    public InlineBytes16 Reserved16;

    /// <summary>保留字节（全 0）。</summary>
    public InlineBytes4 Reserved4;

    /// <summary>
    /// 创建一个新的 <see cref="BlockHeader"/>，填写所有关键字段。
    /// </summary>
    /// <param name="seriesId">序列唯一 ID。</param>
    /// <param name="min">本 Block 最小时间戳（毫秒 UTC）。</param>
    /// <param name="max">本 Block 最大时间戳（毫秒 UTC）。</param>
    /// <param name="count">数据点数量。</param>
    /// <param name="fieldType">字段数据类型。</param>
    /// <param name="fieldNameLen">字段名 UTF-8 字节数。</param>
    /// <param name="tsLen">时间戳载荷字节数。</param>
    /// <param name="valLen">值载荷字节数。</param>
    /// <returns>已初始化的 <see cref="BlockHeader"/> 实例。</returns>
    public static BlockHeader CreateNew(
        ulong seriesId,
        long min,
        long max,
        int count,
        FieldType fieldType,
        int fieldNameLen,
        int tsLen,
        int valLen)
    {
        BlockHeader h = default;
        h.SeriesId = seriesId;
        h.MinTimestamp = min;
        h.MaxTimestamp = max;
        h.Count = count;
        h.FieldType = fieldType;
        h.FieldNameUtf8Length = fieldNameLen;
        h.TimestampPayloadLength = tsLen;
        h.ValuePayloadLength = valLen;
        h.Encoding = BlockEncoding.None;
        return h;
    }
}
