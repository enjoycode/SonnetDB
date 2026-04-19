using TSLite.Engine;
using TSLite.Memory;
using TSLite.Model;
using TSLite.Query;
using TSLite.Storage.Segments;
using Xunit;

namespace TSLite.Tests.Engine;

/// <summary>
/// 后台 Flush 集成测试：验证连续写入 5000 点后，后台线程自动产生多个 Segment，并查询正确。
/// </summary>
public sealed class BackgroundFlushIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public BackgroundFlushIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private TsdbOptions MakeOptions() =>
        new TsdbOptions
        {
            RootDirectory = _tempDir,
            WalBufferSize = 64 * 1024,
            FlushPolicy = new MemTableFlushPolicy
            {
                MaxPoints = 500,
                MaxBytes = 1024L * 1024 * 1024,
                MaxAge = TimeSpan.FromHours(24),
            },
            SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
            BackgroundFlush = new BackgroundFlushOptions
            {
                Enabled = true,
                PollInterval = TimeSpan.FromMilliseconds(100),
                ShutdownTimeout = TimeSpan.FromSeconds(15),
            },
        };

    private static Point MakePoint(long timestamp, double value) =>
        Point.Create("metrics", timestamp,
            new Dictionary<string, string> { ["host"] = "srv1" },
            new Dictionary<string, FieldValue> { ["val"] = FieldValue.FromDouble(value) });

    /// <summary>
    /// 单线程连续写入 5000 点，不主动 FlushNow，等待 ≤ 5s 后断言：
    /// - SegmentCount >= 5（宽松下界）
    /// - 查询能返回全部 5000 点（跨 segment + MemTable 残余）
    /// </summary>
    [Fact]
    public void ContinuousWrite_5000Points_AutoFlushesMultipleSegments()
    {
        const int totalPoints = 5000;

        using var db = Tsdb.Open(MakeOptions());

        for (int i = 0; i < totalPoints; i++)
            db.Write(MakePoint(1000L + i, i));

        // 等待后台 Flush（最长 5s）
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && db.Segments.SegmentCount < 5)
            Thread.Sleep(100);

        // 断言至少产生 5 个 Segment（5000/500=10，下界宽松为 5）
        Assert.True(db.Segments.SegmentCount >= 5,
            $"期望 SegmentCount >= 5，实际 {db.Segments.SegmentCount}");

        // 主动再 Flush 一次，确保残余 MemTable 数据也落盘
        db.FlushNow();

        // 查询全部数据
        var seriesId = db.Catalog.Snapshot().First().Id;
        var query = new PointQuery(seriesId, "val", new TimeRange(0, long.MaxValue));
        var points = db.Query.Execute(query).ToList();

        Assert.Equal(totalPoints, points.Count);
    }

    /// <summary>
    /// 并发写入后，等待后台 Flush 完成，重新打开数据库查询正确。
    /// </summary>
    [Fact]
    public void AfterAutoFlush_Reopen_QueryIsCorrect()
    {
        const int totalPoints = 2000;

        {
            using var db = Tsdb.Open(MakeOptions());

            for (int i = 0; i < totalPoints; i++)
                db.Write(MakePoint(1000L + i, i));

            // 等待后台线程至少触发一次 Flush
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline && db.Segments.SegmentCount < 1)
                Thread.Sleep(100);

            // 正常 Dispose（会再 Flush 剩余）
        }

        // 重新打开验证查询
        {
            using var db = Tsdb.Open(MakeOptions());

            var seriesId = db.Catalog.Snapshot().First().Id;
            var query = new PointQuery(seriesId, "val", new TimeRange(0, long.MaxValue));
            var points = db.Query.Execute(query).ToList();

            Assert.Equal(totalPoints, points.Count);
        }
    }
}
