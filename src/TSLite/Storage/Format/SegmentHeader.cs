using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TSLite.Buffers;

namespace TSLite.Storage.Format;

/// <summary>
/// TSLite 段文件头（固定 64 字节）。
/// <para>
/// 二进制布局（little-endian）：
/// <code>
/// Offset  Size  Field
/// 0       8     Magic              ("TSLSEGv1")
/// 8       4     FormatVersion      (当前 = 1)
/// 12      4     HeaderSize         (= 64)
/// 16      8     SegmentId          (段唯一标识，单调递增)
/// 24      8     CreatedAtUtcTicks
/// 32      4     BlockCount         (预留，写入时填 0，Flush 后更新)
/// 36      4     Reserved0
/// 40      16    Reserved16
/// 56      8     Reserved8
/// ─────────────────────────────────
/// Total  64
/// </code>
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SegmentHeader
{
    /// <summary>段文件 magic（"TSLSEGv1"，8 字节）。</summary>
    public InlineBytes8 Magic;

    /// <summary>文件格式版本号（当前 = <see cref="TsdbMagic.FormatVersion"/>）。</summary>
    public int FormatVersion;

    /// <summary>本头部的字节大小（= 64）。</summary>
    public int HeaderSize;

    /// <summary>段唯一标识（单调递增 ID）。</summary>
    public long SegmentId;

    /// <summary>段文件创建时间（UTC Ticks）。</summary>
    public long CreatedAtUtcTicks;

    /// <summary>本段包含的 Block 数量（预留，Flush 后回填）。</summary>
    public int BlockCount;

    /// <summary>保留字段（0）。</summary>
    public int Reserved0;

    /// <summary>保留字节（全 0）。</summary>
    public InlineBytes16 Reserved16;

    /// <summary>保留字节（全 0）。</summary>
    public InlineBytes8 Reserved8;

    /// <summary>
    /// 创建一个新的 <see cref="SegmentHeader"/>，自动填写 magic、版本号、当前 UTC 时间。
    /// </summary>
    /// <param name="segmentId">段唯一标识符。</param>
    /// <returns>已初始化的 <see cref="SegmentHeader"/> 实例。</returns>
    public static SegmentHeader CreateNew(long segmentId)
    {
        SegmentHeader h = default;
        TsdbMagic.Segment.CopyTo(h.Magic.AsSpan());
        h.FormatVersion = TsdbMagic.FormatVersion;
        h.HeaderSize = Unsafe.SizeOf<SegmentHeader>();
        h.SegmentId = segmentId;
        h.CreatedAtUtcTicks = DateTime.UtcNow.Ticks;
        return h;
    }

    /// <summary>
    /// 校验段文件头是否有效（magic 一致且版本匹配）。
    /// </summary>
    /// <returns>有效返回 <c>true</c>，否则返回 <c>false</c>。</returns>
    public readonly bool IsValid() =>
        Magic.AsReadOnlySpan().SequenceEqual(TsdbMagic.Segment) &&
        FormatVersion == TsdbMagic.FormatVersion;
}
