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

    /// <summary>当前文件格式版本号。</summary>
    public const int FormatVersion = 1;

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
