using TSLite.Memory;
using TSLite.Storage.Segments;
using TSLite.Wal;

namespace TSLite.Engine;

/// <summary>
/// MemTable → Segment 的 Flush 协调器。串行运行，保证 (写 Segment, 写 WAL Checkpoint, Roll WAL, 回收旧段, 重置 MemTable) 五步原子可见。
/// </summary>
/// <remarks>
/// 崩溃恢复语义：
/// <list type="bullet">
///   <item><description>若 step 2 之前崩溃 → 重启后 WAL 仍含全部 record，replay 重建 MemTable。</description></item>
///   <item><description>若 step 2 完成但 step 3 之前崩溃 → 段文件存在但 WAL 无 Checkpoint。重启时 replay 全部 record（允许冗余）。</description></item>
///   <item><description>若 step 3 完成（AppendCheckpoint+Sync）但 step 4（Roll）之前崩溃 → Checkpoint 已记录，replay 将跳过已落盘 WritePoint。</description></item>
///   <item><description>若 step 4（Roll）完成但 step 5（RecycleUpTo）之前崩溃 → 旧 segment 仍存在，重启后 RecycleUpTo 将在下次 Flush 时清理。</description></item>
///   <item><description>若全部步骤完成 → 旧 segment 已删除，新 active segment 从 nextLsn 开始。</description></item>
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
    /// 执行一次 Flush：将 MemTable 写出为 Segment，追加 WAL Checkpoint，Roll WAL，回收旧 WAL segment，然后重置 MemTable。
    /// </summary>
    /// <param name="memTable">要 Flush 的 MemTable 实例。</param>
    /// <param name="walSet">当前活跃的 WAL segment 集合管理器。</param>
    /// <param name="segmentId">本次生成 Segment 的唯一标识符（单调递增）。</param>
    /// <returns>
    /// Segment 构建结果；若 MemTable 为空则返回 null（不触碰 WAL，不创建 Segment）。
    /// </returns>
    /// <exception cref="ArgumentNullException">任何参数为 null 时抛出。</exception>
    public SegmentBuildResult? Flush(MemTable memTable, WalSegmentSet walSet, long segmentId)
    {
        ArgumentNullException.ThrowIfNull(memTable);
        ArgumentNullException.ThrowIfNull(walSet);

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
        long checkpointRecordLsn = walSet.AppendCheckpoint(lastLsnBeforeFlush);
        walSet.Sync();

        // 步骤 4：主动 Roll（确保 checkpoint 之前的数据归到一个已封存的 segment，
        //         使 active segment 的 startLsn 严格 > checkpointLsn，避免 RecycleUpTo 误删）
        walSet.Roll();

        // 步骤 5：回收已 checkpoint 的旧 WAL segment
        // 使用 checkpoint 记录本身的 LSN（= lastLsnBeforeFlush + 1），
        // 保证包含 Checkpoint 记录的 segment 也能被正确回收
        walSet.RecycleUpTo(checkpointRecordLsn);

        // 步骤 6：重置 MemTable
        memTable.Reset();

        return result;
    }
}
