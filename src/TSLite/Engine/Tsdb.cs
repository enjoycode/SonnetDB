using TSLite.Catalog;
using TSLite.Memory;
using TSLite.Model;
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
/// 单实例只能由一个进程打开（FileShare.None 锁保护 active WAL）。
/// </summary>
public sealed class Tsdb : IDisposable
{
    private readonly TsdbOptions _options;
    private readonly FlushCoordinator _flushCoordinator;
    private readonly object _writeSync = new();
    private readonly HashSet<ulong> _seriesWithWalRecord;

    private WalWriter? _walWriter;
    private long _nextSegmentId;
    private bool _disposed;

    /// <summary>数据库根目录路径。</summary>
    public string RootDirectory => _options.RootDirectory;

    /// <summary>当前序列目录。</summary>
    public SeriesCatalog Catalog { get; }

    /// <summary>当前内存层（MemTable）。</summary>
    public MemTable MemTable { get; }

    /// <summary>下一个将分配的 SegmentId（线程安全读取）。</summary>
    public long NextSegmentId
    {
        get
        {
            lock (_writeSync)
                return _nextSegmentId;
        }
    }

    private Tsdb(
        TsdbOptions options,
        SeriesCatalog catalog,
        MemTable memTable,
        WalWriter walWriter,
        long nextSegmentId,
        HashSet<ulong> seriesWithWalRecord)
    {
        _options = options;
        Catalog = catalog;
        MemTable = memTable;
        _walWriter = walWriter;
        _nextSegmentId = nextSegmentId;
        _seriesWithWalRecord = seriesWithWalRecord;
        _flushCoordinator = new FlushCoordinator(options);
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

        // 回放 WAL（若文件存在）
        var memTable = new MemTable();
        string walPath = TsdbPaths.ActiveWalPath(root);
        if (File.Exists(walPath) && new FileInfo(walPath).Length > 0)
        {
            var records = WalReplay.ReplayInto(walPath, catalog);
            memTable.ReplayFrom(records);
        }

        // 记录"已在 WAL 中有 CreateSeries 记录"的 series 集合（replay 后的 catalog 快照）
        var seriesWithWalRecord = new HashSet<ulong>();
        foreach (var entry in catalog.Snapshot())
            seriesWithWalRecord.Add(entry.Id);

        // 打开 WAL 写入器（已有文件时自动续写）
        var walWriter = WalWriter.Open(walPath, startLsn: 1, bufferSize: options.WalBufferSize);

        return new Tsdb(options, catalog, memTable, walWriter, nextSegmentId, seriesWithWalRecord);
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
                _walWriter!.AppendCreateSeries(entry.Id, entry.Measurement, entry.Tags);

            // 每个字段写入 WAL 和 MemTable
            foreach (var (fieldName, value) in point.Fields)
            {
                long lsn = _walWriter!.AppendWritePoint(entry.Id, point.Timestamp, fieldName, value);
                MemTable.Append(entry.Id, point.Timestamp, fieldName, value, lsn);
            }

            if (_options.SyncWalOnEveryWrite)
                _walWriter!.Sync();

            if (MemTable.ShouldFlush(_options.FlushPolicy))
                FlushNowLocked();
        }
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
    /// 主动触发一次 Flush：把 MemTable 写出为 Segment，追加 WAL Checkpoint，然后截断 WAL。
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
    /// 关闭数据库：Flush 剩余 MemTable、保存 catalog、关闭 WAL。
    /// </summary>
    public void Dispose()
    {
        lock (_writeSync)
        {
            if (_disposed)
                return;
            _disposed = true;

            WalWriter? writerToDispose = _walWriter;
            _walWriter = null;

            try
            {
                if (writerToDispose != null)
                {
                    // 尝试 Flush 剩余数据
                    if (MemTable.PointCount > 0)
                    {
                        try
                        {
                            var writer = writerToDispose;
                            _flushCoordinator.Flush(MemTable, ref writer, _nextSegmentId++);
                            writerToDispose = writer;
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
                writerToDispose?.Dispose();
            }
        }
    }

    // ── 内部辅助 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 执行 Flush（调用方必须持有 _writeSync 锁）。
    /// </summary>
    private SegmentBuildResult? FlushNowLocked()
    {
        if (_walWriter == null)
            return null;

        long segId = _nextSegmentId++;
        var result = _flushCoordinator.Flush(MemTable, ref _walWriter!, segId);

        // Flush 成功后，向新 WAL 重写所有 catalog 条目的 CreateSeries 记录，
        // 确保在 .tslcat 未落盘的情况下崩溃恢复仍能从 WAL 重建 catalog。
        if (result != null)
        {
            foreach (var entry in Catalog.Snapshot())
                _walWriter!.AppendCreateSeries(entry.Id, entry.Measurement, entry.Tags);
            _walWriter!.Sync();
        }

        return result;
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
            _walWriter?.Dispose();
            _walWriter = null;
        }
    }
}
