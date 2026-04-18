namespace TSLite.Storage.Format;

/// <summary>
/// TSLite 所有固定二进制结构体的大小常量。
/// <para>
/// 这些常量与 <c>Unsafe.SizeOf&lt;T&gt;()</c> 严格对应，由单元测试 <c>FormatSizesTests</c> 守护。
/// 修改任何结构体布局时必须同步更新此类，并升级 <see cref="TsdbMagic.FormatVersion"/>。
/// </para>
/// </summary>
public static class FormatSizes
{
    /// <summary>
    /// <see cref="FileHeader"/> 的固定大小（字节）。
    /// </summary>
    public const int FileHeaderSize = 64;

    /// <summary>
    /// <see cref="SegmentHeader"/> 的固定大小（字节）。
    /// </summary>
    public const int SegmentHeaderSize = 64;

    /// <summary>
    /// <see cref="BlockHeader"/> 的固定大小（字节）。
    /// </summary>
    public const int BlockHeaderSize = 64;

    /// <summary>
    /// <see cref="BlockIndexEntry"/> 的固定大小（字节）。
    /// </summary>
    public const int BlockIndexEntrySize = 48;

    /// <summary>
    /// <see cref="SegmentFooter"/> 的固定大小（字节）。
    /// </summary>
    public const int SegmentFooterSize = 64;

    /// <summary>
    /// <see cref="WalRecordHeader"/> 的固定大小（字节）。
    /// </summary>
    public const int WalRecordHeaderSize = 32;
}
