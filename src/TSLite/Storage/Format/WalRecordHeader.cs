using System.Runtime.InteropServices;

namespace TSLite.Storage.Format;

/// <summary>
/// TSLite WAL 日志中每条记录的头部（固定 32 字节）。
/// <para>
/// WAL 文件布局：每条记录 = WalRecordHeader + Payload（PayloadLength 字节）。
/// </para>
/// <para>
/// 二进制布局（little-endian）：
/// <code>
/// Offset  Size  Field
/// 0       1     RecordType      (<see cref="WalRecordType"/>)
/// 1       1     Reserved0
/// 2       2     Reserved1
/// 4       4     PayloadLength   (载荷字节数，不含本头部)
/// 8       8     SeriesId        (序列唯一 ID)
/// 16      8     Timestamp       (时间戳，毫秒 UTC)
/// 24      4     Crc32           (预留，Milestone 3 填写 CRC32)
/// 28      4     Reserved4
/// ─────────────────────────────────
/// Total  32
/// </code>
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WalRecordHeader
{
    /// <summary>WAL 记录类型（见 <see cref="WalRecordType"/>）。</summary>
    public WalRecordType RecordType;

    /// <summary>保留字节（0）。</summary>
    public byte Reserved0;

    /// <summary>保留字段（0）。</summary>
    public ushort Reserved1;

    /// <summary>载荷字节数（不含本头部自身）。</summary>
    public int PayloadLength;

    /// <summary>序列唯一 ID（与写入的序列对应）。</summary>
    public ulong SeriesId;

    /// <summary>数据点时间戳（毫秒 UTC）。</summary>
    public long Timestamp;

    /// <summary>CRC32 校验值（预留，Milestone 3 中填写，当前填 0）。</summary>
    public uint Crc32;

    /// <summary>保留字段（0）。</summary>
    public int Reserved4;

    /// <summary>
    /// 创建一个新的 <see cref="WalRecordHeader"/>，填写关键字段。
    /// </summary>
    /// <param name="recordType">记录类型。</param>
    /// <param name="seriesId">序列唯一 ID。</param>
    /// <param name="timestamp">时间戳（毫秒 UTC）。</param>
    /// <param name="payloadLength">载荷字节数。</param>
    /// <returns>已初始化的 <see cref="WalRecordHeader"/> 实例。</returns>
    public static WalRecordHeader CreateNew(
        WalRecordType recordType,
        ulong seriesId,
        long timestamp,
        int payloadLength)
    {
        WalRecordHeader h = default;
        h.RecordType = recordType;
        h.SeriesId = seriesId;
        h.Timestamp = timestamp;
        h.PayloadLength = payloadLength;
        return h;
    }
}
