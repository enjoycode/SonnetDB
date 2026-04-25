using SonnetDB.Model;
using SonnetDB.Storage.Format;

namespace SonnetDB.Memory;

/// <summary>
/// MemTable 的单桶：固定 (SeriesId, FieldName, FieldType) 下的有序 DataPoint 列表。
/// 内部以追加为主，有序性通过"插入时合并 + 一次性排序快照"双路径保证。
/// 单调递增写入时保持 <c>_isSorted = true</c>，出现乱序时在 <see cref="Snapshot"/> 时懒排序。
/// </summary>
public sealed class MemTableSeries
{
    private readonly object _sync = new();
    private readonly List<DataPoint> _points = [];
    private bool _isSorted = true;
    private long _lastTimestamp = long.MinValue;
    private long _minTimestamp = long.MaxValue;
    private long _maxTimestamp = long.MinValue;
    // 数值字段的运行期增量聚合（Float64/Int64/Boolean），用于范围全覆盖时的查询快路径。
    // 非数值字段始终保持 _hasNumericAggregates = false。
    private bool _hasNumericAggregates;
    private double _numericSum;
    private double _numericMin = double.PositiveInfinity;
    private double _numericMax = double.NegativeInfinity;

    /// <summary>该桶的 (SeriesId, FieldName) 复合键。</summary>
    public SeriesFieldKey Key { get; }

    /// <summary>该桶的字段类型（创建时固定）。</summary>
    public FieldType FieldType { get; }

    /// <summary>桶内当前数据点数量。</summary>
    public int Count
    {
        get
        {
            lock (_sync)
                return _points.Count;
        }
    }

    /// <summary>桶内最小时间戳；若桶为空则返回 <see cref="long.MaxValue"/>。</summary>
    public long MinTimestamp
    {
        get
        {
            lock (_sync)
                return _minTimestamp;
        }
    }

    /// <summary>桶内最大时间戳；若桶为空则返回 <see cref="long.MinValue"/>。</summary>
    public long MaxTimestamp
    {
        get
        {
            lock (_sync)
                return _maxTimestamp;
        }
    }

    /// <summary>
    /// 估算的内存占用（字节），用于 MemTable 阈值判定。
    /// </summary>
    /// <remarks>
    /// Double/Long ≈ 16 bytes/point；Bool ≈ 9 bytes/point；
    /// String ≈ 8 + UTF-8 byte count + 8 overhead。
    /// </remarks>
    public long EstimatedBytes
    {
        get
        {
            lock (_sync)
                return ComputeEstimatedBytes();
        }
    }

    /// <summary>
    /// 创建一个新的数据点桶。
    /// </summary>
    /// <param name="key">桶的 (SeriesId, FieldName) 复合键。</param>
    /// <param name="fieldType">该桶的字段类型。</param>
    internal MemTableSeries(SeriesFieldKey key, FieldType fieldType)
    {
        Key = key;
        FieldType = fieldType;
        _hasNumericAggregates = fieldType is FieldType.Float64 or FieldType.Int64 or FieldType.Boolean;
    }

    /// <summary>
    /// 是否为数值字段（Float64 / Int64 / Boolean），仅在该字段类型下维护运行期 sum/min/max。
    /// </summary>
    public bool HasNumericAggregates => _hasNumericAggregates;

    /// <summary>
    /// 数值字段累积 Sum 快照（仅当 <see cref="HasNumericAggregates"/> 且 <see cref="Count"/> &gt; 0 时有意义）。
    /// </summary>
    public double NumericSum
    {
        get { lock (_sync) return _numericSum; }
    }

    /// <summary>
    /// 数值字段累积 Min 快照（仅当 <see cref="HasNumericAggregates"/> 且 <see cref="Count"/> &gt; 0 时有意义）。
    /// </summary>
    public double NumericMin
    {
        get { lock (_sync) return _numericMin; }
    }

    /// <summary>
    /// 数值字段累积 Max 快照（仅当 <see cref="HasNumericAggregates"/> 且 <see cref="Count"/> &gt; 0 时有意义）。
    /// </summary>
    public double NumericMax
    {
        get { lock (_sync) return _numericMax; }
    }

    /// <summary>
    /// 一次性原子读取范围全覆盖时所需的全部聚合状态：count / sum / min / max / 时间窗。
    /// 仅当 <see cref="HasNumericAggregates"/> 为 true 且 <paramref name="count"/> &gt; 0 时返回的聚合值有意义。
    /// </summary>
    /// <param name="count">桶内数据点总数。</param>
    /// <param name="minTs">桶内最小时间戳。</param>
    /// <param name="maxTs">桶内最大时间戳。</param>
    /// <param name="sum">数值累积 Sum。</param>
    /// <param name="min">数值累积 Min。</param>
    /// <param name="max">数值累积 Max。</param>
    /// <returns>是否提供有效的 sum/min/max（true 表示数值字段且非空）。</returns>
    public bool TryGetNumericAggregateSnapshot(
        out int count, out long minTs, out long maxTs,
        out double sum, out double min, out double max)
    {
        lock (_sync)
        {
            count = _points.Count;
            minTs = _minTimestamp;
            maxTs = _maxTimestamp;
            sum = _numericSum;
            min = _numericMin;
            max = _numericMax;
            return _hasNumericAggregates && count > 0;
        }
    }

    /// <summary>
    /// 追加一个数据点。线程安全（内部加锁）。
    /// </summary>
    /// <param name="timestamp">数据点时间戳（Unix 毫秒）。</param>
    /// <param name="value">字段值，类型必须与桶的 <see cref="FieldType"/> 一致。</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> 的类型与桶的 <see cref="FieldType"/> 不匹配时抛出。
    /// </exception>
    public void Append(long timestamp, FieldValue value)
    {
        if (value.Type != FieldType)
            throw new ArgumentException(
                $"FieldValue type mismatch: expected {FieldType}, got {value.Type}.", nameof(value));

        lock (_sync)
        {
            if (timestamp < _lastTimestamp)
                _isSorted = false;

            _lastTimestamp = timestamp;

            if (timestamp < _minTimestamp)
                _minTimestamp = timestamp;
            if (timestamp > _maxTimestamp)
                _maxTimestamp = timestamp;

            if (_hasNumericAggregates)
            {
                double v = FieldType switch
                {
                    FieldType.Float64 => value.AsDouble(),
                    FieldType.Int64 => value.AsLong(),
                    FieldType.Boolean => value.AsBool() ? 1.0 : 0.0,
                    _ => 0.0,
                };
                _numericSum += v;
                if (v < _numericMin) _numericMin = v;
                if (v > _numericMax) _numericMax = v;
            }

            _points.Add(new DataPoint(timestamp, value));
        }
    }

    /// <summary>
    /// 返回排序后的只读快照（按时间戳升序；同 timestamp 保留写入顺序，稳定排序）。
    /// 调用后内部缓冲不变，可被 SegmentWriter 直接消费。
    /// </summary>
    /// <returns>按时间戳升序排列的数据点只读内存。</returns>
    public ReadOnlyMemory<DataPoint> Snapshot()
    {
        lock (_sync)
        {
            if (_isSorted)
                return _points.ToArray();

            return StableSorted();
        }
    }

    /// <summary>
    /// 按时间范围 [<paramref name="fromInclusive"/>, <paramref name="toInclusive"/>] 返回切片快照（仍排序）。
    /// </summary>
    /// <param name="fromInclusive">起始时间戳（含）。</param>
    /// <param name="toInclusive">结束时间戳（含）。</param>
    /// <returns>在指定时间范围内的按时间戳升序排列的数据点只读内存。</returns>
    public ReadOnlyMemory<DataPoint> SnapshotRange(long fromInclusive, long toInclusive)
    {
        var sorted = Snapshot();
        var span = sorted.Span;

        int start = 0;
        int end = span.Length;

        // 找到 fromInclusive 的起始位置（binary search）
        int lo = 0, hi = span.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (span[mid].Timestamp < fromInclusive)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        start = lo;

        // 找到 toInclusive 的结束位置（binary search）
        lo = start;
        hi = span.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (span[mid].Timestamp <= toInclusive)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        end = lo;

        return sorted.Slice(start, end - start);
    }

    // ── 私有辅助 ──────────────────────────────────────────────────────────────

    private long ComputeEstimatedBytes()
    {
        int count = _points.Count;
        return FieldType switch
        {
            FieldType.Boolean => count * 9L,
            FieldType.String => ComputeStringBytes(),
            FieldType.Vector => ComputeVectorBytes(),
            FieldType.GeoPoint => count * 24L,
            _ => count * 16L, // Float64 / Int64
        };
    }

    private long ComputeStringBytes()
    {
        long total = 0;
        foreach (var dp in _points)
        {
            total += 8 + System.Text.Encoding.UTF8.GetByteCount(dp.Value.AsString()) + 8;
        }
        return total;
    }

    private long ComputeVectorBytes()
    {
        // 8(timestamp) + 8(ROM<float> overhead) + dim*4
        long total = 0;
        foreach (var dp in _points)
        {
            total += 16 + dp.Value.VectorDimension * 4L;
        }
        return total;
    }

    private DataPoint[] StableSorted()
    {
        // 稳定排序：通过 (index, point) 对保证同 timestamp 保留追加顺序
        var indexed = new (int Index, DataPoint Point)[_points.Count];
        for (int i = 0; i < _points.Count; i++)
            indexed[i] = (i, _points[i]);

        Array.Sort(indexed, static (a, b) =>
        {
            int cmp = a.Point.Timestamp.CompareTo(b.Point.Timestamp);
            return cmp != 0 ? cmp : a.Index.CompareTo(b.Index);
        });

        var result = new DataPoint[indexed.Length];
        for (int i = 0; i < indexed.Length; i++)
            result[i] = indexed[i].Point;

        return result;
    }
}
