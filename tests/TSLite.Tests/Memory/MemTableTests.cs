using TSLite.Memory;
using TSLite.Model;
using TSLite.Storage.Format;
using Xunit;

namespace TSLite.Tests.Memory;

/// <summary>
/// <see cref="MemTable"/> 单元测试。
/// </summary>
public sealed class MemTableTests
{
    [Fact]
    public void Append_SameSeriesId_DifferentFieldName_CreatesSeparateBuckets()
    {
        var table = new MemTable();
        const ulong sid = 1UL;

        table.Append(sid, 1000L, "cpu", FieldValue.FromDouble(50.0), 1L);
        table.Append(sid, 2000L, "mem", FieldValue.FromLong(4096L), 2L);

        Assert.Equal(2, table.SeriesCount);
        Assert.Equal(2L, table.PointCount);
    }

    [Fact]
    public void Append_SameKey_DifferentFieldType_ThrowsInvalidOperationException()
    {
        var table = new MemTable();
        const ulong sid = 42UL;

        table.Append(sid, 1000L, "v", FieldValue.FromDouble(1.0), 1L);

        Assert.Throws<InvalidOperationException>(() =>
            table.Append(sid, 2000L, "v", FieldValue.FromLong(2L), 2L));
    }

    [Fact]
    public void GetBySeries_ReturnsBucketsOrderedByFieldName()
    {
        var table = new MemTable();
        const ulong sid = 10UL;

        table.Append(sid, 1000L, "z_field", FieldValue.FromDouble(1.0), 1L);
        table.Append(sid, 1000L, "a_field", FieldValue.FromDouble(2.0), 2L);
        table.Append(sid, 1000L, "m_field", FieldValue.FromDouble(3.0), 3L);

        var buckets = table.GetBySeries(sid);

        Assert.Equal(3, buckets.Count);
        Assert.Equal("a_field", buckets[0].Key.FieldName);
        Assert.Equal("m_field", buckets[1].Key.FieldName);
        Assert.Equal("z_field", buckets[2].Key.FieldName);
    }

    [Fact]
    public void GetBySeries_WrongSeriesId_ReturnsEmpty()
    {
        var table = new MemTable();

        table.Append(1UL, 1000L, "v", FieldValue.FromDouble(1.0), 1L);

        var buckets = table.GetBySeries(99UL);
        Assert.Empty(buckets);
    }

    [Fact]
    public void SnapshotAll_CountMatchesSeriesCount()
    {
        var table = new MemTable();

        table.Append(1UL, 1000L, "v", FieldValue.FromDouble(1.0), 1L);
        table.Append(2UL, 1000L, "v", FieldValue.FromDouble(2.0), 2L);
        table.Append(3UL, 1000L, "v", FieldValue.FromDouble(3.0), 3L);

        var snap = table.SnapshotAll();
        Assert.Equal(table.SeriesCount, snap.Count);
        Assert.Equal(3, snap.Count);
    }

    [Fact]
    public void ReplayFrom_PopulatesCorrectly()
    {
        var records = new[]
        {
            new TSLite.Wal.WritePointRecord(1L, 0L, 1UL, 1000L, "cpu", FieldValue.FromDouble(10.0)),
            new TSLite.Wal.WritePointRecord(2L, 0L, 1UL, 2000L, "cpu", FieldValue.FromDouble(20.0)),
            new TSLite.Wal.WritePointRecord(3L, 0L, 2UL, 1000L, "mem", FieldValue.FromLong(1024L)),
        };

        var table = new MemTable();
        int replayed = table.ReplayFrom(records);

        Assert.Equal(3, replayed);
        Assert.Equal(3L, table.PointCount);
        Assert.Equal(2, table.SeriesCount);
        Assert.Equal(1L, table.FirstLsn);
        Assert.Equal(3L, table.LastLsn);
    }

    [Fact]
    public void ReplayFrom_EmptyInput_ReturnsZero()
    {
        var table = new MemTable();
        int replayed = table.ReplayFrom([]);

        Assert.Equal(0, replayed);
        Assert.Equal(0, table.SeriesCount);
    }

    [Fact]
    public void Reset_ClearsAllData()
    {
        var table = new MemTable();

        table.Append(1UL, 1000L, "v", FieldValue.FromDouble(1.0), 1L);
        table.Append(2UL, 2000L, "v", FieldValue.FromDouble(2.0), 2L);

        table.Reset();

        Assert.Equal(0, table.SeriesCount);
        Assert.Equal(0L, table.PointCount);
        Assert.Equal(long.MinValue, table.FirstLsn);
        Assert.Equal(long.MinValue, table.LastLsn);
    }

    [Fact]
    public void TryGet_ExistingKey_ReturnsBucket()
    {
        var table = new MemTable();
        const ulong sid = 5UL;

        table.Append(sid, 1000L, "temp", FieldValue.FromDouble(25.0), 1L);

        var key = new SeriesFieldKey(sid, "temp");
        var bucket = table.TryGet(in key);

        Assert.NotNull(bucket);
        Assert.Equal(1, bucket.Count);
    }

    [Fact]
    public void TryGet_MissingKey_ReturnsNull()
    {
        var table = new MemTable();
        var key = new SeriesFieldKey(999UL, "nonexistent");

        Assert.Null(table.TryGet(in key));
    }

    [Fact]
    public void FirstLsn_LastLsn_TrackWalLsns()
    {
        var table = new MemTable();

        table.Append(1UL, 1000L, "v", FieldValue.FromDouble(1.0), 100L);
        table.Append(1UL, 2000L, "v", FieldValue.FromDouble(2.0), 200L);
        table.Append(1UL, 3000L, "v", FieldValue.FromDouble(3.0), 300L);

        Assert.Equal(100L, table.FirstLsn);
        Assert.Equal(300L, table.LastLsn);
    }

    [Fact]
    public void ShouldFlush_MaxBytes_ReturnsTrue()
    {
        var table = new MemTable();
        var policy = new MemTableFlushPolicy { MaxBytes = 0, MaxPoints = long.MaxValue, MaxAge = TimeSpan.MaxValue };

        table.Append(1UL, 1000L, "v", FieldValue.FromDouble(1.0), 1L);

        Assert.True(table.ShouldFlush(policy));
    }

    [Fact]
    public void ShouldFlush_MaxPoints_ReturnsTrue()
    {
        var table = new MemTable();
        var policy = new MemTableFlushPolicy { MaxBytes = long.MaxValue, MaxPoints = 1, MaxAge = TimeSpan.MaxValue };

        table.Append(1UL, 1000L, "v", FieldValue.FromDouble(1.0), 1L);
        table.Append(1UL, 2000L, "v", FieldValue.FromDouble(2.0), 2L);

        Assert.True(table.ShouldFlush(policy));
    }

    [Fact]
    public void ShouldFlush_MaxAge_ReturnsTrue()
    {
        var table = new MemTable();
        var policy = new MemTableFlushPolicy
        {
            MaxBytes = long.MaxValue,
            MaxPoints = long.MaxValue,
            MaxAge = TimeSpan.Zero
        };

        Assert.True(table.ShouldFlush(policy));
    }

    [Fact]
    public void ShouldFlush_NoThresholdMet_ReturnsFalse()
    {
        var table = new MemTable();
        var policy = new MemTableFlushPolicy
        {
            MaxBytes = long.MaxValue,
            MaxPoints = long.MaxValue,
            MaxAge = TimeSpan.MaxValue
        };

        table.Append(1UL, 1000L, "v", FieldValue.FromDouble(1.0), 1L);

        Assert.False(table.ShouldFlush(policy));
    }

    [Fact]
    public void MinMaxTimestamp_AggregatesAcrossBuckets()
    {
        var table = new MemTable();

        table.Append(1UL, 5000L, "a", FieldValue.FromDouble(1.0), 1L);
        table.Append(2UL, 1000L, "b", FieldValue.FromDouble(2.0), 2L);
        table.Append(3UL, 9000L, "c", FieldValue.FromDouble(3.0), 3L);

        Assert.Equal(1000L, table.MinTimestamp);
        Assert.Equal(9000L, table.MaxTimestamp);
    }

    [Fact]
    public void EstimatedBytes_SumOfAllBuckets()
    {
        var table = new MemTable();

        // 2 points in Float64 bucket = 2 * 16 = 32 bytes
        table.Append(1UL, 1000L, "v", FieldValue.FromDouble(1.0), 1L);
        table.Append(1UL, 2000L, "v", FieldValue.FromDouble(2.0), 2L);

        Assert.Equal(32L, table.EstimatedBytes);
    }
}
