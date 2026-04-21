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
/// 42      2     AggregateFlags
/// 44      8     AggregateSum
/// 52      4     AggregateMinBits
/// 56      4     AggregateMaxBits
/// 60      4     Crc32
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

    /// <summary>
    /// 聚合元数据标记（按位组合）。
    /// <list type="bullet">
    ///   <item><c>0x01</c> <see cref="HasSumCount"/>: <see cref="AggregateSum"/> 已写入且对 Sum/Count/Avg 精度足够。</item>
    ///   <item><c>0x02</c> <see cref="HasMinMax"/>: <see cref="AggregateMinBits"/> / <see cref="AggregateMaxBits"/> 为无损值，可用于 Min/Max。</item>
    /// </list>
    /// 老版本（仅 0/1）的 <c>1</c> 等同于 <see cref="HasSumCount"/>，min/max 精度被视为不可信。
    /// </summary>
    public short AggregateFlags;

    /// <summary>数值聚合的 Sum（统一按 double 持久化）。</summary>
    public double AggregateSum;

    /// <summary>
    /// 数值聚合的 Min 位模式（仅当 <see cref="HasMinMax"/> 置位时有效）。
    /// Float64 字段使用 <see cref="BitConverter.SingleToInt32Bits(float)"/>（写入侧已校验无精度损失），
    /// Int64/Boolean 直接为 int32 数值。
    /// </summary>
    public int AggregateMinBits;

    /// <summary>数值聚合的 Max 位模式（编码方式同 <see cref="AggregateMinBits"/>）。</summary>
    public int AggregateMaxBits;

    /// <summary><see cref="AggregateFlags"/> 的标记位：sum/count 已写入。</summary>
    public const short HasSumCount = 0x01;

    /// <summary><see cref="AggregateFlags"/> 的标记位：min/max 已无损写入。</summary>
    public const short HasMinMax = 0x02;

    /// <summary>块数据 CRC32 校验值（CRC32(FieldNameUtf8 ++ TimestampPayload ++ ValuePayload)）。</summary>
    public uint Crc32;

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
