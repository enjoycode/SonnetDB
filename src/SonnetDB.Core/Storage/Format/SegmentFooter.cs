using System.Runtime.InteropServices;
using SonnetDB.Buffers;

namespace SonnetDB.Storage.Format;

/// <summary>
/// SonnetDB 段文件的尾部（固定 64 字节，位于文件末尾）。
/// <para>
/// 段文件整体布局：
/// <code>
/// [SegmentHeader 64B]
/// [Block 0 ... Block N-1]
/// [BlockIndexEntry 0 ... N-1  (每项 48B，共 IndexCount 项)]
/// [SegmentFooter  64B]
/// </code>
/// 文件长度满足：<c>IndexOffset + IndexCount * 48 + 64 == FileLength</c>。
/// </para>
/// <para>
/// 二进制布局（little-endian）：
/// <code>
/// Offset  Size  Field
/// 0       8     Magic              ("SDBSEGv1")
/// 8       4     FormatVersion      (当前 = 1)
/// 12      4     IndexCount         (BlockIndexEntry 条目数)
/// 16      8     IndexOffset        (索引数组在文件中的偏移)
/// 24      8     FileLength         (段文件总字节数)
/// 32      4     Crc32              (预留，Milestone 3 填写 CRC32)
/// 36      4     Reserved0
/// 40      16    Reserved16
/// 56      8     Reserved8
/// ─────────────────────────────────
/// Total  64
/// </code>
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SegmentFooter
{
    /// <summary>段文件 magic（"SDBSEGv1"，8 字节）。</summary>
    public InlineBytes8 Magic;

    /// <summary>段文件格式版本号（当前 = <see cref="TsdbMagic.SegmentFormatVersion"/>）。</summary>
    public int FormatVersion;

    /// <summary>BlockIndexEntry 条目数量。</summary>
    public int IndexCount;

    /// <summary>BlockIndexEntry 数组在段文件中的字节偏移。</summary>
    public long IndexOffset;

    /// <summary>段文件总字节数（= <see cref="IndexOffset"/> + <see cref="IndexCount"/> * 48 + 64）。</summary>
    public long FileLength;

    /// <summary>CRC32 校验值（预留，Milestone 3 中填写，当前填 0）。</summary>
    public uint Crc32;

    /// <summary>保留字段（0）。</summary>
    public int Reserved0;

    /// <summary>保留字节（全 0）。</summary>
    public InlineBytes16 Reserved16;

    /// <summary>保留字节（全 0）。</summary>
    public InlineBytes8 Reserved8;

    /// <summary>
    /// 创建一个新的 <see cref="SegmentFooter"/>，填写 magic、版本号及索引信息。
    /// </summary>
    /// <param name="indexCount">BlockIndexEntry 条目数量。</param>
    /// <param name="indexOffset">索引数组在文件中的偏移。</param>
    /// <param name="fileLength">段文件总字节数。</param>
    /// <returns>已初始化的 <see cref="SegmentFooter"/> 实例。</returns>
    public static SegmentFooter CreateNew(int indexCount, long indexOffset, long fileLength)
    {
        SegmentFooter f = default;
        TsdbMagic.Segment.CopyTo(f.Magic.AsSpan());
        f.FormatVersion = TsdbMagic.SegmentFormatVersion;
        f.IndexCount = indexCount;
        f.IndexOffset = indexOffset;
        f.FileLength = fileLength;
        return f;
    }

    /// <summary>
    /// 校验段文件尾部是否有效（magic 一致且版本匹配当前写入版本）。
    /// </summary>
    /// <returns>有效返回 <c>true</c>，否则返回 <c>false</c>。</returns>
    public readonly bool IsValid() =>
        Magic.AsReadOnlySpan().SequenceEqual(TsdbMagic.Segment) &&
        FormatVersion == TsdbMagic.SegmentFormatVersion;

    /// <summary>
    /// 读取兼容性校验（magic 一致且版本属于
    /// <see cref="TsdbMagic.SupportedSegmentFormatVersions"/>）。
    /// </summary>
    /// <returns>段文件可被当前 SonnetDB 读取时返回 <c>true</c>。</returns>
    public readonly bool IsCompatibleForRead()
    {
        if (!Magic.AsReadOnlySpan().SequenceEqual(TsdbMagic.Segment))
            return false;
        ReadOnlySpan<int> supported = TsdbMagic.SupportedSegmentFormatVersions;
        for (int i = 0; i < supported.Length; i++)
        {
            if (supported[i] == FormatVersion)
                return true;
        }
        return false;
    }
}
