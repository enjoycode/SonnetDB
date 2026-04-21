namespace SonnetDB.Storage.Segments;

/// <summary>
/// <see cref="SegmentReader"/> 的读取选项。
/// </summary>
public sealed class SegmentReaderOptions
{
    /// <summary>
    /// 是否在 Open 时校验 <see cref="SonnetDB.Storage.Format.SegmentFooter"/> 的 IndexCrc32（默认 true）。
    /// </summary>
    public bool VerifyIndexCrc { get; init; } = true;

    /// <summary>
    /// 读取 Block 时是否校验 <see cref="SonnetDB.Storage.Format.BlockHeader"/> 的 Crc32（默认 true）。
    /// </summary>
    public bool VerifyBlockCrc { get; init; } = true;

    /// <summary>使用默认选项（两项校验均启用）的共享实例。</summary>
    public static SegmentReaderOptions Default { get; } = new();
}
