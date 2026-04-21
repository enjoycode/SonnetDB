using System.Buffers.Binary;
using System.Runtime.InteropServices;
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
    private readonly TombstoneTable? _tombstones;

    /// <summary>
    /// 初始化 <see cref="QueryEngine"/> 实例。
    /// </summary>
    /// <param name="memTable">内存层数据源。</param>
    /// <param name="segments">段集合与索引快照管理器。</param>
    /// <param name="catalog">序列目录。</param>
    /// <param name="tombstones">可选的墓碑集合，用于查询时过滤被删除的数据点；为 null 时不过滤。</param>
    /// <exception cref="ArgumentNullException"><paramref name="memTable"/>、<paramref name="segments"/> 或 <paramref name="catalog"/> 为 null 时抛出。</exception>
    public QueryEngine(MemTable memTable, SegmentManager segments, SeriesCatalog catalog, TombstoneTable? tombstones = null)
    {
        ArgumentNullException.ThrowIfNull(memTable);
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(catalog);

        _memTable = memTable;
        _segments = segments;
        _catalog = catalog;
        _tombstones = tombstones;
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

        // 5. 应用墓碑过滤
        IEnumerable<DataPoint> filtered = merged;
        if (_tombstones != null)
        {
            var tombstoneList = _tombstones.GetForSeriesField(query.SeriesId, query.FieldName);
            if (tombstoneList.Count > 0)
                filtered = merged.Where(p => !IsCoveredByTombstones(p.Timestamp, tombstoneList));
        }

        // 6. 应用 Limit
        if (query.Limit.HasValue)
        {
            int limit = query.Limit.Value;
            int count = 0;
            foreach (var dp in filtered)
            {
                if (count >= limit)
                    yield break;
                yield return dp;
                count++;
            }
        }
        else
        {
            foreach (var dp in filtered)
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

        if (ShouldUsePointAggregatePath(query))
            return ExecuteAggregateViaPoints(query);

        return ExecuteAggregateFast(query);
    }

    private IEnumerable<AggregateBucket> ExecuteAggregateViaPoints(AggregateQuery query)
    {
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
                {
                    bucketStart = dp.Timestamp;
                    firstPoint = false;
                }

                double v = ToDouble(dp.Value);
                count++;
                sum += v;
                if (v < min) min = v;
                if (v > max) max = v;
                if (count == 1) firstValue = v;
                lastValue = v;
            }

            if (count == 0)
                yield break;

            // 确定 bucketEnd（全局单桶时终点 = ToInclusive+1 或 MaxValue）
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

    private IReadOnlyList<AggregateBucket> ExecuteAggregateFast(AggregateQuery query)
    {
        long from = query.Range.FromInclusive;
        long to = query.Range.ToInclusive;

        var key = new SeriesFieldKey(query.SeriesId, query.FieldName);
        var bucket = _memTable.TryGet(in key);
        ReadOnlyMemory<DataPoint>? memSlice = bucket?.SnapshotRange(from, to);
        FieldType? memFieldType = bucket?.FieldType;

        var candidates = _segments.Index.LookupCandidates(
            query.SeriesId, query.FieldName, from, to);
        var readers = BuildReaderMap(_segments.Readers);

        if (query.BucketSizeMs <= 0)
        {
            long bucketStart = query.Range.FromInclusive == long.MinValue
                ? long.MaxValue
                : query.Range.FromInclusive;
            long bucketEnd = query.Range.ToInclusive < long.MaxValue
                ? query.Range.ToInclusive + 1
                : long.MaxValue;
            var state = new AggregateState(bucketStart, bucketEnd);
            bool useObservedStart = query.Range.FromInclusive == long.MinValue;

            AddDecodedPointsToGlobal(memSlice, ref state, useObservedStart);
            AddSegmentBlocksToGlobal(
                candidates, readers, memFieldType, query.Range, query.Aggregator, ref state, useObservedStart);

            if (!state.HasData)
                return Array.Empty<AggregateBucket>();

            return new[] { state.ToBucket(query.Aggregator) };
        }

        var buckets = new Dictionary<long, AggregateState>();
        AddDecodedPointsToBuckets(memSlice, query.BucketSizeMs, buckets);
        AddSegmentBlocksToBuckets(
            candidates, readers, memFieldType, query.Range, query.BucketSizeMs, query.Aggregator, buckets);

        if (buckets.Count == 0)
            return Array.Empty<AggregateBucket>();

        var bucketStarts = buckets.Keys.ToArray();
        Array.Sort(bucketStarts);

        var result = new List<AggregateBucket>(bucketStarts.Length);
        foreach (long bucketStart in bucketStarts)
            result.Add(buckets[bucketStart].ToBucket(query.Aggregator));

        return result;
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

    private bool ShouldUsePointAggregatePath(AggregateQuery query)
    {
        if (query.Aggregator is Aggregator.First or Aggregator.Last)
            return true;

        if (_tombstones == null)
            return false;

        var tombstoneList = _tombstones.GetForSeriesField(query.SeriesId, query.FieldName);
        return tombstoneList.Count > 0;
    }

    private static void AddDecodedPointsToGlobal(
        ReadOnlyMemory<DataPoint>? points,
        ref AggregateState state,
        bool useObservedStart)
    {
        if (!points.HasValue)
            return;

        AddDecodedPointsToGlobal(points.Value.Span, ref state, useObservedStart);
    }

    private static void AddDecodedPointsToGlobal(
        ReadOnlySpan<DataPoint> points,
        ref AggregateState state,
        bool useObservedStart)
    {
        for (int i = 0; i < points.Length; i++)
        {
            var point = points[i];
            state.Add(point.Timestamp, ToDouble(point.Value), useObservedStart);
        }
    }

    private static void AddDecodedPointsToBuckets(
        ReadOnlyMemory<DataPoint>? points,
        long bucketSizeMs,
        Dictionary<long, AggregateState> buckets)
    {
        if (!points.HasValue)
            return;

        AddDecodedPointsToBuckets(points.Value.Span, bucketSizeMs, buckets);
    }

    private static void AddDecodedPointsToBuckets(
        ReadOnlySpan<DataPoint> points,
        long bucketSizeMs,
        Dictionary<long, AggregateState> buckets)
    {
        for (int i = 0; i < points.Length; i++)
        {
            var point = points[i];
            AddValueToBucket(buckets, bucketSizeMs, point.Timestamp, ToDouble(point.Value));
        }
    }

    private static void AddSegmentBlocksToGlobal(
        IReadOnlyList<SegmentBlockRef> candidates,
        Dictionary<long, SegmentReader> readers,
        FieldType? memFieldType,
        TimeRange range,
        Aggregator aggregator,
        ref AggregateState state,
        bool useObservedStart)
    {
        foreach (var blockRef in candidates)
        {
            ThrowIfFieldTypeMismatch(memFieldType, blockRef);

            if (!readers.TryGetValue(blockRef.SegmentId, out var reader))
                continue;

            if (CanUseAggregateMetadata(blockRef.Descriptor, range, aggregator))
            {
                state.AddMetadataBlock(blockRef.Descriptor, useObservedStart);
                continue;
            }

            var data = reader.ReadBlock(blockRef.Descriptor);
            AddBlockToGlobal(
                blockRef.Descriptor,
                data.TimestampPayload,
                data.ValuePayload,
                range,
                ref state,
                useObservedStart);
        }
    }

    private static void AddSegmentBlocksToBuckets(
        IReadOnlyList<SegmentBlockRef> candidates,
        Dictionary<long, SegmentReader> readers,
        FieldType? memFieldType,
        TimeRange range,
        long bucketSizeMs,
        Aggregator aggregator,
        Dictionary<long, AggregateState> buckets)
    {
        foreach (var blockRef in candidates)
        {
            ThrowIfFieldTypeMismatch(memFieldType, blockRef);

            if (!readers.TryGetValue(blockRef.SegmentId, out var reader))
                continue;

            // 快路径：block 完整落入查询范围且整体落在同一个桶内时，直接合并元数据。
            if (TryUseAggregateMetadataForBucket(
                    blockRef.Descriptor, range, bucketSizeMs, aggregator,
                    out long bucketStart, out long bucketEndExclusive))
            {
                ref var state = ref CollectionsMarshal.GetValueRefOrAddDefault(
                    buckets, bucketStart, out bool exists);

                if (!exists)
                    state = new AggregateState(bucketStart, bucketEndExclusive);

                state.AddMetadataBlock(blockRef.Descriptor, useObservedStart: false);
                continue;
            }

            var data = reader.ReadBlock(blockRef.Descriptor);
            AddBlockToBuckets(
                blockRef.Descriptor,
                data.TimestampPayload,
                data.ValuePayload,
                range,
                bucketSizeMs,
                buckets);
        }
    }

    private static void AddBlockToGlobal(
        in BlockDescriptor descriptor,
        ReadOnlySpan<byte> tsPayload,
        ReadOnlySpan<byte> valPayload,
        TimeRange range,
        ref AggregateState state,
        bool useObservedStart)
    {
        if (CanAggregateRawBlock(descriptor))
        {
            int start = LowerBoundRaw(tsPayload, descriptor.Count, range.FromInclusive);
            int end = UpperBoundRaw(tsPayload, descriptor.Count, range.ToInclusive);
            if (start >= end)
                return;

            ThrowIfString(descriptor.FieldType);
            for (int i = start; i < end; i++)
            {
                long timestamp = ReadRawTimestamp(tsPayload, i);
                double value = ReadRawNumericValue(descriptor.FieldType, valPayload, i);
                state.Add(timestamp, value, useObservedStart);
            }
            return;
        }

        var points = BlockDecoder.DecodeRange(
            descriptor,
            tsPayload,
            valPayload,
            range.FromInclusive,
            range.ToInclusive);
        AddDecodedPointsToGlobal(points.AsSpan(), ref state, useObservedStart);
    }

    private static void AddBlockToBuckets(
        in BlockDescriptor descriptor,
        ReadOnlySpan<byte> tsPayload,
        ReadOnlySpan<byte> valPayload,
        TimeRange range,
        long bucketSizeMs,
        Dictionary<long, AggregateState> buckets)
    {
        if (CanAggregateRawBlock(descriptor))
        {
            int start = LowerBoundRaw(tsPayload, descriptor.Count, range.FromInclusive);
            int end = UpperBoundRaw(tsPayload, descriptor.Count, range.ToInclusive);
            if (start >= end)
                return;

            ThrowIfString(descriptor.FieldType);
            for (int i = start; i < end; i++)
            {
                long timestamp = ReadRawTimestamp(tsPayload, i);
                double value = ReadRawNumericValue(descriptor.FieldType, valPayload, i);
                AddValueToBucket(buckets, bucketSizeMs, timestamp, value);
            }
            return;
        }

        var points = BlockDecoder.DecodeRange(
            descriptor,
            tsPayload,
            valPayload,
            range.FromInclusive,
            range.ToInclusive);
        AddDecodedPointsToBuckets(points.AsSpan(), bucketSizeMs, buckets);
    }

    private static bool CanUseAggregateMetadata(
        in BlockDescriptor descriptor,
        TimeRange range,
        Aggregator aggregator)
    {
        if (descriptor.MinTimestamp < range.FromInclusive
            || descriptor.MaxTimestamp > range.ToInclusive)
            return false;

        return aggregator switch
        {
            // Count 始终可用：descriptor.Count 总是有效的。
            Aggregator.Count => true,
            // Sum / Avg 依赖 sum 元数据。
            Aggregator.Sum or Aggregator.Avg => descriptor.HasAggregateSumCount,
            // Min / Max 依赖无损 min/max 元数据。
            Aggregator.Min or Aggregator.Max => descriptor.HasAggregateMinMax,
            _ => false,
        };
    }

    /// <summary>
    /// 桶聚合快路径判定：仅当 block 完整落入查询范围、整体落在同一个桶内、且元数据满足聚合函数要求时返回 <c>true</c>。
    /// </summary>
    private static bool TryUseAggregateMetadataForBucket(
        in BlockDescriptor descriptor,
        TimeRange range,
        long bucketSizeMs,
        Aggregator aggregator,
        out long bucketStart,
        out long bucketEndExclusive)
    {
        bucketStart = 0;
        bucketEndExclusive = 0;

        if (!CanUseAggregateMetadata(descriptor, range, aggregator))
            return false;

        long startBucket = TimeBucket.Floor(descriptor.MinTimestamp, bucketSizeMs);
        long endBucket = TimeBucket.Floor(descriptor.MaxTimestamp, bucketSizeMs);
        if (startBucket != endBucket)
            return false;

        bucketStart = startBucket;
        bucketEndExclusive = startBucket + bucketSizeMs;
        return true;
    }

    private static void AddValueToBucket(
        Dictionary<long, AggregateState> buckets,
        long bucketSizeMs,
        long timestamp,
        double value)
    {
        long bucketStart = TimeBucket.Floor(timestamp, bucketSizeMs);
        ref var state = ref CollectionsMarshal.GetValueRefOrAddDefault(
            buckets,
            bucketStart,
            out bool exists);

        if (!exists)
            state = new AggregateState(bucketStart, bucketStart + bucketSizeMs);

        state.Add(timestamp, value, useObservedStart: false);
    }

    private static bool CanAggregateRawBlock(in BlockDescriptor descriptor)
    {
        return (descriptor.TimestampEncoding & BlockEncoding.DeltaTimestamp) == 0
            && (descriptor.ValueEncoding & BlockEncoding.DeltaValue) == 0;
    }

    private static double ReadRawNumericValue(
        FieldType fieldType,
        ReadOnlySpan<byte> valPayload,
        int index)
    {
        return fieldType switch
        {
            FieldType.Float64 => BinaryPrimitives.ReadDoubleLittleEndian(valPayload.Slice(index * 8, 8)),
            FieldType.Int64 => (double)BinaryPrimitives.ReadInt64LittleEndian(valPayload.Slice(index * 8, 8)),
            FieldType.Boolean => valPayload[index] != 0 ? 1.0 : 0.0,
            _ => throw new NotSupportedException(
                $"字段类型 {fieldType} 不支持数值聚合。仅支持 Float64 / Int64 / Boolean 字段。"),
        };
    }

    private static int LowerBoundRaw(ReadOnlySpan<byte> tsPayload, int count, long value)
    {
        int lo = 0, hi = count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (ReadRawTimestamp(tsPayload, mid) < value)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    private static int UpperBoundRaw(ReadOnlySpan<byte> tsPayload, int count, long value)
    {
        int lo = 0, hi = count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (ReadRawTimestamp(tsPayload, mid) <= value)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    private static long ReadRawTimestamp(ReadOnlySpan<byte> tsPayload, int index)
        => BinaryPrimitives.ReadInt64LittleEndian(tsPayload.Slice(index * 8, 8));

    private static void ThrowIfFieldTypeMismatch(FieldType? memFieldType, SegmentBlockRef blockRef)
    {
        if (memFieldType.HasValue && memFieldType.Value != blockRef.FieldType)
        {
            throw new InvalidOperationException(
                $"FieldType mismatch across MemTable and Segment for series {blockRef.SeriesId:X16}/{blockRef.FieldName}: " +
                $"MemTable={memFieldType.Value}, Segment(id={blockRef.SegmentId})={blockRef.FieldType}。");
        }
    }

    private struct AggregateState
    {
        public long BucketStart;
        public long BucketEndExclusive;
        public long Count;
        public double Sum;
        public double Min;
        public double Max;
        public double FirstValue;
        public double LastValue;

        public AggregateState(long bucketStart, long bucketEndExclusive)
        {
            BucketStart = bucketStart;
            BucketEndExclusive = bucketEndExclusive;
            Count = 0;
            Sum = 0;
            Min = double.PositiveInfinity;
            Max = double.NegativeInfinity;
            FirstValue = 0;
            LastValue = 0;
        }

        public readonly bool HasData => Count > 0;

        public void Add(long timestamp, double value, bool useObservedStart)
        {
            if (useObservedStart && timestamp < BucketStart)
                BucketStart = timestamp;

            if (Count == 0)
                FirstValue = value;

            LastValue = value;
            Count++;
            Sum += value;
            if (value < Min) Min = value;
            if (value > Max) Max = value;
        }

        public void AddMetadataBlock(in BlockDescriptor descriptor, bool useObservedStart)
        {
            if (useObservedStart && descriptor.MinTimestamp < BucketStart)
                BucketStart = descriptor.MinTimestamp;

            // First/Last 不会走元数据快路径（ShouldUsePointAggregatePath 已强制走点路径），
            // 因此这里仅在两个标记集都满足时维护 First/Last 的“最佳近似”，否则保持不变。
            if (Count == 0 && descriptor.HasAggregateMinMax)
                FirstValue = descriptor.AggregateMin;

            if (descriptor.HasAggregateMinMax)
                LastValue = descriptor.AggregateMax;

            Count += descriptor.Count;

            if (descriptor.HasAggregateSumCount)
                Sum += descriptor.AggregateSum;

            if (descriptor.HasAggregateMinMax)
            {
                if (descriptor.AggregateMin < Min) Min = descriptor.AggregateMin;
                if (descriptor.AggregateMax > Max) Max = descriptor.AggregateMax;
            }
        }

        public readonly AggregateBucket ToBucket(Aggregator aggregator)
        {
            return new AggregateBucket(
                BucketStart,
                BucketEndExclusive,
                Count,
                ComputeValue(aggregator, Count, Sum, Min, Max, FirstValue, LastValue));
        }
    }

    /// <summary>构建 SegmentId → SegmentReader 的轻量映射（每次查询时重建，不长期缓存）。</summary>
    private static Dictionary<long, SegmentReader> BuildReaderMap(IReadOnlyList<SegmentReader> readers)
    {
        var map = new Dictionary<long, SegmentReader>(readers.Count);
        foreach (var r in readers)
            map[r.Header.SegmentId] = r;
        return map;
    }

    /// <summary>
    /// 判定 <paramref name="timestamp"/> 是否被 <paramref name="tombstones"/> 列表中的任意墓碑覆盖。
    /// 对小集合（≤ 4 个）线性扫描；超过 4 个时仍线性扫描（v1 简化，通常墓碑数量很少）。
    /// </summary>
    private static bool IsCoveredByTombstones(long timestamp, IReadOnlyList<Tombstone> tombstones)
    {
        foreach (var tomb in tombstones)
        {
            if (timestamp >= tomb.FromTimestamp && timestamp <= tomb.ToTimestamp)
                return true;
        }
        return false;
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
