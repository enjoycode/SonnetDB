using TSLite.Engine;
using TSLite.Memory;
using TSLite.Model;
using TSLite.Storage.Segments;
using TSLite.Wal;
using Xunit;

namespace TSLite.Tests.Engine;

/// <summary>
/// <see cref="Tsdb"/> 写入路径的单元测试。
/// </summary>
public sealed class TsdbWriteTests : IDisposable
{
    private readonly string _tempDir;

    public TsdbWriteTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private TsdbOptions MakeOptions(MemTableFlushPolicy? flushPolicy = null) =>
        new TsdbOptions
        {
            RootDirectory = _tempDir,
            WalBufferSize = 64 * 1024,
            FlushPolicy = flushPolicy ?? new MemTableFlushPolicy { MaxPoints = 1_000_000, MaxBytes = 64 * 1024 * 1024 },
            SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
        };

    [Fact]
    public void Open_EmptyDirectory_CreatesExpectedStructure()
    {
        using var db = Tsdb.Open(MakeOptions());

        Assert.True(Directory.Exists(TsdbPaths.WalDir(_tempDir)));
        Assert.True(Directory.Exists(TsdbPaths.SegmentsDir(_tempDir)));
        // 新模型：WAL 以 segment 文件存在，wal/ 目录中应有至少一个 .tslwal 文件
        var walSegments = WalSegmentLayout.Enumerate(TsdbPaths.WalDir(_tempDir));
        Assert.NotEmpty(walSegments);
        Assert.Equal(0, db.Catalog.Count);
        Assert.Equal(0L, db.MemTable.PointCount);
    }

    [Fact]
    public void Write_SinglePoint_CatalogAndMemTableUpdated()
    {
        using var db = Tsdb.Open(MakeOptions());

        var point = Point.Create("cpu", 1000L,
            new Dictionary<string, string> { ["host"] = "srv1" },
            new Dictionary<string, FieldValue> { ["usage"] = FieldValue.FromDouble(75.0) });

        db.Write(point);

        Assert.Equal(1, db.Catalog.Count);
        Assert.Equal(1L, db.MemTable.PointCount);
        // 新模型：WAL 以 segment 文件存在
        var walSegments = WalSegmentLayout.Enumerate(TsdbPaths.WalDir(_tempDir));
        Assert.NotEmpty(walSegments);
    }

    [Fact]
    public void Write_MultiplePoints_MemTableCountsCorrect()
    {
        using var db = Tsdb.Open(MakeOptions());

        for (int i = 0; i < 10; i++)
        {
            var point = Point.Create("sensor", 1000L + i,
                new Dictionary<string, string> { ["id"] = "s1" },
                new Dictionary<string, FieldValue> { ["value"] = FieldValue.FromDouble(i) });
            db.Write(point);
        }

        Assert.Equal(10L, db.MemTable.PointCount);
        Assert.Equal(1, db.Catalog.Count);
    }

    [Fact]
    public void WriteMany_NPoints_MemTablePointCountEquals_N()
    {
        using var db = Tsdb.Open(MakeOptions());

        var points = Enumerable.Range(0, 20).Select(i => Point.Create(
            "metric", 1000L + i,
            new Dictionary<string, string> { ["host"] = "h" },
            new Dictionary<string, FieldValue> { ["v"] = FieldValue.FromDouble(i) }));

        int written = db.WriteMany(points);

        Assert.Equal(20, written);
        Assert.Equal(20L, db.MemTable.PointCount);
    }

    [Fact]
    public void FlushNow_EmptyMemTable_ReturnsNull()
    {
        using var db = Tsdb.Open(MakeOptions());
        var result = db.FlushNow();
        Assert.Null(result);
    }

    [Fact]
    public void FlushNow_AfterWrite_CreatesSegmentAndClearsMemTable()
    {
        using var db = Tsdb.Open(MakeOptions());

        for (int i = 0; i < 5; i++)
        {
            var p = Point.Create("temp", 1000L + i,
                new Dictionary<string, string> { ["loc"] = "lab" },
                new Dictionary<string, FieldValue> { ["c"] = FieldValue.FromDouble(20.0 + i) });
            db.Write(p);
        }

        Assert.Equal(5L, db.MemTable.PointCount);

        var result = db.FlushNow();

        Assert.NotNull(result);
        Assert.Equal(0L, db.MemTable.PointCount);
        Assert.Equal(0, db.MemTable.SeriesCount);

        // Segment 文件存在
        var segments = db.ListSegments();
        Assert.Single(segments);
        Assert.True(File.Exists(segments[0].Path));

        // catalog 未自动持久化（Dispose 时才保存）
        Assert.False(File.Exists(TsdbPaths.CatalogPath(_tempDir)));
    }

    [Fact]
    public void FlushNow_SegmentId_IsMonotonicallyIncreasing()
    {
        using var db = Tsdb.Open(MakeOptions());

        for (int flush = 0; flush < 3; flush++)
        {
            for (int i = 0; i < 3; i++)
            {
                var p = Point.Create("m", 1000L + flush * 100 + i,
                    new Dictionary<string, string> { ["k"] = "v" },
                    new Dictionary<string, FieldValue> { ["f"] = FieldValue.FromDouble(i) });
                db.Write(p);
            }
            db.FlushNow();
        }

        var segs = db.ListSegments();
        Assert.Equal(3, segs.Count);
        Assert.Equal(1L, segs[0].SegmentId);
        Assert.Equal(2L, segs[1].SegmentId);
        Assert.Equal(3L, segs[2].SegmentId);
    }

    [Fact]
    public void Dispose_SavesCatalog_ClosesWal()
    {
        var p = Point.Create("cpu", 1000L,
            new Dictionary<string, string> { ["host"] = "h" },
            new Dictionary<string, FieldValue> { ["v"] = FieldValue.FromDouble(1.0) });

        using (var db = Tsdb.Open(MakeOptions()))
        {
            db.Write(p);
        }

        // Catalog 应已保存
        Assert.True(File.Exists(TsdbPaths.CatalogPath(_tempDir)));
    }

    [Fact]
    public void Dispose_ThenOpen_CatalogAndSegmentsPreserved()
    {
        // First session: write + flush
        using (var db = Tsdb.Open(MakeOptions()))
        {
            for (int i = 0; i < 5; i++)
            {
                var p = Point.Create("m", 1000L + i,
                    new Dictionary<string, string> { ["k"] = "v" },
                    new Dictionary<string, FieldValue> { ["f"] = FieldValue.FromDouble(i) });
                db.Write(p);
            }
            db.FlushNow();
        }

        // Second session: verify state is preserved
        using (var db = Tsdb.Open(MakeOptions()))
        {
            Assert.Equal(1, db.Catalog.Count);

            var segs = db.ListSegments();
            Assert.Single(segs);

            // MemTable should be empty (all data flushed)
            Assert.Equal(0L, db.MemTable.PointCount);
        }
    }

    [Fact]
    public void Write_NullPoint_ThrowsArgumentNull()
    {
        using var db = Tsdb.Open(MakeOptions());
        Assert.Throws<ArgumentNullException>(() => db.Write(null!));
    }

    [Fact]
    public void ListSegments_ReturnsSegmentsInAscendingOrder()
    {
        using var db = Tsdb.Open(MakeOptions());

        // Flush 3 times
        for (int flush = 0; flush < 3; flush++)
        {
            var p = Point.Create("m", 1000L + flush,
                new Dictionary<string, string> { ["i"] = flush.ToString() },
                new Dictionary<string, FieldValue> { ["v"] = FieldValue.FromDouble(flush) });
            db.Write(p);
            db.FlushNow();
        }

        var segs = db.ListSegments();
        for (int i = 1; i < segs.Count; i++)
            Assert.True(segs[i].SegmentId > segs[i - 1].SegmentId);
    }

    [Fact]
    public void Open_WithExistingSegments_NextSegmentIdIsMaxPlusOne()
    {
        // Create fake segment files to simulate existing segments
        Directory.CreateDirectory(TsdbPaths.SegmentsDir(_tempDir));
        File.WriteAllBytes(TsdbPaths.SegmentPath(_tempDir, 3L), []);
        File.WriteAllBytes(TsdbPaths.SegmentPath(_tempDir, 7L), []);

        using var db = Tsdb.Open(MakeOptions());
        Assert.Equal(8L, db.NextSegmentId);
    }
}
