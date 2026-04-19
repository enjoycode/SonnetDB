using TSLite.Engine.Compaction;
using TSLite.Memory;
using TSLite.Storage.Segments;

namespace TSLite.Engine;

/// <summary>
/// TSLite 引擎全局配置选项。
/// </summary>
public sealed class TsdbOptions
{
    /// <summary>数据库根目录路径（默认为当前工作目录 "."）。</summary>
    public string RootDirectory { get; init; } = ".";

    /// <summary>MemTable Flush 阈值策略（默认 16 MB / 100 万点 / 5 分钟）。</summary>
    public MemTableFlushPolicy FlushPolicy { get; init; } = MemTableFlushPolicy.Default;

    /// <summary>Segment 写入选项（默认 64 KB 缓冲、fsync 开启）。</summary>
    public SegmentWriterOptions SegmentWriterOptions { get; init; } = SegmentWriterOptions.Default;

    /// <summary>WAL 写缓冲区大小（字节），默认 64 KB。</summary>
    public int WalBufferSize { get; init; } = 64 * 1024;

    /// <summary>
    /// 是否在每次 Append 后自动 fsync WAL（持久性最强；默认 false，由 Flush 时刻保证）。
    /// </summary>
    public bool SyncWalOnEveryWrite { get; init; } = false;

    /// <summary>段读取选项（默认两项校验均启用）。</summary>
    public SegmentReaderOptions SegmentReaderOptions { get; init; } = SegmentReaderOptions.Default;

    /// <summary>后台 Flush 线程选项（默认启用，轮询间隔 1s，关闭超时 30s）。</summary>
    public BackgroundFlushOptions BackgroundFlush { get; init; } = BackgroundFlushOptions.Default;

    /// <summary>Compaction 策略选项（默认启用，Size-Tiered，MinTierSize=4）。</summary>
    public CompactionPolicy Compaction { get; init; } = CompactionPolicy.Default;

    /// <summary>默认配置实例。</summary>
    public static TsdbOptions Default { get; } = new();
}
