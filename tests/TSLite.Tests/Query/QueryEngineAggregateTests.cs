using TSLite.Engine;
using TSLite.Memory;
using TSLite.Model;
using TSLite.Query;
using TSLite.Storage.Segments;
using Xunit;

namespace TSLite.Tests.Query;

/// <summary>
/// <see cref="QueryEngine"/> 聚合查询（<see cref="AggregateQuery"/>）的单元测试。
/// </summary>
public sealed class QueryEngineAggregateTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TsdbOptions _opts;

    public QueryEngineAggregateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _opts = new TsdbOptions
        {
            RootDirectory = _tempDir,
            WalBufferSize = 64 * 1024,
            FlushPolicy = new MemTableFlushPolicy { MaxPoints = 1_000_000, MaxBytes = 64 * 1024 * 1024 },
            SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
        };
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── 辅助方法 ──────────────────────────────────────────────────────────────

    private static Point MakePoint(string measurement, long ts, string field, FieldValue value)
        => Point.Create(measurement, ts,
            new Dictionary<string, string> { ["host"] = "h1" },
            new Dictionary<string, FieldValue> { [field] = value });

    // ── Double 全局聚合 ────────────────────────────────────────────────────

    [Fact]
    public void Execute_GlobalCount_Double_Correct()
    {
        using var db = Tsdb.Open(_opts);

        for (int i = 1; i <= 10; i++)
            db.Write(MakePoint("m", i * 100L, "v", FieldValue.FromDouble(i)));

        var entry = db.Catalog.Snapshot().First();
        var q = new AggregateQuery(entry.Id, "v", TimeRange.All, Aggregator.Count, 0);
        var result = db.Query.Execute(q).ToList();

        Assert.Single(result);
        Assert.Equal(10L, result[0].Count);
        Assert.Equal(10.0, result[0].Value, precision: 9);
    }

    [Fact]
    public void Execute_GlobalSum_Double_Correct()
    {
        using var db = Tsdb.Open(_opts);

        for (int i = 1; i <= 10; i++)
            db.Write(MakePoint("m", i * 100L, "v", FieldValue.FromDouble(i)));

        var entry = db.Catalog.Snapshot().First();
        var q = new AggregateQuery(entry.Id, "v", TimeRange.All, Aggregator.Sum, 0);
        var result = db.Query.Execute(q).ToList();

        Assert.Single(result);
        Assert.Equal(55.0, result[0].Value, precision: 9);  // 1+2+...+10 = 55
    }

    [Fact]
    public void Execute_GlobalMin_Double_Correct()
    {
        using var db = Tsdb.Open(_opts);

        foreach (var v in new double[] { 5.0, 2.0, 8.0, 1.0, 9.0 })
            db.Write(MakePoint("m", v.GetHashCode() + 1000L, "v", FieldValue.FromDouble(v)));

        var entry = db.Catalog.Snapshot().First();
        var q = new AggregateQuery(entry.Id, "v", TimeRange.All, Aggregator.Min, 0);
        var result = db.Query.Execute(q).ToList();

        Assert.Single(result);
        Assert.Equal(1.0, result[0].Value, precision: 9);
    }

    [Fact]
    public void Execute_GlobalMax_Double_Correct()
    {
        using var db = Tsdb.Open(_opts);

        foreach (var (ts, v) in new[] { (1L, 5.0), (2L, 2.0), (3L, 8.0), (4L, 1.0), (5L, 9.0) })
            db.Write(MakePoint("m", ts * 100L, "v", FieldValue.FromDouble(v)));

        var entry = db.Catalog.Snapshot().First();
        var q = new AggregateQuery(entry.Id, "v", TimeRange.All, Aggregator.Max, 0);
        var result = db.Query.Execute(q).ToList();

        Assert.Single(result);
        Assert.Equal(9.0, result[0].Value, precision: 9);
    }

    [Fact]
    public void Execute_GlobalAvg_Double_Correct()
    {
        using var db = Tsdb.Open(_opts);

        for (int i = 1; i <= 5; i++)
            db.Write(MakePoint("m", i * 100L, "v", FieldValue.FromDouble(i)));

        var entry = db.Catalog.Snapshot().First();
        var q = new AggregateQuery(entry.Id, "v", TimeRange.All, Aggregator.Avg, 0);
        var result = db.Query.Execute(q).ToList();

        Assert.Single(result);
        Assert.Equal(3.0, result[0].Value, precision: 9);  // (1+2+3+4+5)/5 = 3
    }

    // ── Long 全局聚合 ─────────────────────────────────────────────────────

    [Fact]
    public void Execute_GlobalCount_Long_Correct()
    {
        using var db = Tsdb.Open(_opts);

        for (int i = 1; i <= 5; i++)
            db.Write(MakePoint("m", i * 100L, "v", FieldValue.FromLong(i * 10L)));

        var entry = db.Catalog.Snapshot().First();
        var q = new AggregateQuery(entry.Id, "v", TimeRange.All, Aggregator.Count, 0);
        var result = db.Query.Execute(q).ToList();

        Assert.Single(result);
        Assert.Equal(5L, result[0].Count);
    }

    [Fact]
    public void Execute_GlobalSum_Long_Correct()
    {
        using var db = Tsdb.Open(_opts);

        for (int i = 1; i <= 4; i++)
            db.Write(MakePoint("m", i * 100L, "v", FieldValue.FromLong(i)));

        var entry = db.Catalog.Snapshot().First();
        var q = new AggregateQuery(entry.Id, "v", TimeRange.All, Aggregator.Sum, 0);
        var result = db.Query.Execute(q).ToList();

        Assert.Single(result);
        Assert.Equal(10.0, result[0].Value, precision: 9);  // 1+2+3+4=10
    }

    // ── Boolean 全局聚合 ──────────────────────────────────────────────────

    [Fact]
    public void Execute_GlobalSum_Bool_TrueIsOne()
    {
        using var db = Tsdb.Open(_opts);

        // 3 true, 2 false
        foreach (var (ts, v) in new[] { (1L, true), (2L, false), (3L, true), (4L, false), (5L, true) })
            db.Write(MakePoint("m", ts * 100L, "v", FieldValue.FromBool(v)));

        var entry = db.Catalog.Snapshot().First();
        var q = new AggregateQuery(entry.Id, "v", TimeRange.All, Aggregator.Sum, 0);
        var result = db.Query.Execute(q).ToList();

        Assert.Single(result);
        Assert.Equal(3.0, result[0].Value, precision: 9);  // 1+0+1+0+1=3
    }

    // ── First / Last ──────────────────────────────────────────────────────

    [Fact]
    public void Execute_GlobalFirst_ReturnsFirstPoint()
    {
        using var db = Tsdb.Open(_opts);

        // 写入乱序，应按时间戳排序后取第一个
        foreach (var (ts, v) in new[] { (300L, 30.0), (100L, 10.0), (200L, 20.0) })
            db.Write(MakePoint("m", ts, "v", FieldValue.FromDouble(v)));

        var entry = db.Catalog.Snapshot().First();
        var q = new AggregateQuery(entry.Id, "v", TimeRange.All, Aggregator.First, 0);
        var result = db.Query.Execute(q).ToList();

        Assert.Single(result);
        Assert.Equal(10.0, result[0].Value, precision: 9);  // ts=100 的 value=10.0
    }

    [Fact]
    public void Execute_GlobalLast_ReturnsLastPoint()
    {
        using var db = Tsdb.Open(_opts);

        foreach (var (ts, v) in new[] { (300L, 30.0), (100L, 10.0), (200L, 20.0) })
            db.Write(MakePoint("m", ts, "v", FieldValue.FromDouble(v)));

        var entry = db.Catalog.Snapshot().First();
        var q = new AggregateQuery(entry.Id, "v", TimeRange.All, Aggregator.Last, 0);
        var result = db.Query.Execute(q).ToList();

        Assert.Single(result);
        Assert.Equal(30.0, result[0].Value, precision: 9);  // ts=300 的 value=30.0
    }

    // ── 桶聚合 (GROUP BY time) ────────────────────────────────────────────

    [Fact]
    public void Execute_BucketAgg_ThreeBuckets_CorrectCountAndValue()
    {
        using var db = Tsdb.Open(_opts);

        // 桶大小 1000ms，写 3 个桶各 10 点
        // 桶 0: [0, 999]，桶 1: [1000, 1999]，桶 2: [2000, 2999]
        for (int bucket = 0; bucket < 3; bucket++)
            for (int j = 0; j < 10; j++)
            {
                long ts = bucket * 1000L + j * 100L;
                db.Write(MakePoint("m", ts, "v", FieldValue.FromDouble(bucket * 100.0 + j)));
            }

        var entry = db.Catalog.Snapshot().First();
        var q = new AggregateQuery(entry.Id, "v", TimeRange.All, Aggregator.Count, 1000L);
        var result = db.Query.Execute(q).ToList();

        Assert.Equal(3, result.Count);
        Assert.All(result, b => Assert.Equal(10L, b.Count));

        // 验证 BucketStart 升序
        Assert.Equal(0L, result[0].BucketStart);
        Assert.Equal(1000L, result[1].BucketStart);
        Assert.Equal(2000L, result[2].BucketStart);
    }

    [Fact]
    public void Execute_BucketAgg_SumPerBucket_Correct()
    {
        using var db = Tsdb.Open(_opts);

        // 桶 0: [0, 999] 写 v=1,2,3
        // 桶 1: [1000, 1999] 写 v=10,20,30
        foreach (var (ts, v) in new[] { (100L, 1.0), (200L, 2.0), (300L, 3.0) })
            db.Write(MakePoint("m", ts, "v", FieldValue.FromDouble(v)));
        foreach (var (ts, v) in new[] { (1100L, 10.0), (1200L, 20.0), (1300L, 30.0) })
            db.Write(MakePoint("m", ts, "v", FieldValue.FromDouble(v)));

        var entry = db.Catalog.Snapshot().First();
        var q = new AggregateQuery(entry.Id, "v", TimeRange.All, Aggregator.Sum, 1000L);
        var result = db.Query.Execute(q).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal(6.0, result[0].Value, precision: 9);   // 1+2+3
        Assert.Equal(60.0, result[1].Value, precision: 9);  // 10+20+30
    }

    // ── 跨 MemTable + 多段聚合 ────────────────────────────────────────────

    [Fact]
    public void Execute_CrossFlushAndMemTable_AggregatesCorrectly()
    {
        using var db = Tsdb.Open(_opts);

        // 写 50 点，Flush
        for (int i = 0; i < 50; i++)
            db.Write(MakePoint("m", i * 10L, "v", FieldValue.FromDouble(i)));
        db.FlushNow();

        // 再写 50 点（不 Flush，在 MemTable 中）
        for (int i = 50; i < 100; i++)
            db.Write(MakePoint("m", i * 10L, "v", FieldValue.FromDouble(i)));

        var entry = db.Catalog.Snapshot().First();
        var q = new AggregateQuery(entry.Id, "v", TimeRange.All, Aggregator.Count, 0);
        var result = db.Query.Execute(q).ToList();

        Assert.Single(result);
        Assert.Equal(100L, result[0].Count);
    }

    // ── String 字段聚合 → 抛 NotSupportedException ───────────────────────

    [Fact]
    public void Execute_StringField_ThrowsNotSupportedException()
    {
        using var db = Tsdb.Open(_opts);

        db.Write(MakePoint("m", 100L, "s", FieldValue.FromString("hello")));

        var entry = db.Catalog.Snapshot().First();
        var q = new AggregateQuery(entry.Id, "s", TimeRange.All, Aggregator.Count, 0);

        Assert.Throws<NotSupportedException>(() => db.Query.Execute(q).ToList());
    }

    // ── 空数据集 ─────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_NoData_ReturnsEmptySequence()
    {
        using var db = Tsdb.Open(_opts);

        var q = new AggregateQuery(0xDEAD_BEEF_CAFE_BABEuL, "v", TimeRange.All, Aggregator.Count, 0);
        var result = db.Query.Execute(q).ToList();

        Assert.Empty(result);
    }

    // ── BucketEndExclusive 验证 ───────────────────────────────────────────

    [Fact]
    public void Execute_BucketAgg_BucketEndExclusive_IsCorrect()
    {
        using var db = Tsdb.Open(_opts);

        // 桶大小 500ms，写两个桶
        db.Write(MakePoint("m", 100L, "v", FieldValue.FromDouble(1.0)));
        db.Write(MakePoint("m", 600L, "v", FieldValue.FromDouble(2.0)));

        var entry = db.Catalog.Snapshot().First();
        var q = new AggregateQuery(entry.Id, "v", TimeRange.All, Aggregator.Count, 500L);
        var result = db.Query.Execute(q).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal(0L, result[0].BucketStart);
        Assert.Equal(500L, result[0].BucketEndExclusive);
        Assert.Equal(500L, result[1].BucketStart);
        Assert.Equal(1000L, result[1].BucketEndExclusive);
    }
}
