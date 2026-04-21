using TSLite.Catalog;
using TSLite.Engine;
using TSLite.Model;
using TSLite.Query;
using TSLite.Query.Functions;
using TSLite.Sql.Ast;
using TSLite.Storage.Format;

namespace TSLite.Sql.Execution;

/// <summary>
/// <c>SELECT</c> 语句执行的内部辅助：处理投影分类、原始模式行构建、聚合模式桶合并。
/// 公共入口仍是 <see cref="SqlExecutor.ExecuteSelect"/>。
/// </summary>
internal static class SelectExecutor
{
    public static SelectExecutionResult Execute(Tsdb tsdb, SelectStatement statement)
    {
        var schema = tsdb.Measurements.TryGet(statement.Measurement)
            ?? throw new InvalidOperationException(
                $"Measurement '{statement.Measurement}' 不存在；请先执行 CREATE MEASUREMENT。");

        var where = WhereClauseDecomposer.Decompose(statement.Where, schema);
        var matchedSeries = tsdb.Catalog.Find(statement.Measurement, where.TagFilter);

        // 分类投影
        var classified = ClassifyProjections(statement.Projections, schema);

        bool hasAggregate = classified.Any(p => p.Kind == ProjectionKind.Aggregate);
        bool hasNonAggregate = classified.Any(p => p.Kind != ProjectionKind.Aggregate);

        if (hasAggregate && hasNonAggregate)
            throw new InvalidOperationException(
                "SELECT 中不允许同时出现聚合函数与非聚合列（v1 不支持 GROUP BY 列）。");

        if (statement.GroupByTime is not null && !hasAggregate)
            throw new InvalidOperationException(
                "GROUP BY time(...) 仅在聚合查询中有效。");

        if (hasAggregate)
            return ExecuteAggregate(tsdb, schema, classified, matchedSeries, where, statement.GroupByTime);

        return ExecuteRaw(tsdb, schema, classified, matchedSeries, where);
    }

    // ── 投影分类 ───────────────────────────────────────────────────────────

    private enum ProjectionKind
    {
        Time,
        Tag,
        Field,
        Aggregate,
    }

    private sealed record Projection(
        string ColumnName,
        ProjectionKind Kind,
        MeasurementColumn? Column,
        FunctionCallExpression? Function);

    private static IReadOnlyList<Projection> ClassifyProjections(
        IReadOnlyList<SelectItem> items,
        MeasurementSchema schema)
    {
        var result = new List<Projection>(items.Count);
        foreach (var item in items)
        {
            switch (item.Expression)
            {
                case StarExpression:
                    if (item.Alias is not null)
                        throw new InvalidOperationException("'*' 不允许带 alias。");
                    // 展开为 time + 所有 tag 列 + 所有 field 列
                    result.Add(new Projection("time", ProjectionKind.Time, null, null));
                    foreach (var col in schema.Columns)
                        result.Add(new Projection(
                            col.Name,
                            col.Role == MeasurementColumnRole.Tag ? ProjectionKind.Tag : ProjectionKind.Field,
                            col, null));
                    break;

                case IdentifierExpression id:
                    result.Add(BuildIdentifierProjection(id.Name, item.Alias, schema));
                    break;

                case FunctionCallExpression fn:
                    if (!FunctionRegistry.TryGetAggregate(fn.Name, out _))
                        throw new InvalidOperationException(
                            $"未知函数 '{fn.Name}'；v1 仅支持 count/sum/avg/min/max/first/last。");
                    var aggColumnName = item.Alias ?? FormatFunctionColumnName(fn);
                    result.Add(new Projection(aggColumnName, ProjectionKind.Aggregate, null, fn));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"不支持的投影表达式类型 '{item.Expression.GetType().Name}'。");
            }
        }
        return result;
    }

    private static Projection BuildIdentifierProjection(string name, string? alias, MeasurementSchema schema)
    {
        if (string.Equals(name, "time", StringComparison.OrdinalIgnoreCase))
            return new Projection(alias ?? "time", ProjectionKind.Time, null, null);

        var col = schema.TryGetColumn(name)
            ?? throw new InvalidOperationException(
                $"SELECT 中引用了未知列 '{name}'。");
        var kind = col.Role == MeasurementColumnRole.Tag ? ProjectionKind.Tag : ProjectionKind.Field;
        return new Projection(alias ?? name, kind, col, null);
    }

    private static string FormatFunctionColumnName(FunctionCallExpression fn)
    {
        if (fn.IsStar) return $"{fn.Name.ToLowerInvariant()}(*)";
        if (fn.Arguments.Count == 1 && fn.Arguments[0] is IdentifierExpression id)
            return $"{fn.Name.ToLowerInvariant()}({id.Name})";
        return fn.Name.ToLowerInvariant();
    }

    // ── 原始模式 ───────────────────────────────────────────────────────────

    private static SelectExecutionResult ExecuteRaw(
        Tsdb tsdb,
        MeasurementSchema schema,
        IReadOnlyList<Projection> projections,
        IReadOnlyList<SeriesEntry> matchedSeries,
        WhereClause where)
    {
        // 收集 raw 模式中所有需要查询的 field 列
        var fieldCols = projections
            .Where(p => p.Kind == ProjectionKind.Field)
            .Select(p => p.Column!.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var rows = new List<IReadOnlyList<object?>>();

        foreach (var series in matchedSeries)
        {
            // 每个 series 内：以所有目标 field 列时间戳的并集作为行集合（外连接）。
            // 缺失字段输出 null。若没有 field 投影，则用 schema 第一个 field 的时间戳作为时间轴。
            var fieldData = new Dictionary<string, IReadOnlyList<DataPoint>>(StringComparer.Ordinal);
            if (fieldCols.Count == 0)
            {
                var probeField = schema.FieldColumns.First().Name;
                fieldData[probeField] = QueryPoints(tsdb, series.Id, probeField, where.TimeRange);
            }
            else
            {
                foreach (var fname in fieldCols)
                    fieldData[fname] = QueryPoints(tsdb, series.Id, fname, where.TimeRange);
            }

            // 时间戳并集
            var timestamps = new SortedSet<long>();
            foreach (var (_, list) in fieldData)
                foreach (var dp in list) timestamps.Add(dp.Timestamp);
            if (timestamps.Count == 0) continue;

            // 每个 field 的 ts→value 字典（按需）
            var fieldLookups = new Dictionary<string, Dictionary<long, FieldValue>>(StringComparer.Ordinal);
            foreach (var fname in fieldCols)
            {
                var dict = new Dictionary<long, FieldValue>(fieldData[fname].Count);
                foreach (var dp in fieldData[fname]) dict[dp.Timestamp] = dp.Value;
                fieldLookups[fname] = dict;
            }

            foreach (var ts in timestamps)
            {
                var row = new object?[projections.Count];
                for (int i = 0; i < projections.Count; i++)
                {
                    var p = projections[i];
                    row[i] = p.Kind switch
                    {
                        ProjectionKind.Time => ts,
                        ProjectionKind.Tag => series.Tags.TryGetValue(p.Column!.Name, out var tagVal) ? tagVal : null,
                        ProjectionKind.Field => fieldLookups[p.Column!.Name].TryGetValue(ts, out var v)
                            ? UnboxFieldValue(v)
                            : null,
                        _ => throw new InvalidOperationException("内部错误：不应在 raw 模式出现聚合投影。"),
                    };
                }
                rows.Add(row);
            }
        }

        var columnNames = projections.Select(p => p.ColumnName).ToList();
        return new SelectExecutionResult(columnNames, rows);
    }

    private static IReadOnlyList<DataPoint> QueryPoints(Tsdb tsdb, ulong seriesId, string fieldName, TimeRange range)
    {
        var query = new PointQuery(seriesId, fieldName, range);
        return tsdb.Query.Execute(query).ToList();
    }

    private static object UnboxFieldValue(FieldValue v) => v.Type switch
    {
        FieldType.Float64 => v.AsDouble(),
        FieldType.Int64 => v.AsLong(),
        FieldType.Boolean => v.AsBool(),
        FieldType.String => v.AsString(),
        _ => throw new InvalidOperationException($"不支持的 FieldType {v.Type}。"),
    };

    // ── 聚合模式 ───────────────────────────────────────────────────────────

    private static SelectExecutionResult ExecuteAggregate(
        Tsdb tsdb,
        MeasurementSchema schema,
        IReadOnlyList<Projection> projections,
        IReadOnlyList<SeriesEntry> matchedSeries,
        WhereClause where,
        TimeBucketSpec? groupByTime)
    {
        long bucketSizeMs = groupByTime?.BucketSizeMs ?? 0;

        // 解析每个聚合投影的 (aggregator, fieldName)
        var aggSpecs = projections.Select(p =>
        {
            var fn = p.Function!;
            var (agg, field) = ResolveAggregate(fn, schema);
            // first/last 多 series 暂不支持
            if ((agg == Aggregator.First || agg == Aggregator.Last) && matchedSeries.Count > 1)
                throw new InvalidOperationException(
                    $"{agg} 聚合在多 series 场景下尚未支持（v1）；请用 WHERE 过滤到单一 series。");
            return new AggSpec(p.ColumnName, agg, field);
        }).ToList();

        // 为每个 (bucketStart, aggSpec) 累积 (count, sum, min, max, first, last) 并按 bucket 排序输出。
        // 桶按 bucketSize 切分；bucketSize <= 0 时全局单桶。
        var bucketAccumulators = new SortedDictionary<long, BucketState[]>();

        for (int specIdx = 0; specIdx < aggSpecs.Count; specIdx++)
        {
            var spec = aggSpecs[specIdx];
            // count(*) 时跨所有 field 列累加
            var fields = spec.IsCountStar ? schema.FieldColumns.Select(c => c.Name).ToList() : [spec.FieldName!];

            foreach (var series in matchedSeries)
            {
                foreach (var fname in fields)
                {
                    var col = schema.TryGetColumn(fname);
                    // count(*) 时跨所有 field 列累加，但跳过 String（语义上不参与数值统计）
                    if (spec.IsCountStar && col is not null && col.DataType == FieldType.String)
                        continue;

                    foreach (var dp in tsdb.Query.Execute(new PointQuery(series.Id, fname, where.TimeRange)))
                    {
                        long bucketStart = bucketSizeMs > 0
                            ? TimeBucket.Floor(dp.Timestamp, bucketSizeMs)
                            : long.MinValue;

                        if (!bucketAccumulators.TryGetValue(bucketStart, out var states))
                        {
                            states = new BucketState[aggSpecs.Count];
                            for (int k = 0; k < states.Length; k++) states[k] = BucketState.Empty;
                            bucketAccumulators[bucketStart] = states;
                        }

                        // count 不需要数值；其他聚合需要
                        double value = spec.Aggregator == Aggregator.Count
                            ? 0.0
                            : FieldValueToDouble(dp.Value, col);
                        states[specIdx] = states[specIdx].Update(dp.Timestamp, value);
                    }
                }
            }
        }

        var rows = new List<IReadOnlyList<object?>>(bucketAccumulators.Count);
        if (bucketAccumulators.Count == 0)
        {
            // 空数据：聚合查询返回 0 行（与 QueryEngine 行为一致）。
            // 例外：count(*) / count(field) 通常约定返回 0；此处选择"空数据 → 空表"以保持一致。
        }

        foreach (var (_, states) in bucketAccumulators)
        {
            var row = new object?[aggSpecs.Count];
            for (int i = 0; i < aggSpecs.Count; i++)
            {
                var spec = aggSpecs[i];
                var st = states[i];
                row[i] = ComputeAggregateValue(spec.Aggregator, st);
            }
            rows.Add(row);
        }

        var columnNames = projections.Select(p => p.ColumnName).ToList();
        return new SelectExecutionResult(columnNames, rows);
    }

    private static double FieldValueToDouble(FieldValue v, MeasurementColumn? col)
    {
        if (v.TryGetNumeric(out var d)) return d;
        throw new InvalidOperationException(
            $"聚合不支持 String 字段（列 '{col?.Name ?? "?"}'）。");
    }

    private static object ComputeAggregateValue(Aggregator agg, BucketState st) => agg switch
    {
        Aggregator.Count => (object)st.Count,
        Aggregator.Sum => st.Sum,
        Aggregator.Min => st.Count == 0 ? 0.0 : st.Min,
        Aggregator.Max => st.Count == 0 ? 0.0 : st.Max,
        Aggregator.Avg => st.Count == 0 ? 0.0 : st.Sum / st.Count,
        Aggregator.First => st.Count == 0 ? 0.0 : st.FirstValue,
        Aggregator.Last => st.Count == 0 ? 0.0 : st.LastValue,
        _ => throw new InvalidOperationException($"不支持的聚合 {agg}。"),
    };

    private static (Aggregator Agg, string? FieldName) ResolveAggregate(
        FunctionCallExpression fn,
        MeasurementSchema schema)
    {
        if (!FunctionRegistry.TryGetAggregate(fn.Name, out var aggregate)
            || aggregate.LegacyAggregator is not { } legacyAggregator)
        {
            throw new InvalidOperationException($"未知聚合函数 '{fn.Name}'。");
        }

        var fieldName = aggregate.ResolveFieldName(fn, schema);
        return (legacyAggregator, fieldName);
    }

    private sealed record AggSpec(string ColumnName, Aggregator Aggregator, string? FieldName)
    {
        public bool IsCountStar => Aggregator == Aggregator.Count && FieldName is null;
    }

    private readonly record struct BucketState(
        long Count,
        double Sum,
        double Min,
        double Max,
        long FirstTimestamp,
        double FirstValue,
        long LastTimestamp,
        double LastValue)
    {
        public static BucketState Empty => new(0, 0, double.PositiveInfinity, double.NegativeInfinity, long.MaxValue, 0, long.MinValue, 0);

        public BucketState Update(long timestamp, double value)
        {
            return new BucketState(
                Count + 1,
                Sum + value,
                value < Min ? value : Min,
                value > Max ? value : Max,
                timestamp < FirstTimestamp ? timestamp : FirstTimestamp,
                timestamp < FirstTimestamp ? value : FirstValue,
                timestamp > LastTimestamp ? timestamp : LastTimestamp,
                timestamp > LastTimestamp ? value : LastValue);
        }
    }
}
