using SonnetDB.Engine;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Segments;

public sealed class SegmentAggregateSketchSidecarTests : IDisposable
{
    private readonly string _tempDir;

    public SegmentAggregateSketchSidecarTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sndb-aidx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void WriteFrom_WithNumericBlock_CreatesAggregateSketchSidecar()
    {
        const ulong SeriesId = 0xA11CEUL;
        const string FieldName = "usage";

        var mt = new MemTable();
        long lsn = 1L;
        for (int i = 0; i < 100; i++)
        {
            double value = i % 10;
            mt.Append(SeriesId, i + 1L, FieldName, FieldValue.FromDouble(value), lsn++);
        }

        string path = Path.Combine(_tempDir, "aggregate.SDBSEG");
        var writer = new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false });
        writer.WriteFrom(mt, segmentId: 1L, path);

        Assert.True(File.Exists(TsdbPaths.AggregateIndexPathForSegment(path)));

        using var reader = SegmentReader.Open(path);
        var block = Assert.Single(reader.FindBySeriesAndField(SeriesId, FieldName));
        Assert.False(reader.AggregateSketchOffsetsLoaded);

        Assert.True(reader.TryGetAggregateSketch(block, out var sketch));
        Assert.True(reader.AggregateSketchOffsetsLoaded);
        Assert.Equal(block.Index, sketch.BlockIndex);
        Assert.Equal(block.Crc32, sketch.BlockCrc32);
        Assert.Equal(100L, sketch.ValueCount);
        Assert.NotNull(sketch.TDigest);
        Assert.NotNull(sketch.HyperLogLog);
        Assert.InRange(sketch.TDigest!.Quantile(0.95d), 8d, 9d);
        Assert.InRange(sketch.HyperLogLog!.Estimate(), 8L, 12L);
    }

    [Fact]
    public void TryGetAggregateSketch_WithoutSidecar_ReturnsFalseAndKeepsSegmentReadable()
    {
        const ulong SeriesId = 0xBEEFUL;
        const string FieldName = "usage";

        var mt = new MemTable();
        long lsn = 1L;
        for (int i = 0; i < 10; i++)
            mt.Append(SeriesId, 1_000L + i, FieldName, FieldValue.FromLong(i), lsn++);

        string path = Path.Combine(_tempDir, "missing-sidecar.SDBSEG");
        var writer = new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false });
        writer.WriteFrom(mt, segmentId: 2L, path);
        File.Delete(TsdbPaths.AggregateIndexPathForSegment(path));

        using var reader = SegmentReader.Open(path);
        var block = Assert.Single(reader.FindBySeriesAndField(SeriesId, FieldName));

        Assert.False(reader.TryGetAggregateSketch(block, out _));
        Assert.True(reader.AggregateSketchOffsetsLoaded);
        Assert.Equal(10, reader.DecodeBlock(block).Length);
    }
}
