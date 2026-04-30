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

    /// <summary>
    /// 单个 <see cref="SegmentReader"/> 可用于缓存已解码 Block 的最大字节数；小于等于 0 表示禁用。
    /// 默认 16 MB，缓存以 LRU 策略淘汰，且仅驻留内存。
    /// </summary>
    public long DecodeBlockCacheMaxBytes { get; init; } = 16L * 1024L * 1024L;

    /// <summary>使用默认选项（两项校验均启用）的共享实例。</summary>
    public static SegmentReaderOptions Default { get; } = new();
}
