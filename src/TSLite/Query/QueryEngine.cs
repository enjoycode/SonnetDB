using TSLite.Catalog;
using TSLite.Engine;
using TSLite.Memory;
using TSLite.Model;
using TSLite.Storage.Format;
using TSLite.Storage.Segments;

namespace TSLite.Query;

/// <summary>
/// 查询执行器：合并 MemTable 与 SegmentManager 的候选 Block，对外提供原始点查询、聚合查询。
/// <para>
/// 线程安全：内部不持锁，所有数据源（MemTable / SegmentManager / Catalog）自身保证只读并发。
/// </para>
/// <para>
/// 查询路径只读：绝不修改 MemTable / Segment / Catalog 的任何数据。
/// </para>
/// </summary>
public sealed class QueryEngine
{
    private readonly MemTable _memTable;
    private readonly SegmentManager _segments;
    private readonly SeriesCatalog _catalog;

    /// <summary>
    /// 初始化 <see cref="QueryEngine"/> 实例。
    /// </summary>
    /// <param name="memTable">内存层数据源。</param>
    /// <param name="segments">段集合与索引快照管理器。</param>
    /// <param name="catalog">序列目录。</param>
    /// <exception cref="ArgumentNullException">任意参数为 null 时抛出。</exception>
    public QueryEngine(MemTable memTable, SegmentManager segments, SeriesCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(memTable);
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(catalog);

        _memTable = memTable;
        _segments = segments;
        _catalog = catalog;
    }

    /// <summary>
    /// 原始点查询；按时间戳升序流式返回指定 (series, field) 在时间范围内的数据点。
    /// </summary>
    /// <param name="query">点查询参数。</param>
    /// <returns>按时间戳升序排列的 <see cref="DataPoint"/> 序列。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> 为 null 时抛出。</exception>
    /// <exception cref="InvalidOperationException">MemTable 与 Segment 中同 (series, field) 的 FieldType 不一致时抛出。</exception>
    public IEnumerable<DataPoint> Execute(PointQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        long from = query.Range.FromInclusive;
        long to = query.Range.ToInclusive;

        // 1. 从 MemTable 取切片
        var key = new SeriesFieldKey(query.SeriesId, query.FieldName);
        var bucket = _memTable.TryGet(in key);
        ReadOnlyMemory<DataPoint>? memSlice = bucket?.SnapshotRange(from, to);
        FieldType? memFieldType = bucket?.FieldType;

        // 2. 从 SegmentManager 取候选 Block 引用
        var candidates = _segments.Index.LookupCandidates(
            query.SeriesId, query.FieldName, from, to);

        // 构建当次查询用的 SegmentId → SegmentReader 快照（轻量映射，每次查询时重建）
        var readers = BuildReaderMap(_segments.Readers);

        // 3. 解码每个候选 Block
        var segmentSlices = new List<DataPoint[]>(candidates.Count);
        foreach (var blockRef in candidates)
        {
            // FieldType 校验
            if (memFieldType.HasValue && memFieldType.Value != blockRef.FieldType)
                throw new InvalidOperationException(
                    $"FieldType mismatch across MemTable and Segment for series {query.SeriesId:X16}/{query.FieldName}: " +
                    $"MemTable={memFieldType.Value}, Segment(id={blockRef.SegmentId})={blockRef.FieldType}。");

            if (!readers.TryGetValue(blockRef.SegmentId, out var reader))
            {
                // 段文件不在当前 Readers 快照中（极端情况：Readers 快照比 Index 快照旧），跳过
                continue;
            }

            var slice = reader.DecodeBlockRange(blockRef.Descriptor, from, to);
            if (slice.Length > 0)
                segmentSlices.Add(slice);
        }

        // 4. N 路有序合并
        var merged = BlockSourceMerger.Merge(memSlice, segmentSlices);

        // 5. 应用 Limit
        if (query.Limit.HasValue)
        {
            int limit = query.Limit.Value;
            int count = 0;
            foreach (var dp in merged)
            {
                if (count >= limit)
                    yield break;
                yield return dp;
                count++;
            }
        }
        else
        {
            foreach (var dp in merged)
                yield return dp;
        }
    }

    /// <summary>
    /// 聚合查询；按 BucketStart 升序返回聚合桶。
    /// <para>
    /// 支持字段类型：Float64 / Int64 / Boolean（Bool 走数值聚合，true=1, false=0）。
    /// String 字段会抛出 <see cref="NotSupportedException"/>。
    /// </para>
    /// <para>
    /// BucketSizeMs &lt;= 0 时全局单桶聚合；&gt; 0 时按 <see cref="TimeBucket.Floor"/> 分桶。
    /// 空桶不输出。
    /// </para>
    /// </summary>
    /// <param name="query">聚合查询参数。</param>
    /// <returns>按 BucketStart 升序排列的 <see cref="AggregateBucket"/> 序列（空数据集返回空序列）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> 为 null 时抛出。</exception>
    /// <exception cref="NotSupportedException">字段类型为 String 时抛出。</exception>
    public IEnumerable<AggregateBucket> Execute(AggregateQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        // 用 PointQuery 取原始点流（利用已实现的合并逻辑）
        var pointQuery = new PointQuery(query.SeriesId, query.FieldName, query.Range);
        var points = Execute(pointQuery);

        // 检查 FieldType（先从 MemTable 或 Segment 中取得，若数据为空则无法校验，直接返回）
        // 字段类型校验在第一条点时进行
        bool fieldTypeChecked = false;

        if (query.BucketSizeMs <= 0)
        {
            // ── 全局单桶聚合 ────────────────────────────────────────────────────
            long bucketStart = query.Range.FromInclusive == long.MinValue
                ? long.MinValue  // 延迟到第一条点确定
                : query.Range.FromInclusive;
            long bucketEnd = query.Range.ToInclusive == long.MaxValue
                ? long.MaxValue
                : query.Range.ToInclusive + 1;

            bool firstPoint = true;
            long count = 0;
            double sum = 0, min = double.PositiveInfinity, max = double.NegativeInfinity;
            double firstValue = 0, lastValue = 0;

            foreach (var dp in points)
            {
                if (!fieldTypeChecked)
                {
                    ThrowIfString(dp.Value.Type);
                    fieldTypeChecked = true;
                }

                if (firstPoint && query.Range.FromInclusive == long.MinValue)
                    bucketStart = dp.Timestamp;

                double v = ToDouble(dp.Value);
                count++;
                sum += v;
                if (v < min) min = v;
                if (v > max) max = v;
                if (firstPoint) { firstValue = v; firstPoint = false; }
                lastValue = v;
            }

            if (count == 0)
                yield break;

            if (query.Range.ToInclusive == long.MaxValue)
                bucketEnd = lastValue == 0 ? bucketStart + 1 : long.MaxValue; // 无意义，用合理值

            // 重新确定 bucketEnd（全局单桶时终点 = ToInclusive+1 或 MaxValue）
            bucketEnd = query.Range.ToInclusive < long.MaxValue
                ? query.Range.ToInclusive + 1
                : long.MaxValue;

            yield return new AggregateBucket(
                bucketStart, bucketEnd, count,
                ComputeValue(query.Aggregator, count, sum, min, max, firstValue, lastValue));
        }
        else
        {
            // ── 桶聚合（GROUP BY time(BucketSizeMs)）─────────────────────────────
            long bucketSizeMs = query.BucketSizeMs;
            long currentBucketStart = long.MinValue;
            long currentBucketEnd = long.MinValue;
            long count = 0;
            double sum = 0, min = double.PositiveInfinity, max = double.NegativeInfinity;
            double firstValue = 0, lastValue = 0;
            bool hasCurrent = false;

            foreach (var dp in points)
            {
                if (!fieldTypeChecked)
                {
                    ThrowIfString(dp.Value.Type);
                    fieldTypeChecked = true;
                }

                long bucketStart = TimeBucket.Floor(dp.Timestamp, bucketSizeMs);
                long bucketEnd = bucketStart + bucketSizeMs;

                if (!hasCurrent)
                {
                    // 初始化第一个桶
                    currentBucketStart = bucketStart;
                    currentBucketEnd = bucketEnd;
                    hasCurrent = true;
                }
                else if (bucketStart != currentBucketStart)
                {
                    // 桶切换：emit 当前桶
                    yield return new AggregateBucket(
                        currentBucketStart, currentBucketEnd, count,
                        ComputeValue(query.Aggregator, count, sum, min, max, firstValue, lastValue));

                    // 重置当前桶状态
                    currentBucketStart = bucketStart;
                    currentBucketEnd = bucketEnd;
                    count = 0;
                    sum = 0;
                    min = double.PositiveInfinity;
                    max = double.NegativeInfinity;
                    firstValue = 0;
                    lastValue = 0;
                }

                double v = ToDouble(dp.Value);
                if (count == 0) firstValue = v;
                lastValue = v;
                count++;
                sum += v;
                if (v < min) min = v;
                if (v > max) max = v;
            }

            // emit 最后一个桶
            if (hasCurrent && count > 0)
            {
                yield return new AggregateBucket(
                    currentBucketStart, currentBucketEnd, count,
                    ComputeValue(query.Aggregator, count, sum, min, max, firstValue, lastValue));
            }
        }
    }

    /// <summary>
    /// 批量聚合：对一组 series 做相同的聚合查询（field / range / aggregator / bucketSizeMs 共享）。
    /// </summary>
    /// <param name="seriesIds">目标序列 ID 列表。</param>
    /// <param name="fieldName">目标字段名称。</param>
    /// <param name="range">查询时间范围。</param>
    /// <param name="aggregator">聚合函数类型。</param>
    /// <param name="bucketSizeMs">桶大小（毫秒）；&lt;= 0 表示全局单桶聚合。</param>
    /// <returns>以 SeriesId 为键的聚合结果字典（各 series 的桶列表）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="seriesIds"/> 或 <paramref name="fieldName"/> 为 null 时抛出。</exception>
    public IReadOnlyDictionary<ulong, IReadOnlyList<AggregateBucket>> ExecuteMany(
        IReadOnlyList<ulong> seriesIds,
        string fieldName,
        TimeRange range,
        Aggregator aggregator,
        long bucketSizeMs = 0)
    {
        ArgumentNullException.ThrowIfNull(seriesIds);
        ArgumentNullException.ThrowIfNull(fieldName);

        var result = new Dictionary<ulong, IReadOnlyList<AggregateBucket>>(seriesIds.Count);

        foreach (var seriesId in seriesIds)
        {
            var q = new AggregateQuery(seriesId, fieldName, range, aggregator, bucketSizeMs);
            var buckets = Execute(q).ToList();
            result[seriesId] = buckets.AsReadOnly();
        }

        return result;
    }

    // ── 私有辅助 ──────────────────────────────────────────────────────────────

    /// <summary>构建 SegmentId → SegmentReader 的轻量映射（每次查询时重建，不长期缓存）。</summary>
    private static Dictionary<long, SegmentReader> BuildReaderMap(IReadOnlyList<SegmentReader> readers)
    {
        var map = new Dictionary<long, SegmentReader>(readers.Count);
        foreach (var r in readers)
            map[r.Header.SegmentId] = r;
        return map;
    }

    /// <summary>将 <see cref="FieldValue"/> 转换为 double，用于数值聚合。</summary>
    private static double ToDouble(FieldValue value)
    {
        return value.Type switch
        {
            FieldType.Float64 => value.AsDouble(),
            FieldType.Int64 => (double)value.AsLong(),
            FieldType.Boolean => value.AsBool() ? 1.0 : 0.0,
            _ => throw new NotSupportedException(
                $"字段类型 {value.Type} 不支持数值聚合。仅支持 Float64 / Int64 / Boolean。"),
        };
    }

    /// <summary>若字段类型为 String，则抛出 <see cref="NotSupportedException"/>。</summary>
    private static void ThrowIfString(FieldType fieldType)
    {
        if (fieldType == FieldType.String)
            throw new NotSupportedException(
                "String 字段不支持聚合查询。仅支持 Float64 / Int64 / Boolean 字段。");
    }

    /// <summary>根据聚合函数类型计算最终值。</summary>
    private static double ComputeValue(
        Aggregator aggregator,
        long count,
        double sum,
        double min,
        double max,
        double firstValue,
        double lastValue)
    {
        return aggregator switch
        {
            Aggregator.Count => (double)count,
            Aggregator.Sum => sum,
            Aggregator.Min => min,
            Aggregator.Max => max,
            Aggregator.Avg => count == 0 ? 0.0 : sum / count,
            Aggregator.First => firstValue,
            Aggregator.Last => lastValue,
            Aggregator.None => 0.0,
            _ => throw new ArgumentOutOfRangeException(nameof(aggregator), aggregator, null),
        };
    }
}
