using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Format;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace SonnetDB.Core.Tests.Memory;

/// <summary>
/// <see cref="MemTableSeries"/> 单元测试。
/// </summary>
public sealed class MemTableSeriesTests
{
    private static SeriesFieldKey MakeKey(string field = "v") =>
        new(0xABCD_1234_5678_0001UL, field);

    [Fact]
    public void Append_OrderedTimestamps_SnapshotIsOrdered()
    {
        var key = MakeKey();
        var series = new MemTableSeries(key, FieldType.Float64);

        series.Append(1000L, FieldValue.FromDouble(1.0));
        series.Append(2000L, FieldValue.FromDouble(2.0));
        series.Append(3000L, FieldValue.FromDouble(3.0));

        var snap = series.Snapshot();

        Assert.Equal(3, snap.Length);
        Assert.Equal(1000L, snap.Span[0].Timestamp);
        Assert.Equal(2000L, snap.Span[1].Timestamp);
        Assert.Equal(3000L, snap.Span[2].Timestamp);
    }

    [Fact]
    public void Append_UnorderedTimestamps_MinMaxCorrect()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.Int64);

        series.Append(5000L, FieldValue.FromLong(5));
        series.Append(1000L, FieldValue.FromLong(1));
        series.Append(9000L, FieldValue.FromLong(9));

        Assert.Equal(3, series.Count);
        Assert.Equal(1000L, series.MinTimestamp);
        Assert.Equal(9000L, series.MaxTimestamp);
    }

    [Fact]
    public void Append_UnorderedTimestamps_SnapshotStillOrdered()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.Float64);

        series.Append(3000L, FieldValue.FromDouble(3.0));
        series.Append(1000L, FieldValue.FromDouble(1.0));
        series.Append(2000L, FieldValue.FromDouble(2.0));

        var snap = series.Snapshot();

        Assert.Equal(3, snap.Length);
        Assert.Equal(1000L, snap.Span[0].Timestamp);
        Assert.Equal(2000L, snap.Span[1].Timestamp);
        Assert.Equal(3000L, snap.Span[2].Timestamp);
    }

    [Fact]
    public void Append_SameTimestamp_StableOrder()
    {
        // 同 timestamp 应保留追加顺序
        var series = new MemTableSeries(MakeKey(), FieldType.Float64);

        series.Append(1000L, FieldValue.FromDouble(1.0));
        series.Append(1000L, FieldValue.FromDouble(2.0));
        series.Append(1000L, FieldValue.FromDouble(3.0));

        var snap = series.Snapshot();

        Assert.Equal(3, snap.Length);
        Assert.Equal(1.0, snap.Span[0].Value.AsDouble());
        Assert.Equal(2.0, snap.Span[1].Value.AsDouble());
        Assert.Equal(3.0, snap.Span[2].Value.AsDouble());
    }

    [Fact]
    public void Append_WrongFieldType_ThrowsArgumentException()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.Float64);

        Assert.Throws<ArgumentException>(() =>
            series.Append(1000L, FieldValue.FromLong(42)));
    }

    [Fact]
    public void SnapshotRange_ReturnsCorrectSlice()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.Float64);

        for (int i = 1; i <= 10; i++)
            series.Append(i * 1000L, FieldValue.FromDouble(i));

        // [3000, 7000] → 5 points (3, 4, 5, 6, 7)
        var slice = series.SnapshotRange(3000L, 7000L);

        Assert.Equal(5, slice.Length);
        Assert.Equal(3000L, slice.Span[0].Timestamp);
        Assert.Equal(7000L, slice.Span[4].Timestamp);
    }

    [Fact]
    public void SnapshotRange_InclusiveBoundaries()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.Int64);

        series.Append(100L, FieldValue.FromLong(1));
        series.Append(200L, FieldValue.FromLong(2));
        series.Append(300L, FieldValue.FromLong(3));

        // exact boundaries
        var slice = series.SnapshotRange(100L, 300L);
        Assert.Equal(3, slice.Length);

        // single point
        var single = series.SnapshotRange(200L, 200L);
        Assert.Equal(1, single.Length);
        Assert.Equal(200L, single.Span[0].Timestamp);
    }

    [Fact]
    public void SnapshotRange_OutOfRange_ReturnsEmpty()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.Float64);

        series.Append(1000L, FieldValue.FromDouble(1.0));
        series.Append(2000L, FieldValue.FromDouble(2.0));

        var slice = series.SnapshotRange(5000L, 9000L);
        Assert.Equal(0, slice.Length);
    }

    [Fact]
    public void Snapshot_RepeatedQueries_ReusesCachedReadOnlySnapshot()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.Float64);
        for (int i = 0; i < 256; i++)
            series.Append(i, FieldValue.FromDouble(i));

        var snapshot = series.Snapshot();
        var range = series.SnapshotRange(10L, 20L);
        Assert.False(MemoryMarshal.TryGetArray(snapshot, out ArraySegment<DataPoint> _));
        Assert.False(MemoryMarshal.TryGetArray(range, out ArraySegment<DataPoint> _));

        long before = GC.GetAllocatedBytesForCurrentThread();
        long checksum = 0;
        for (int i = 0; i < 1_000; i++)
        {
            var snap = series.Snapshot();
            var slice = series.SnapshotRange(10L, 20L);
            checksum += snap.Length;
            checksum += slice.Length;
            checksum += snap.Span[0].Timestamp;
            checksum += slice.Span[0].Timestamp;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(checksum > 0);
        Assert.True(allocated < 1024, $"重复 Snapshot/SnapshotRange 不应分配新数组，实际分配 {allocated} bytes。");
    }

    [Fact]
    public void Snapshot_AfterAppend_InvalidatesCachedSnapshot()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.Float64);
        series.Append(1000L, FieldValue.FromDouble(1.0));
        series.Append(2000L, FieldValue.FromDouble(2.0));

        var beforeAppend = series.Snapshot();

        series.Append(3000L, FieldValue.FromDouble(3.0));
        var afterAppend = series.Snapshot();

        Assert.Equal(2, beforeAppend.Length);
        Assert.Equal(3, afterAppend.Length);
        Assert.Equal(1000L, afterAppend.Span[0].Timestamp);
        Assert.Equal(2000L, afterAppend.Span[1].Timestamp);
        Assert.Equal(3000L, afterAppend.Span[2].Timestamp);
        Assert.False(MemoryMarshal.TryGetArray(afterAppend, out ArraySegment<DataPoint> _));
    }

    [Fact]
    public void Count_UpdatesOnAppend()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.Boolean);

        Assert.Equal(0, series.Count);
        series.Append(1L, FieldValue.FromBool(true));
        Assert.Equal(1, series.Count);
        series.Append(2L, FieldValue.FromBool(false));
        Assert.Equal(2, series.Count);
    }

    [Fact]
    public void EstimatedBytes_Double_ApproximatelyCorrect()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.Float64);
        const int n = 100;

        for (int i = 0; i < n; i++)
            series.Append(i, FieldValue.FromDouble(i));

        Assert.Equal(n * 16L, series.EstimatedBytes);
    }

    [Fact]
    public void EstimatedBytes_Bool_ApproximatelyCorrect()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.Boolean);
        const int n = 50;

        for (int i = 0; i < n; i++)
            series.Append(i, FieldValue.FromBool(i % 2 == 0));

        Assert.Equal(n * 9L, series.EstimatedBytes);
    }

    [Fact]
    public void EstimatedBytes_String_IncrementalUtf8BytesMatchLegacyFormula()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.String);
        string[] values = ["", "sensor-a", "温度=二十"];

        long expected = 0;
        for (int i = 0; i < values.Length; i++)
        {
            series.Append(i, FieldValue.FromString(values[i]));
            expected += 16L + Encoding.UTF8.GetByteCount(values[i]);
            Assert.Equal(expected, series.EstimatedBytes);
        }

        Assert.Equal(values.Length, series.Count);
    }

    [Fact]
    public void EstimatedBytes_StringNull_ThrowsAndDoesNotChangeStatistics()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.String);
        series.Append(1L, FieldValue.FromString("ok"));
        long expectedBytes = 16L + Encoding.UTF8.GetByteCount("ok");

        Assert.Throws<ArgumentNullException>(() => FieldValue.FromString(null!));

        Assert.Equal(1, series.Count);
        Assert.Equal(expectedBytes, series.EstimatedBytes);
    }

    [Fact]
    public async Task ConcurrentAppend_CountIsCorrect()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.Float64);
        const int threads = 10;
        const int pointsPerThread = 1000;

        var tasks = Enumerable.Range(0, threads)
            .Select(t => Task.Run(() =>
            {
                for (int i = 0; i < pointsPerThread; i++)
                    series.Append((long)(t * 1_000_000 + i), FieldValue.FromDouble(i));
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(threads * pointsPerThread, series.Count);

        // Snapshot should be ordered
        var snap = series.Snapshot();
        Assert.Equal(threads * pointsPerThread, snap.Length);
        for (int i = 1; i < snap.Length; i++)
            Assert.True(snap.Span[i].Timestamp >= snap.Span[i - 1].Timestamp);
    }

    [Fact]
    public void MinMaxTimestamp_EmptySeries_ReturnsSentinelValues()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.Float64);

        Assert.Equal(long.MaxValue, series.MinTimestamp);
        Assert.Equal(long.MinValue, series.MaxTimestamp);
    }
}
