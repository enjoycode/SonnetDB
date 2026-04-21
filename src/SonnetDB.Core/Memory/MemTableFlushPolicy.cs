namespace SonnetDB.Memory;

/// <summary>
/// MemTable 触发 Flush 的阈值策略，基于字节数、数据点数与时间间隔三种条件。
/// </summary>
public sealed class MemTableFlushPolicy
{
    /// <summary>触发 Flush 的字节数上限，默认 16 MB。</summary>
    public long MaxBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>触发 Flush 的数据点数上限，默认 100 万点。</summary>
    public long MaxPoints { get; init; } = 1_000_000;

    /// <summary>触发 Flush 的最大存活时间，默认 5 分钟。</summary>
    public TimeSpan MaxAge { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>默认策略实例（16 MB / 100 万点 / 5 分钟）。</summary>
    public static MemTableFlushPolicy Default { get; } = new();
}
