namespace TSLite.Buffers;

/// <summary>TSLite 文件格式魔数与版本常量。</summary>
public static class TsdbMagic
{
    /// <summary>主文件 magic："TSLITE\0\0"（8 字节）。</summary>
    public static ReadOnlySpan<byte> File => "TSLITE\0\0"u8;

    /// <summary>段文件 magic："TSLSEGv1"（8 字节）。</summary>
    public static ReadOnlySpan<byte> Segment => "TSLSEGv1"u8;

    /// <summary>WAL 文件 magic："TSLWALv1"（8 字节）。</summary>
    public static ReadOnlySpan<byte> Wal => "TSLWALv1"u8;

    /// <summary>目录文件 magic："TSLCATv1"（8 字节）。</summary>
    public static ReadOnlySpan<byte> Catalog => "TSLCATv1"u8;

    /// <summary>当前 TSLite 容器文件格式版本号（FileHeader / WAL / Catalog 等通用文件）。</summary>
    public const int FormatVersion = 1;

    /// <summary>
    /// 段文件（<c>.tslseg</c>）格式版本号。
    /// <para>
    /// v2（PR #50）：<see cref="TSLite.Storage.Format.BlockHeader"/> 中 <c>AggregateMin</c> /
    /// <c>AggregateMax</c> 由 4 字节窄类型位模式（Float64 走 <c>float</c>、Int64 走 <c>int32</c>）
    /// 升级为 8 字节 <see cref="double"/>，无损覆盖 Float64 / Int64 全部范围；
    /// BlockHeader 大小由 64B 增至 72B；旧 v1 段文件被 <c>SegmentReader</c> 拒绝。
    /// </para>
    /// </summary>
    public const int SegmentFormatVersion = 2;

    /// <summary>构造文件 magic <see cref="InlineBytes8"/>。</summary>
    /// <returns>内容为 <c>"TSLITE\0\0"</c> 的 <see cref="InlineBytes8"/> 实例。</returns>
    public static InlineBytes8 CreateFileMagic()
    {
        InlineBytes8 magic = default;
        File.CopyTo(magic.AsSpan());
        return magic;
    }

    /// <summary>构造段 magic <see cref="InlineBytes8"/>。</summary>
    /// <returns>内容为 <c>"TSLSEGv1"</c> 的 <see cref="InlineBytes8"/> 实例。</returns>
    public static InlineBytes8 CreateSegmentMagic()
    {
        InlineBytes8 magic = default;
        Segment.CopyTo(magic.AsSpan());
        return magic;
    }

    /// <summary>构造 WAL magic <see cref="InlineBytes8"/>。</summary>
    /// <returns>内容为 <c>"TSLWALv1"</c> 的 <see cref="InlineBytes8"/> 实例。</returns>
    public static InlineBytes8 CreateWalMagic()
    {
        InlineBytes8 magic = default;
        Wal.CopyTo(magic.AsSpan());
        return magic;
    }

    /// <summary>构造目录 magic <see cref="InlineBytes8"/>。</summary>
    /// <returns>内容为 <c>"TSLCATv1"</c> 的 <see cref="InlineBytes8"/> 实例。</returns>
    public static InlineBytes8 CreateCatalogMagic()
    {
        InlineBytes8 magic = default;
        Catalog.CopyTo(magic.AsSpan());
        return magic;
    }
}
