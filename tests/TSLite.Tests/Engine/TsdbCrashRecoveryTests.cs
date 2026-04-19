using TSLite.Engine;
using TSLite.Memory;
using TSLite.Model;
using TSLite.Storage.Segments;
using TSLite.Wal;
using Xunit;

namespace TSLite.Tests.Engine;

/// <summary>
/// <see cref="Tsdb"/> 崩溃恢复场景测试。
/// </summary>
/// <remarks>
/// 三个崩溃场景：
/// <list type="bullet">
///   <item><description>场景 A：写入未 Flush 即崩溃 → WAL replay 重建 MemTable 和 Catalog</description></item>
///   <item><description>场景 B：Flush 完成后崩溃 → segments 保留，WAL replay 重建后续点</description></item>
///   <item><description>场景 C：Flush 中途崩溃（段文件已落盘但 Checkpoint 未写） → 接受冗余 replay</description></item>
/// </list>
/// </remarks>
public sealed class TsdbCrashRecoveryTests : IDisposable
{
    private readonly string _tempDir;

    public TsdbCrashRecoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private TsdbOptions MakeOptions(SegmentWriterOptions? segOpts = null) =>
        new TsdbOptions
        {
            RootDirectory = _tempDir,
            WalBufferSize = 64 * 1024,
            FlushPolicy = new MemTableFlushPolicy
            {
                MaxPoints = 10_000_000,
                MaxBytes = 1024L * 1024 * 1024,
                MaxAge = TimeSpan.FromHours(24),
            },
            SegmentWriterOptions = segOpts ?? new SegmentWriterOptions { FsyncOnCommit = false },
        };

    /// <summary>
    /// 场景 A：写入 10 个点后崩溃（不 Flush，不 Dispose）。
    /// 重启后应通过 WAL replay 恢复全部 10 个点及 catalog。
    /// </summary>
    [Fact]
    public void ScenarioA_WriteWithoutFlush_RecoveredOnReopen()
    {
        const int pointCount = 10;
        var tags = new Dictionary<string, string> { ["host"] = "srv1" };

        // 会话 1：写入但不 Dispose（模拟崩溃）
        var db = Tsdb.Open(MakeOptions());
        for (int i = 0; i < pointCount; i++)
        {
            var p = Point.Create("cpu", 1000L + i, tags,
                new Dictionary<string, FieldValue> { ["usage"] = FieldValue.FromDouble(i * 10.0) });
            db.Write(p);
        }
        db.CrashSimulationCloseWal(); // 崩溃：不保存 catalog，不 Flush

        // 验证：catalog 文件未保存
        Assert.False(File.Exists(TsdbPaths.CatalogPath(_tempDir)));

        // 会话 2：重新打开，应通过 WAL replay 恢复
        using var db2 = Tsdb.Open(MakeOptions());

        // catalog 应通过 WAL 的 CreateSeries 记录重建出 1 个 series
        Assert.Equal(1, db2.Catalog.Count);

        // MemTable 应恢复 10 个点
        Assert.Equal(pointCount, (int)db2.MemTable.PointCount);
    }

    /// <summary>
    /// 场景 B：写入 1000 个点 → Flush → 再写 10 个点 → 崩溃。
    /// 重启后：segments 含 1 个，MemTable 含 10 个点。
    /// </summary>
    [Fact]
    public void ScenarioB_FlushThenWriteThenCrash_RecoveredOnReopen()
    {
        const int flushCount = 100;
        const int afterFlushCount = 10;
        var tags = new Dictionary<string, string> { ["host"] = "srv1" };

        // 会话 1：写入、Flush、再写、崩溃
        var db = Tsdb.Open(MakeOptions());
        for (int i = 0; i < flushCount; i++)
        {
            var p = Point.Create("cpu", 1000L + i, tags,
                new Dictionary<string, FieldValue> { ["usage"] = FieldValue.FromDouble(i) });
            db.Write(p);
        }

        var flushResult = db.FlushNow();
        Assert.NotNull(flushResult);
        Assert.Equal(0L, db.MemTable.PointCount);

        // 再写 10 个点
        for (int i = 0; i < afterFlushCount; i++)
        {
            var p = Point.Create("cpu", 2000L + i, tags,
                new Dictionary<string, FieldValue> { ["usage"] = FieldValue.FromDouble(100 + i) });
            db.Write(p);
        }

        Assert.Equal(afterFlushCount, (int)db.MemTable.PointCount);
        db.CrashSimulationCloseWal(); // 崩溃

        // 会话 2：重新打开
        using var db2 = Tsdb.Open(MakeOptions());

        // Segment 应保留
        var segs = db2.ListSegments();
        Assert.Single(segs);

        // catalog 通过 WAL replay 重建
        Assert.Equal(1, db2.Catalog.Count);

        // MemTable 应包含 Flush 之后写入的 10 个点
        Assert.Equal(afterFlushCount, (int)db2.MemTable.PointCount);
    }

    /// <summary>
    /// 场景 C：SegmentWriter 完成 rename 后、写 Checkpoint 之前崩溃。
    /// v1 接受冗余 replay：段文件存在，WAL 无 Checkpoint，重新 Open 时全部点可以回放。
    /// </summary>
    [Fact]
    public void ScenarioC_SegmentWrittenButCheckpointNotWritten_RedundantReplayAccepted()
    {
        const int pointCount = 5;
        var tags = new Dictionary<string, string> { ["host"] = "srv1" };

        // 配置：rename 后立即抛出异常，模拟 rename 之后、Checkpoint 之前崩溃
        bool crashed = false;
        var segOpts = new SegmentWriterOptions
        {
            FsyncOnCommit = false,
            PostRenameAction = () =>
            {
                if (!crashed)
                {
                    crashed = true;
                    throw new IOException("Simulated crash after rename");
                }
            },
        };
        var options = MakeOptions(segOpts);

        // 会话 1：写入点，然后尝试 FlushNow（会在 rename 之后崩溃）
        var db = Tsdb.Open(options);
        for (int i = 0; i < pointCount; i++)
        {
            var p = Point.Create("cpu", 1000L + i, tags,
                new Dictionary<string, FieldValue> { ["usage"] = FieldValue.FromDouble(i * 10.0) });
            db.Write(p);
        }

        // FlushNow 应抛出异常（来自 PostRenameAction）
        Assert.Throws<IOException>(() => db.FlushNow());

        // 段文件应已存在（rename 已完成）
        string segPath = TsdbPaths.SegmentPath(_tempDir, 1L);
        Assert.True(File.Exists(segPath), "Segment file should exist after rename");

        // 强制关闭（崩溃模拟：不保存 catalog，不再次 Flush）
        db.CrashSimulationCloseWal();

        // 验证：WAL 中没有 Checkpoint 记录
        using (var walReader = WalReader.Open(TsdbPaths.ActiveWalPath(_tempDir)))
        {
            var records = walReader.Replay().ToList();
            var checkpoints = records.OfType<CheckpointRecord>().ToList();
            Assert.Empty(checkpoints);
        }

        // 会话 2：重新打开（不使用崩溃注入选项）
        using var db2 = Tsdb.Open(MakeOptions());

        // catalog 通过 WAL replay 重建
        Assert.Equal(1, db2.Catalog.Count);

        // v1：MemTable 应冗余回放出全部点（允许）
        Assert.Equal(pointCount, (int)db2.MemTable.PointCount);

        // 段文件仍然存在
        Assert.True(File.Exists(segPath));

        // 下一次 FlushNow 使用新的 SegmentId（不重复使用 1）
        var result = db2.FlushNow();
        Assert.NotNull(result);
        Assert.Equal(2L, result.SegmentId);
    }

    /// <summary>
    /// 崩溃后重新打开，catalog 通过 WAL 的 CreateSeries 记录正确重建。
    /// </summary>
    [Fact]
    public void CrashRecovery_CatalogRebuiltFromWalCreateSeries()
    {
        var tags1 = new Dictionary<string, string> { ["host"] = "a" };
        var tags2 = new Dictionary<string, string> { ["host"] = "b" };

        // 会话 1：写入两个不同 series 的点，然后崩溃
        var db = Tsdb.Open(MakeOptions());
        db.Write(Point.Create("cpu", 1000L, tags1,
            new Dictionary<string, FieldValue> { ["v"] = FieldValue.FromDouble(1.0) }));
        db.Write(Point.Create("cpu", 2000L, tags2,
            new Dictionary<string, FieldValue> { ["v"] = FieldValue.FromDouble(2.0) }));
        db.CrashSimulationCloseWal();

        // 会话 2：通过 WAL replay 重建 catalog
        using var db2 = Tsdb.Open(MakeOptions());
        Assert.Equal(2, db2.Catalog.Count);
        Assert.Equal(2L, db2.MemTable.PointCount);
    }
}
