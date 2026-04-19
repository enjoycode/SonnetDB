using TSLite.Memory;
using TSLite.Storage.Segments;
using TSLite.Wal;

namespace TSLite.Engine;

/// <summary>
/// MemTable → Segment 的 Flush 协调器。串行运行，保证 (写 Segment, 写 WAL Checkpoint, 截断 WAL) 三步原子可见。
/// </summary>
/// <remarks>
/// 崩溃恢复语义：
/// <list type="bullet">
///   <item><description>若 step 2 之前崩溃 → 重启后 WAL 仍含全部 record，replay 重建 MemTable。</description></item>
///   <item><description>若 step 2 完成但 step 3 之前崩溃 → 段文件存在但 WAL 无 Checkpoint。重启时 replay 全部 record（允许冗余）。</description></item>
///   <item><description>若 step 3 完成但 step 5 之前崩溃 → v1 仅记录 LSN，不真正跳过（Milestone 5 优化）。</description></item>
///   <item><description>若 step 5 完成 → 旧 WAL 已删除，新 WAL 从 nextLsn 开始。</description></item>
/// </list>
/// </remarks>
public sealed class FlushCoordinator
{
    private readonly TsdbOptions _options;

    /// <summary>
    /// 创建 <see cref="FlushCoordinator"/> 实例。
    /// </summary>
    /// <param name="options">引擎选项。</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> 为 null 时抛出。</exception>
    public FlushCoordinator(TsdbOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// 执行一次 Flush：将 MemTable 写出为 Segment，追加 WAL Checkpoint，然后截断 WAL。
    /// </summary>
    /// <param name="memTable">要 Flush 的 MemTable 实例。</param>
    /// <param name="walWriter">当前活跃的 WAL 写入器（完成后被替换为新写入器）。</param>
    /// <param name="segmentId">本次生成 Segment 的唯一标识符（单调递增）。</param>
    /// <returns>
    /// Segment 构建结果；若 MemTable 为空则返回 null（不触碰 WAL，不创建 Segment）。
    /// </returns>
    /// <exception cref="ArgumentNullException">任何参数为 null 时抛出。</exception>
    public SegmentBuildResult? Flush(MemTable memTable, ref WalWriter walWriter, long segmentId)
    {
        ArgumentNullException.ThrowIfNull(memTable);

        // 步骤 1：检查 MemTable 是否为空
        if (memTable.PointCount == 0)
            return null;

        // 记录 flush 前的 lastLsn（用于 Checkpoint 记录）
        long lastLsnBeforeFlush = memTable.LastLsn;

        // 步骤 2：写 Segment（临时文件 + 原子 rename，由 SegmentWriter 保证）
        string segPath = TsdbPaths.SegmentPath(_options.RootDirectory, segmentId);
        var segWriter = new SegmentWriter(_options.SegmentWriterOptions);
        var result = segWriter.WriteFrom(memTable, segmentId, segPath);

        // 步骤 3：追加 WAL Checkpoint + Sync
        walWriter.AppendCheckpoint(lastLsnBeforeFlush);
        walWriter.Sync();

        // 步骤 4：重置 MemTable
        memTable.Reset();

        // 步骤 5：截断 WAL（rename + 重建）
        string activeWalPath = TsdbPaths.ActiveWalPath(_options.RootDirectory);
        walWriter = WalTruncator.SwapAndTruncate(
            walWriter,
            activeWalPath,
            walWriter.NextLsn,
            _options.WalBufferSize,
            keepArchive: false);

        return result;
    }
}
