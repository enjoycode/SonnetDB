using TSLite.Engine;
using TSLite.Memory;
using TSLite.Model;
using TSLite.Query;
using TSLite.Storage.Segments;
using Xunit;

namespace TSLite.Tests.Query;

/// <summary>
/// <see cref="QueryEngine"/> 与 <see cref="TombstoneTable"/> 结合的删除过滤测试。
/// </summary>
public sealed class QueryEngineDeleteTests : IDisposable
{
    private readonly string _tempDir;

    public QueryEngineDeleteTests()
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
            FlushPolicy = new MemTableFlushPolicy { MaxPoints = 10_000_000, MaxBytes = 256 * 1024 * 1024 },
            SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
            SyncWalOnEveryWrite = false,
        };

    private static Point MakePoint(long ts, double value, string field = "usage") =>
        Point.Create("cpu", ts,
            new Dictionary<string, string> { ["host"] = "h1" },
            new Dictionary<string, FieldValue> { [field] = FieldValue.FromDouble(value) });

    [Fact]
    public void PointQuery_WithTombstone_FiltersCorrectly()
    {
        using var db = Tsdb.Open(MakeOptions());

        for (int i = 0; i < 100; i++)
            db.Write(MakePoint(i, i));

        var entry = db.Catalog.Snapshot().First();
        ulong seriesId = entry.Id;

        db.Delete(seriesId, "usage", 20L, 39L); // 20 points

        var q = new PointQuery(seriesId, "usage", TimeRange.All);
        var points = db.Query.Execute(q).ToList();

        Assert.Equal(80, points.Count);
        Assert.All(points, p => Assert.False(p.Timestamp >= 20 && p.Timestamp <= 39));
    }

    [Fact]
    public void PointQuery_WithNoTombstones_ReturnsAll()
    {
        using var db = Tsdb.Open(MakeOptions());

        for (int i = 0; i < 100; i++)
            db.Write(MakePoint(i, i));

        var entry = db.Catalog.Snapshot().First();
        var q = new PointQuery(entry.Id, "usage", TimeRange.All);
        var points = db.Query.Execute(q).ToList();

        Assert.Equal(100, points.Count);
    }

    [Fact]
    public void AggregateQuery_Sum_CorrectAfterDelete()
    {
        using var db = Tsdb.Open(MakeOptions());

        // Write points 1..100
        for (int i = 1; i <= 100; i++)
            db.Write(MakePoint(i, i));

        var entry = db.Catalog.Snapshot().First();
        ulong seriesId = entry.Id;

        // Delete 91..100 (sum of 91+92+...+100 = 955)
        db.Delete(seriesId, "usage", 91L, 100L);

        var q = new AggregateQuery(seriesId, "usage", TimeRange.All, Aggregator.Sum);
        var result = db.Query.Execute(q).ToList();

        Assert.Single(result);
        // Sum of 1..90 = 90*91/2 = 4095
        Assert.Equal(4095.0, result[0].Value, 1.0);
    }

    [Fact]
    public void AggregateQuery_Count_CorrectAfterDelete()
    {
        using var db = Tsdb.Open(MakeOptions());

        for (int i = 0; i < 100; i++)
            db.Write(MakePoint(i, i));

        var entry = db.Catalog.Snapshot().First();
        ulong seriesId = entry.Id;

        db.Delete(seriesId, "usage", 0L, 49L); // 50 points

        var q = new AggregateQuery(seriesId, "usage", TimeRange.All, Aggregator.Count);
        var result = db.Query.Execute(q).ToList();

        Assert.Single(result);
        Assert.Equal(50L, result[0].Count);
    }

    [Fact]
    public void AggregateQuery_Min_CorrectAfterDelete()
    {
        using var db = Tsdb.Open(MakeOptions());

        for (int i = 1; i <= 100; i++)
            db.Write(MakePoint(i, i));

        var entry = db.Catalog.Snapshot().First();
        ulong seriesId = entry.Id;

        // Delete lowest values
        db.Delete(seriesId, "usage", 1L, 10L);

        var q = new AggregateQuery(seriesId, "usage", TimeRange.All, Aggregator.Min);
        var result = db.Query.Execute(q).ToList();

        Assert.Single(result);
        Assert.Equal(11.0, result[0].Value, 0.5);
    }

    [Fact]
    public void AggregateQuery_Max_CorrectAfterDelete()
    {
        using var db = Tsdb.Open(MakeOptions());

        for (int i = 1; i <= 100; i++)
            db.Write(MakePoint(i, i));

        var entry = db.Catalog.Snapshot().First();
        ulong seriesId = entry.Id;

        // Delete highest values
        db.Delete(seriesId, "usage", 91L, 100L);

        var q = new AggregateQuery(seriesId, "usage", TimeRange.All, Aggregator.Max);
        var result = db.Query.Execute(q).ToList();

        Assert.Single(result);
        Assert.Equal(90.0, result[0].Value, 0.5);
    }

    [Fact]
    public void PointQuery_MultipleFields_OnlyDeletedFieldFiltered()
    {
        using var db = Tsdb.Open(MakeOptions());

        for (int i = 0; i < 100; i++)
        {
            db.Write(MakePoint(i, i, "temp"));
            db.Write(MakePoint(i, i * 2, "pressure"));
        }

        var entry = db.Catalog.Snapshot().First();
        ulong seriesId = entry.Id;

        // Delete only "temp" field range
        db.Delete(seriesId, "temp", 50L, 99L);

        var tempQ = new PointQuery(seriesId, "temp", TimeRange.All);
        var pressureQ = new PointQuery(seriesId, "pressure", TimeRange.All);

        var tempPoints = db.Query.Execute(tempQ).ToList();
        var pressurePoints = db.Query.Execute(pressureQ).ToList();

        Assert.Equal(50, tempPoints.Count);
        Assert.Equal(100, pressurePoints.Count); // unaffected
    }

    [Fact]
    public void PointQuery_WithLimitAndTombstone_LimitAppliedAfterFilter()
    {
        using var db = Tsdb.Open(MakeOptions());

        for (int i = 0; i < 100; i++)
            db.Write(MakePoint(i, i));

        var entry = db.Catalog.Snapshot().First();
        ulong seriesId = entry.Id;

        // Delete first 50 points
        db.Delete(seriesId, "usage", 0L, 49L);

        // Request 10 points (should come from ts 50..59 after filter)
        var q = new PointQuery(seriesId, "usage", TimeRange.All, Limit: 10);
        var points = db.Query.Execute(q).ToList();

        Assert.Equal(10, points.Count);
        Assert.All(points, p => Assert.True(p.Timestamp >= 50));
    }
}

