using SonnetDB.Catalog;
using SonnetDB.Model;
using SonnetDB.Wal;
using Xunit;

namespace SonnetDB.Core.Tests.Wal;

/// <summary>
/// <see cref="WalSegmentSet"/> 单元测试。
/// </summary>
public sealed class WalSegmentSetTests : IDisposable
{
    private readonly string _tempDir;

    public WalSegmentSetTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private WalSegmentSet OpenSet(WalRollingPolicy? policy = null, long initialStartLsn = 1) =>
        WalSegmentSet.Open(_tempDir, policy, bufferSize: 4 * 1024, initialStartLsn: initialStartLsn);

    // ── Open / 基本属性 ─────────────────────────────────────────────────────

    [Fact]
    public void Open_EmptyDirectory_CreatesOnlyOneSegment()
    {
        using var set = OpenSet();

        Assert.Equal(1L, set.ActiveStartLsn);
        Assert.Single(set.Segments);
        Assert.Equal(1L, set.NextLsn); // 空文件，LSN 尚未分配
    }

    [Fact]
    public void Open_EmptyDirectory_SegmentFileExists()
    {
        using var set = OpenSet();

        string expectedPath = WalSegmentLayout.SegmentPath(_tempDir, 1L);
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public void Open_WithInitialStartLsn_UsesCorrectFileName()
    {
        using var set = OpenSet(initialStartLsn: 100L);

        string expectedPath = WalSegmentLayout.SegmentPath(_tempDir, 100L);
        Assert.True(File.Exists(expectedPath));
        Assert.Equal(100L, set.ActiveStartLsn);
    }

    // ── Append ──────────────────────────────────────────────────────────────

    [Fact]
    public void AppendWritePoint_ReturnsSequentialLsns()
    {
        using var set = OpenSet();

        long lsn1 = set.AppendWritePoint(1UL, 1000L, "cpu", FieldValue.FromDouble(1.0));
        long lsn2 = set.AppendWritePoint(1UL, 2000L, "cpu", FieldValue.FromDouble(2.0));
        long lsn3 = set.AppendCreateSeries(1UL, "cpu", new Dictionary<string, string> { ["host"] = "h1" });

        Assert.Equal(1L, lsn1);
        Assert.Equal(2L, lsn2);
        Assert.Equal(3L, lsn3);
    }

    [Fact]
    public void AppendCheckpoint_ReturnsLsn()
    {
        using var set = OpenSet();

        long lsn = set.AppendCheckpoint(42L);
        Assert.Equal(1L, lsn);
    }

    // ── Sync ────────────────────────────────────────────────────────────────

    [Fact]
    public void Sync_DoesNotThrow()
    {
        using var set = OpenSet();
        set.AppendWritePoint(1UL, 1000L, "cpu", FieldValue.FromDouble(1.0));
        set.Sync(); // should not throw
    }

    // ── Roll ────────────────────────────────────────────────────────────────

    [Fact]
    public void Roll_CreatesNewSegment()
    {
        using var set = OpenSet();

        set.AppendWritePoint(1UL, 1000L, "cpu", FieldValue.FromDouble(1.0));
        long nextLsnBeforeRoll = set.NextLsn; // should be 2

        set.Roll();

        Assert.Equal(2, set.Segments.Count);
        Assert.Equal(nextLsnBeforeRoll, set.ActiveStartLsn);
    }

    [Fact]
    public void Roll_OnEmptySegment_DoesNotCreateNewSegment()
    {
        // 空 segment（仅含文件头）不应产生新 segment
        using var set = OpenSet();
        set.Roll(); // 空 segment，跳过

        Assert.Single(set.Segments);
    }

    [Fact]
    public void Roll_MultipleRolls_SegmentCountIncreases()
    {
        using var set = OpenSet();

        for (int i = 0; i < 5; i++)
        {
            set.AppendWritePoint(1UL, 1000L + i, "cpu", FieldValue.FromDouble(i));
            set.Roll();
        }

        Assert.Equal(6, set.Segments.Count);
    }

    // ── 自动 Roll（字节阈值）─────────────────────────────────────────────────

    [Fact]
    public void AutoRoll_OnByteThreshold_CreatesNewSegment()
    {
        // 设置极小的阈值（512 字节）触发自动 Roll
        var policy = new WalRollingPolicy { Enabled = true, MaxBytesPerSegment = 512, MaxRecordsPerSegment = 1_000_000 };
        using var set = OpenSet(policy);

        int initialCount = set.Segments.Count;

        // 写入足够多的数据触发 Roll
        for (int i = 0; i < 20; i++)
            set.AppendWritePoint(1UL, 1000L + i, "cpu", FieldValue.FromDouble(i));

        Assert.True(set.Segments.Count > initialCount, "Expected at least one auto Roll");
    }

    [Fact]
    public void AutoRoll_OnRecordThreshold_CreatesNewSegment()
    {
        // 设置极小的记录数阈值触发自动 Roll
        var policy = new WalRollingPolicy { Enabled = true, MaxBytesPerSegment = long.MaxValue, MaxRecordsPerSegment = 5 };
        using var set = OpenSet(policy);

        for (int i = 0; i < 6; i++)
            set.AppendWritePoint(1UL, 1000L + i, "cpu", FieldValue.FromDouble(i));

        Assert.True(set.Segments.Count >= 2, "Expected auto Roll after 5 records");
    }

    [Fact]
    public void AutoRoll_Disabled_NoAutoRoll()
    {
        var policy = new WalRollingPolicy { Enabled = false };
        using var set = OpenSet(policy);

        for (int i = 0; i < 100; i++)
            set.AppendWritePoint(1UL, 1000L + i, "cpu", FieldValue.FromDouble(i));

        Assert.Single(set.Segments);
    }

    // ── RecycleUpTo ─────────────────────────────────────────────────────────

    [Fact]
    public void RecycleUpTo_DeletesOldSegments()
    {
        using var set = OpenSet();

        // 创建 3 个 segment
        for (int i = 0; i < 3; i++)
        {
            set.AppendWritePoint(1UL, 1000L + i, "cpu", FieldValue.FromDouble(i));
            set.Roll();
        }

        Assert.Equal(4, set.Segments.Count); // 3 rolled + 1 active

        // active.StartLsn - 1 = maxCheckpoint that can be recycled (not touching active)
        long activeStartLsn = set.ActiveStartLsn;
        int recycled = set.RecycleUpTo(activeStartLsn - 1);

        // 应删除除 active 外的所有段
        Assert.Equal(3, recycled);
        Assert.Single(set.Segments);
    }

    [Fact]
    public void RecycleUpTo_NeverDeletesActiveSegment()
    {
        using var set = OpenSet();

        set.AppendWritePoint(1UL, 1000L, "cpu", FieldValue.FromDouble(1.0));

        // checkpoint >= active.startLsn - 1 时不应删除 active
        int recycled = set.RecycleUpTo(long.MaxValue);

        Assert.Equal(0, recycled);
        Assert.Single(set.Segments);
    }

    [Fact]
    public void RecycleUpTo_BoundaryCondition_EqualLastLsn_Deletes()
    {
        using var set = OpenSet();

        // segment 1: LSN 1 (single record), then Roll
        set.AppendWritePoint(1UL, 1000L, "cpu", FieldValue.FromDouble(1.0));
        set.Roll();

        // segment 2 (active): no records

        // segment 1 的 lastLsn = seg2.startLsn - 1 = 1
        // checkpointLsn=1 应满足 lastLsn(1) <= checkpointLsn(1)
        int recycled = set.RecycleUpTo(1L);

        Assert.Equal(1, recycled);
        Assert.Single(set.Segments); // 只剩 active
    }

    [Fact]
    public void RecycleUpTo_OldSegmentFilesDeleted()
    {
        using var set = OpenSet();

        set.AppendWritePoint(1UL, 1000L, "cpu", FieldValue.FromDouble(1.0));
        string oldSegPath = set.Segments[0].Path;
        set.Roll();

        Assert.True(File.Exists(oldSegPath));

        set.RecycleUpTo(long.MaxValue - 1);

        Assert.False(File.Exists(oldSegPath));
    }

    // ── ReplayWithCheckpoint ─────────────────────────────────────────────────

    [Fact]
    public void ReplayWithCheckpoint_EmptySet_ReturnsZeroResult()
    {
        using var set = OpenSet();
        var catalog = new SeriesCatalog();

        var result = set.ReplayWithCheckpoint(catalog);

        Assert.Equal(0, result.CheckpointLsn);
        Assert.Equal(0, result.LastLsn);
        Assert.Empty(result.WritePoints);
    }

    [Fact]
    public void ReplayWithCheckpoint_SingleSegment_ReturnsAllPoints()
    {
        var tags = new Dictionary<string, string> { ["host"] = "h1" };
        var preCatalog = new SeriesCatalog();
        var entry = preCatalog.GetOrAdd("cpu", tags);

        using var set = OpenSet();
        set.AppendCreateSeries(entry.Id, "cpu", tags);
        for (int i = 0; i < 10; i++)
            set.AppendWritePoint(entry.Id, 1000L + i, "usage", FieldValue.FromDouble(i));
        set.Sync();

        var catalog = new SeriesCatalog();
        var result = set.ReplayWithCheckpoint(catalog);

        Assert.Equal(10, result.WritePoints.Count);
        Assert.Equal(1, catalog.Count);
        Assert.Equal(0, result.CheckpointLsn);
    }

    [Fact]
    public void ReplayWithCheckpoint_WithCheckpoint_SkipsEarlierPoints()
    {
        var tags = new Dictionary<string, string> { ["host"] = "h1" };
        var preCatalog = new SeriesCatalog();
        var entry = preCatalog.GetOrAdd("cpu", tags);

        using var set = OpenSet();
        set.AppendCreateSeries(entry.Id, "cpu", tags);
        for (int i = 0; i < 5; i++)
            set.AppendWritePoint(entry.Id, 1000L + i, "usage", FieldValue.FromDouble(i));

        long checkLsn = set.NextLsn - 1; // checkpoint after last write
        set.AppendCheckpoint(checkLsn);
        set.Roll();

        for (int i = 5; i < 8; i++)
            set.AppendWritePoint(entry.Id, 2000L + i, "usage", FieldValue.FromDouble(i));
        set.Sync();

        var catalog = new SeriesCatalog();
        var result = set.ReplayWithCheckpoint(catalog);

        // 只有 checkpoint 之后的点
        Assert.Equal(checkLsn, result.CheckpointLsn);
        Assert.All(result.WritePoints, wp => Assert.True(wp.Lsn > checkLsn));
        Assert.Equal(3, result.WritePoints.Count);
    }

    [Fact]
    public void ReplayWithCheckpoint_MultipleSegments_ReturnsAllRelevantPoints()
    {
        var tags = new Dictionary<string, string> { ["host"] = "h1" };
        var preCatalog = new SeriesCatalog();
        var entry = preCatalog.GetOrAdd("sensor", tags);

        using var set = OpenSet();
        set.AppendCreateSeries(entry.Id, "sensor", tags);

        // 写入点到第一个 segment，然后 Roll
        for (int i = 0; i < 5; i++)
            set.AppendWritePoint(entry.Id, 1000L + i, "temp", FieldValue.FromDouble(i));
        set.Roll();

        // 写入到第二个 segment
        for (int i = 5; i < 10; i++)
            set.AppendWritePoint(entry.Id, 2000L + i, "temp", FieldValue.FromDouble(i));
        set.Sync();

        var catalog = new SeriesCatalog();
        var result = set.ReplayWithCheckpoint(catalog);

        Assert.Equal(10, result.WritePoints.Count);
        Assert.Equal(1, catalog.Count);
        // 验证时间顺序
        for (int i = 1; i < result.WritePoints.Count; i++)
            Assert.True(result.WritePoints[i].Lsn > result.WritePoints[i - 1].Lsn);
    }

    [Fact]
    public void ReplayWithCheckpoint_NullCatalog_ThrowsArgumentNull()
    {
        using var set = OpenSet();
        Assert.Throws<ArgumentNullException>(() => set.ReplayWithCheckpoint(null!));
    }

    // ── 崩溃恢复 ─────────────────────────────────────────────────────────────

    [Fact]
    public void CrashRecovery_SyncedDataSurvivesReopen()
    {
        // 模拟：写入并 Sync，然后 Dispose（正常关闭路径）
        // 重新打开后所有数据应 replay 出来
        int recordCount = 20;
        long lastLsn;
        using (var set = WalSegmentSet.Open(_tempDir, bufferSize: 4 * 1024))
        {
            for (int i = 0; i < recordCount; i++)
                set.AppendWritePoint(1UL, 1000L + i, "v", FieldValue.FromDouble(i));
            set.Sync();
            lastLsn = set.NextLsn - 1;
        }

        // 重新打开
        using var set2 = WalSegmentSet.Open(_tempDir, bufferSize: 4 * 1024);
        var catalog = new SeriesCatalog();
        var result = set2.ReplayWithCheckpoint(catalog);

        Assert.Equal(recordCount, result.WritePoints.Count);
        Assert.Equal(lastLsn, result.LastLsn);
    }

    [Fact]
    public void CrashRecovery_TruncatedLastSegment_SurvivesReopen()
    {
        // 写入若干记录并 Sync
        using (var set = WalSegmentSet.Open(_tempDir, new WalRollingPolicy { Enabled = false }, bufferSize: 4 * 1024))
        {
            for (int i = 0; i < 10; i++)
                set.AppendWritePoint(1UL, 1000L + i, "v", FieldValue.FromDouble(i));
            set.Sync();
        }

        // 截断最后 5 字节，模拟最后一条记录损坏
        string segPath = WalSegmentLayout.SegmentPath(_tempDir, 1L);
        long len = new FileInfo(segPath).Length;
        using (var fs = new FileStream(segPath, FileMode.Open, FileAccess.Write))
        {
            fs.SetLength(len - 5);
        }

        // 重新打开应优雅地处理截断，至少前 9 条记录可读
        using var set2 = WalSegmentSet.Open(_tempDir, new WalRollingPolicy { Enabled = false }, bufferSize: 4 * 1024);
        var catalog = new SeriesCatalog();
        var result = set2.ReplayWithCheckpoint(catalog);

        Assert.True(result.WritePoints.Count >= 9);
    }

    // ── Dispose ─────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var set = OpenSet();
        set.Dispose();
        set.Dispose(); // should not throw
    }

    [Fact]
    public void Dispose_AfterDispose_ThrowsObjectDisposed()
    {
        var set = OpenSet();
        set.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            set.AppendWritePoint(1UL, 1000L, "v", FieldValue.FromDouble(1.0)));
    }

    // ── WalRollingPolicy ─────────────────────────────────────────────────────

    [Fact]
    public void WalRollingPolicy_Default_HasExpectedValues()
    {
        var policy = WalRollingPolicy.Default;
        Assert.True(policy.Enabled);
        Assert.Equal(64L * 1024 * 1024, policy.MaxBytesPerSegment);
        Assert.Equal(1_000_000L, policy.MaxRecordsPerSegment);
    }
}
