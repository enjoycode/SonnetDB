using TSLite.Catalog;
using TSLite.Engine.Compaction;
using TSLite.Memory;
using TSLite.Model;
using TSLite.Query;
using TSLite.Storage.Segments;
using TSLite.Wal;

namespace TSLite.Engine;

/// <summary>
/// TSLite 嵌入式时序数据库门面。负责：
/// <list type="bullet">
///   <item><description>启动时加载 catalog、回放 WAL 重建 MemTable；</description></item>
///   <item><description>写入路径：Append → WAL → MemTable，必要时触发 Flush；</description></item>
///   <item><description>关闭时：Flush MemTable + 持久化 catalog。</description></item>
/// </list>
/// 单实例只能由一个进程打开（WalSegmentSet 的 active segment 文件句柄提供锁保护）。
/// </summary>
public sealed class Tsdb : IDisposable
{
    private readonly TsdbOptions _options;
    private readonly FlushCoordinator _flushCoordinator;
    private readonly object _writeSync = new();
    private readonly HashSet<ulong> _seriesWithWalRecord;

    private WalSegmentSet? _walSet;
    private long _nextSegmentId;
    private bool _disposed;
    private BackgroundFlushWorker? _flushWorker;
    private CompactionWorker? _compactionWorker;
    private long _checkpointLsn;

    /// <summary>数据库根目录路径。</summary>
    public string RootDirectory => _options.RootDirectory;

    /// <summary>当前序列目录。</summary>
    public SeriesCatalog Catalog { get; }

    /// <summary>当前内存层（MemTable）。</summary>
    public MemTable MemTable { get; }

    /// <summary>段集合与索引快照管理器。</summary>
    public SegmentManager Segments { get; }

    /// <summary>查询执行器：合并 MemTable 与多个 Segment 的候选 Block，提供原始点查询与聚合查询。</summary>
    public QueryEngine Query { get; }

    /// <summary>下一个将分配的 SegmentId（线程安全读取）。</summary>
    public long NextSegmentId
    {
        get
        {
            lock (_writeSync)
                return _nextSegmentId;
        }
    }

    /// <summary>最近一次 WAL Checkpoint 的 LSN（启动时从 WAL replay 获得；仅诊断/测试用）。</summary>
    public long CheckpointLsn
    {
        get
        {
            lock (_writeSync)
                return _checkpointLsn;
        }
    }

    /// <summary>后台 Flush 策略（供 BackgroundFlushWorker 访问）。</summary>
    internal MemTableFlushPolicy BackgroundFlushPolicy => _options.FlushPolicy;

    /// <summary>Compaction 写入选项（供 CompactionWorker 访问）。</summary>
    internal SegmentWriterOptions CompactionWriterOptions => _options.SegmentWriterOptions;

    /// <summary>
    /// 线程安全地分配下一个 SegmentId（单调递增）。
    /// </summary>
    /// <returns>新分配的 SegmentId。</returns>
    internal long AllocateSegmentId()
    {
        lock (_writeSync)
            return _nextSegmentId++;
    }

    private Tsdb(
        TsdbOptions options,
        SeriesCatalog catalog,
        MemTable memTable,
        WalSegmentSet walSet,
        long nextSegmentId,
        HashSet<ulong> seriesWithWalRecord,
        SegmentManager segmentManager,
        long checkpointLsn)
    {
        _options = options;
        Catalog = catalog;
        MemTable = memTable;
        _walSet = walSet;
        _nextSegmentId = nextSegmentId;
        _seriesWithWalRecord = seriesWithWalRecord;
        Segments = segmentManager;
        _flushCoordinator = new FlushCoordinator(options);
        Query = new QueryEngine(memTable, segmentManager, catalog);
        _checkpointLsn = checkpointLsn;
    }

    /// <summary>
    /// 打开（不存在则创建）TSDB 根目录，自动加载 catalog 并回放 WAL。
    /// </summary>
    /// <param name="options">引擎选项；为 null 时使用 <see cref="TsdbOptions.Default"/>。</param>
    /// <returns>已初始化的 <see cref="Tsdb"/> 实例。</returns>
    public static Tsdb Open(TsdbOptions? options = null)
    {
        options ??= TsdbOptions.Default;

        string root = options.RootDirectory;
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(TsdbPaths.WalDir(root));
        Directory.CreateDirectory(TsdbPaths.SegmentsDir(root));

        // 加载 catalog（文件不存在时返回空目录）
        var catalog = CatalogFileCodec.Load(TsdbPaths.CatalogPath(root));

        // 扫描已存在的 Segment，计算 NextSegmentId
        long nextSegmentId = 1;
        foreach (var (segId, _) in TsdbPaths.EnumerateSegments(root))
        {
            if (segId + 1 > nextSegmentId)
                nextSegmentId = segId + 1;
        }

        // 打开 WAL segment 集合（自动升级 legacy active.tslwal）
        string walDir = TsdbPaths.WalDir(root);
        var walSet = WalSegmentSet.Open(walDir, options.WalRolling, options.WalBufferSize, initialStartLsn: 1);

        // 回放全部 WAL segment，使用 Checkpoint LSN 跳过已落盘记录
        var memTable = new MemTable();
        var result = walSet.ReplayWithCheckpoint(catalog);
        memTable.ReplayFrom(result.WritePoints);
        long checkpointLsn = result.CheckpointLsn;

        var seriesWithWalRecord = catalog.Snapshot().Select(e => e.Id).ToHashSet();

        var segmentManager = SegmentManager.Open(root, options.SegmentReaderOptions);

        var tsdb = new Tsdb(options, catalog, memTable, walSet, nextSegmentId, seriesWithWalRecord, segmentManager, checkpointLsn);

        // 启动后台 Flush 线程
        if (options.BackgroundFlush.Enabled)
        {
            tsdb._flushWorker = new BackgroundFlushWorker(tsdb, options.BackgroundFlush);
            tsdb._flushWorker.Start();
        }

        // 启动后台 Compaction 线程
        if (options.Compaction.Enabled)
        {
            tsdb._compactionWorker = new CompactionWorker(tsdb, options.Compaction);
            tsdb._compactionWorker.Start();
        }

        return tsdb;
    }

    /// <summary>
    /// 写入一个 Point。自动写入 WAL，追加到 MemTable，必要时触发 Flush。
    /// </summary>
    /// <param name="point">要写入的数据点（已校验）。</param>
    /// <exception cref="ArgumentNullException"><paramref name="point"/> 为 null 时抛出。</exception>
    /// <exception cref="ObjectDisposedException">实例已关闭时抛出。</exception>
    public void Write(Point point)
    {
        ArgumentNullException.ThrowIfNull(point);

        lock (_writeSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var entry = Catalog.GetOrAdd(point);

            // 若是本进程首次写入该 series，向 WAL 追加 CreateSeries 记录
            if (_seriesWithWalRecord.Add(entry.Id))
                _walSet!.AppendCreateSeries(entry.Id, entry.Measurement, entry.Tags);

            // 每个字段写入 WAL 和 MemTable
            foreach (var (fieldName, value) in point.Fields)
            {
                long lsn = _walSet!.AppendWritePoint(entry.Id, point.Timestamp, fieldName, value);
                MemTable.Append(entry.Id, point.Timestamp, fieldName, value, lsn);
            }

            if (_options.SyncWalOnEveryWrite)
                _walSet!.Sync();
        }

        // 锁外向后台线程发送非阻塞信号
        _flushWorker?.Signal();
    }

    /// <summary>
    /// 批量写入多个 Point。
    /// </summary>
    /// <param name="points">要写入的数据点序列。</param>
    /// <returns>成功写入的点数量。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> 为 null 时抛出。</exception>
    /// <exception cref="ObjectDisposedException">实例已关闭时抛出。</exception>
    public int WriteMany(IEnumerable<Point> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        int count = 0;
        foreach (var point in points)
        {
            Write(point);
            count++;
        }
        return count;
    }

    /// <summary>
    /// 主动触发一次 Flush：把 MemTable 写出为 Segment，追加 WAL Checkpoint，Roll WAL，回收旧段，重置 MemTable。
    /// </summary>
    /// <returns>Segment 构建结果；MemTable 为空时返回 null。</returns>
    /// <exception cref="ObjectDisposedException">实例已关闭时抛出。</exception>
    public SegmentBuildResult? FlushNow()
    {
        lock (_writeSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return FlushNowLocked();
        }
    }

    /// <summary>
    /// 枚举当前已落盘的 Segment 文件，按 SegmentId 升序排列。
    /// </summary>
    /// <returns>已落盘段文件的 (SegmentId, FilePath) 只读列表。</returns>
    public IReadOnlyList<(long SegmentId, string Path)> ListSegments()
    {
        var list = new List<(long SegmentId, string Path)>(TsdbPaths.EnumerateSegments(RootDirectory));
        list.Sort(static (a, b) => a.SegmentId.CompareTo(b.SegmentId));
        return list.AsReadOnly();
    }

    /// <summary>
    /// 关闭数据库：先关闭后台 Flush 线程，再 Flush 剩余 MemTable、保存 catalog、关闭 WAL。
    /// </summary>
    public void Dispose()
    {
        // 先关闭 Compaction 后台线程（在锁外，防止与内部操作死锁）
        _compactionWorker?.Dispose();
        _compactionWorker = null;

        // 再关闭后台 Flush 线程（在锁外，防止与 InternalFlushFromBackground 死锁）
        _flushWorker?.Dispose();
        _flushWorker = null;

        lock (_writeSync)
        {
            if (_disposed)
                return;
            _disposed = true;

            WalSegmentSet? walSetToDispose = _walSet;
            _walSet = null;

            try
            {
                if (walSetToDispose != null)
                {
                    // 尝试 Flush 剩余数据
                    if (MemTable.PointCount > 0)
                    {
                        try
                        {
                            var result = _flushCoordinator.Flush(MemTable, walSetToDispose, _nextSegmentId++);
                            if (result != null)
                                _checkpointLsn = MemTable.LastLsn;
                        }
                        catch
                        {
                            // Flush 失败不应阻止 catalog 保存和 WAL 关闭
                        }
                    }

                    // 保存 catalog
                    CatalogFileCodec.Save(Catalog, TsdbPaths.CatalogPath(RootDirectory));
                }
            }
            finally
            {
                walSetToDispose?.Dispose();
                Segments.Dispose();
            }
        }
    }

    // ── 内部辅助 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 执行 Flush（调用方必须持有 _writeSync 锁）。
    /// </summary>
    private SegmentBuildResult? FlushNowLocked()
    {
        if (_walSet == null)
            return null;

        long lsnBeforeFlush = MemTable.LastLsn;
        long segId = _nextSegmentId++;
        var result = _flushCoordinator.Flush(MemTable, _walSet, segId);

        // Flush 成功后，向新 WAL 重写所有 catalog 条目的 CreateSeries 记录，
        // 确保在 .tslcat 未落盘的情况下崩溃恢复仍能从 WAL 重建 catalog。
        if (result != null)
        {
            // 更新 CheckpointLsn（在锁内完成）
            if (lsnBeforeFlush != long.MinValue)
                _checkpointLsn = lsnBeforeFlush;

            // 仅在非关闭路径（非 Dispose 内部调用）时更新索引快照
            if (!_disposed)
                Segments.AddSegment(result.Path);

            foreach (var entry in Catalog.Snapshot())
                _walSet.AppendCreateSeries(entry.Id, entry.Measurement, entry.Tags);
            _walSet.Sync();
        }

        return result;
    }

    /// <summary>
    /// 由后台 Flush 线程调用的 Flush 入口。与同步 FlushNow 共享 _writeSync 锁，保证互斥。
    /// </summary>
    internal void InternalFlushFromBackground()
    {
        lock (_writeSync)
        {
            if (_disposed)
                return;
            FlushNowLocked();
        }
    }

    /// <summary>
    /// （仅测试用）模拟进程崩溃：直接关闭 WAL，不保存 catalog，不 Flush MemTable。
    /// </summary>
    internal void CrashSimulationCloseWal()
    {
        lock (_writeSync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _walSet?.Dispose();
            _walSet = null;
        }
    }
}
