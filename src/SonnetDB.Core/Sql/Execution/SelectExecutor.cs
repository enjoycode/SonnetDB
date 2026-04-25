using SonnetDB.Catalog;
using SonnetDB.Engine;
using SonnetDB.Model;
using SonnetDB.Query;
using SonnetDB.Query.Functions;
using SonnetDB.Query.Functions.Forecasting;
using SonnetDB.Sql.Ast;
using SonnetDB.Storage.Format;

namespace SonnetDB.Sql.Execution;

/// <summary>
/// <c>SELECT</c> 语句执行的内部辅助：处理投影分类、原始模式行构建、聚合模式桶合并。
/// 公共入口仍是 <see cref="SqlExecutor.ExecuteSelect"/>。
/// </summary>
internal static class SelectExecutor
{
    public static SelectExecutionResult Execute(Tsdb tsdb, SelectStatement statement)
    {
        if (statement.TableValuedFunction is not null)
            return ApplyPagination(TableValuedFunctionExecutor.Execute(tsdb, statement), statement.Pagination);

        var schema = tsdb.Measurements.TryGet(statement.Measurement)
            ?? throw new InvalidOperationException(
                $"Measurement '{statement.Measurement}' 不存在；请先执行 CREATE MEASUREMENT。");

        var where = WhereClauseDecomposer.Decompose(statement.Where, schema);
        var matchedSeries = tsdb.Catalog.Find(statement.Measurement, where.TagFilter);

        // 分类投影
        var classified = ClassifyProjections(statement.Projections, schema);

        bool hasAggregate = classified.Any(p => p.Kind == ProjectionKind.Aggregate);
        bool hasNonAggregate = classified.Any(p => p.Kind != ProjectionKind.Aggregate);
        var groupByTime = ResolveGroupByTime(statement.GroupBy);

        if (hasAggregate && hasNonAggregate)
            throw new InvalidOperationException(
                "SELECT 中不允许同时出现聚合函数与非聚合列（v1 不支持 GROUP BY 列）。");

        if (groupByTime is not null && !hasAggregate)
            throw new InvalidOperationException(
                "GROUP BY time(...) 仅在聚合查询中有效。");

        SelectExecutionResult result = hasAggregate
            ? ExecuteAggregate(tsdb, schema, classified, matchedSeries, where, groupByTime)
            : ExecuteRaw(tsdb, schema, classified, matchedSeries, where);

        return ApplyPagination(result, statement.Pagination);
    }

    private static SelectExecutionResult ApplyPagination(SelectExecutionResult result, PaginationSpec? pagination)
    {
        if (pagination is null)
            return result;

        var offset = pagination.Offset;
        if (offset >= result.Rows.Count)
            return new SelectExecutionResult(result.Columns, []);

        int take = pagination.Fetch ?? (result.Rows.Count - offset);
        if (take <= 0)
            return new SelectExecutionResult(result.Columns, []);

        int actualTake = Math.Min(take, result.Rows.Count - offset);
        var slicedRows = result.Rows.Skip(offset).Take(actualTake).ToList();
        return new SelectExecutionResult(result.Columns, slicedRows);
    }

    // ── 投影分类 ───────────────────────────────────────────────────────────

    private enum ProjectionKind
    {
        Time,
        Tag,
        Field,
        Aggregate,
        Scalar,
        Window,
    }

    private sealed record Projection(
        string ColumnName,
        ProjectionKind Kind,
        MeasurementColumn? Column,
        FunctionCallExpression? Function,
        IScalarFunction? ScalarFunction = null,
        IWindowFunction? WindowFunction = null);

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
                    var kind = FunctionRegistry.GetFunctionKind(fn.Name);
                    if (kind == FunctionKind.Aggregate)
                    {
                        var aggColumnName = item.Alias ?? FormatFunctionColumnName(fn);
                        result.Add(new Projection(aggColumnName, ProjectionKind.Aggregate, null, fn));
                        break;
                    }

                    if (kind == FunctionKind.Scalar && FunctionRegistry.TryGetScalar(fn.Name, out var scalarFunction))
                    {
                        var scalarColumnName = item.Alias ?? FormatFunctionColumnName(fn);
                        result.Add(new Projection(scalarColumnName, ProjectionKind.Scalar, null, fn, scalarFunction));
                        break;
                    }

                    if (kind == FunctionKind.Window && FunctionRegistry.TryGetWindow(fn.Name, out var windowFunction))
                    {
                        var windowColumnName = item.Alias ?? FormatFunctionColumnName(fn);
                        result.Add(new Projection(
                            windowColumnName, ProjectionKind.Window, null, fn,
                            ScalarFunction: null, WindowFunction: windowFunction));
                        break;
                    }

                    if (kind == FunctionKind.TableValued)
                        throw new InvalidOperationException(
                            $"函数 '{fn.Name}' 已保留给后续里程碑，当前 SELECT 尚不支持。"
                        );

                    throw new InvalidOperationException(
                        $"未知函数 '{fn.Name}'；当前仅支持内置 aggregate/scalar 函数。"
                    );

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
        // 预先为每个窗口投影构造 evaluator（只构造一次：参数校验在此完成）。
        var windowEvaluators = new IWindowEvaluator?[projections.Count];
        for (int i = 0; i < projections.Count; i++)
        {
            if (projections[i].Kind == ProjectionKind.Window)
            {
                windowEvaluators[i] = projections[i].WindowFunction!.CreateEvaluator(
                    projections[i].Function!, schema);
            }
        }

        // 收集 raw 模式中所有需要查询的 field 列
        var fieldCols = projections
            .Where(p => p.Kind == ProjectionKind.Field)
            .Select(p => p.Column!.Name)
            .Concat(GetScalarFieldDependencies(projections))
            .Concat(windowEvaluators.OfType<IWindowEvaluator>().Select(e => e.FieldName))
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
            var timestampSet = new SortedSet<long>();
            foreach (var (_, list) in fieldData)
                foreach (var dp in list) timestampSet.Add(dp.Timestamp);
            if (timestampSet.Count == 0) continue;
            var timestamps = timestampSet.ToArray();

            // 每个 field 的 ts→value 字典（按需）
            var fieldLookups = new Dictionary<string, Dictionary<long, FieldValue>>(StringComparer.Ordinal);
            foreach (var fname in fieldCols)
            {
                var dict = new Dictionary<long, FieldValue>(fieldData[fname].Count);
                foreach (var dp in fieldData[fname]) dict[dp.Timestamp] = dp.Value;
                fieldLookups[fname] = dict;
            }

            // 为每个窗口投影预计算输出（与 timestamps 数组同长度，逐行对齐）。
            var windowOutputs = new object?[projections.Count][];
            for (int i = 0; i < projections.Count; i++)
            {
                var evaluator = windowEvaluators[i];
                if (evaluator is null) continue;

                var alignedValues = new FieldValue?[timestamps.Length];
                if (fieldLookups.TryGetValue(evaluator.FieldName, out var lookup))
                {
                    for (int row = 0; row < timestamps.Length; row++)
                    {
                        if (lookup.TryGetValue(timestamps[row], out var v))
                            alignedValues[row] = v;
                    }
                }

                windowOutputs[i] = evaluator.Compute(timestamps, alignedValues);
            }

            for (int rowIdx = 0; rowIdx < timestamps.Length; rowIdx++)
            {
                long ts = timestamps[rowIdx];
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
                        ProjectionKind.Scalar => EvaluateScalarProjection(p, ts, series, fieldLookups),
                        ProjectionKind.Window => windowOutputs[i]![rowIdx],
                        _ => throw new InvalidOperationException("内部错误：不应在 raw 模式出现聚合投影。"),
                    };
                }
                rows.Add(row);
            }
        }

        var columnNames = projections.Select(p => p.ColumnName).ToList();
        return new SelectExecutionResult(columnNames, rows);
    }

    private static IEnumerable<string> GetScalarFieldDependencies(IReadOnlyList<Projection> projections)
    {
        foreach (var projection in projections)
        {
            if (projection.Kind != ProjectionKind.Scalar || projection.Function is null)
                continue;

            foreach (var fieldName in GetScalarFieldDependencies(projection.Function))
                yield return fieldName;
        }
    }

    private static IEnumerable<string> GetScalarFieldDependencies(SqlExpression expression)
    {
        switch (expression)
        {
            case IdentifierExpression id when !string.Equals(id.Name, "time", StringComparison.OrdinalIgnoreCase):
                yield return id.Name;
                yield break;
            case FunctionCallExpression fn:
                foreach (var arg in fn.Arguments)
                foreach (var fieldName in GetScalarFieldDependencies(arg))
                    yield return fieldName;
                yield break;
            case UnaryExpression unary:
                foreach (var fieldName in GetScalarFieldDependencies(unary.Operand))
                    yield return fieldName;
                yield break;
            case BinaryExpression binary:
                foreach (var fieldName in GetScalarFieldDependencies(binary.Left))
                    yield return fieldName;
                foreach (var fieldName in GetScalarFieldDependencies(binary.Right))
                    yield return fieldName;
                yield break;
            default:
                yield break;
        }
    }

    private static object? EvaluateScalarProjection(
        Projection projection,
        long timestamp,
        SeriesEntry series,
        IReadOnlyDictionary<string, Dictionary<long, FieldValue>> fieldLookups)
    {
        var scalarFunction = projection.ScalarFunction
            ?? throw new InvalidOperationException("内部错误：缺少标量函数实现。");
        var function = projection.Function
            ?? throw new InvalidOperationException("内部错误：缺少函数调用表达式。");

        var args = new object?[function.Arguments.Count];
        for (int i = 0; i < function.Arguments.Count; i++)
            args[i] = EvaluateScalarArgument(function.Arguments[i], timestamp, series, fieldLookups);

        return scalarFunction.Evaluate(args);
    }

    private static object? EvaluateScalarArgument(
        SqlExpression expression,
        long timestamp,
        SeriesEntry series,
        IReadOnlyDictionary<string, Dictionary<long, FieldValue>> fieldLookups)
    {
        return expression switch
        {
            IdentifierExpression id when string.Equals(id.Name, "time", StringComparison.OrdinalIgnoreCase)
                => timestamp,
            IdentifierExpression id when series.Tags.TryGetValue(id.Name, out var tagValue)
                => tagValue,
            IdentifierExpression id when fieldLookups.TryGetValue(id.Name, out var values)
                => values.TryGetValue(timestamp, out var value) ? UnboxFieldValue(value) : null,
            IdentifierExpression id when fieldLookups.ContainsKey(id.Name)
                => null,
            IdentifierExpression id
                => throw new InvalidOperationException($"SELECT 中引用了未知列 '{id.Name}'。"),
            LiteralExpression literal => EvaluateLiteral(literal),
            UnaryExpression unary => EvaluateUnaryExpression(unary, timestamp, series, fieldLookups),
            BinaryExpression binary => EvaluateBinaryExpression(binary, timestamp, series, fieldLookups),
            VectorLiteralExpression vector => EvaluateVectorLiteral(vector),
            GeoPointLiteralExpression geoPoint => GeoPoint.Create(geoPoint.Lat, geoPoint.Lon),
            FunctionCallExpression nested when FunctionRegistry.TryGetScalar(nested.Name, out var scalarFunction)
                => EvaluateNestedScalarFunction(nested, scalarFunction, timestamp, series, fieldLookups),
            FunctionCallExpression nested
                => throw new InvalidOperationException($"标量上下文不支持函数 '{nested.Name}'。"),
            _ => throw new InvalidOperationException(
                $"不支持的标量表达式类型 '{expression.GetType().Name}'。"),
        };
    }

    private static object? EvaluateNestedScalarFunction(
        FunctionCallExpression function,
        IScalarFunction scalarFunction,
        long timestamp,
        SeriesEntry series,
        IReadOnlyDictionary<string, Dictionary<long, FieldValue>> fieldLookups)
    {
        if (function.IsStar)
            throw new InvalidOperationException($"标量函数 {function.Name}(*) 非法。");

        var args = new object?[function.Arguments.Count];
        for (int i = 0; i < function.Arguments.Count; i++)
            args[i] = EvaluateScalarArgument(function.Arguments[i], timestamp, series, fieldLookups);
        return scalarFunction.Evaluate(args);
    }

    private static object? EvaluateLiteral(LiteralExpression literal) => literal.Kind switch
    {
        SqlLiteralKind.Null => null,
        SqlLiteralKind.Boolean => literal.BooleanValue,
        SqlLiteralKind.Integer => literal.IntegerValue,
        SqlLiteralKind.Float => literal.FloatValue,
        SqlLiteralKind.String => literal.StringValue,
        _ => throw new InvalidOperationException($"不支持的字面量类型 {literal.Kind}。"),
    };

    private static float[] EvaluateVectorLiteral(VectorLiteralExpression vector)
    {
        var result = new float[vector.Components.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = checked((float)vector.Components[i]);
        return result;
    }

    private static object? EvaluateUnaryExpression(
        UnaryExpression expression,
        long timestamp,
        SeriesEntry series,
        IReadOnlyDictionary<string, Dictionary<long, FieldValue>> fieldLookups)
    {
        var operand = EvaluateScalarArgument(expression.Operand, timestamp, series, fieldLookups);
        return expression.Operator switch
        {
            SqlUnaryOperator.Negate => -RequireDouble(operand, "一元负号"),
            SqlUnaryOperator.Not => !RequireBoolean(operand, "NOT"),
            _ => throw new InvalidOperationException($"不支持的一元运算 {expression.Operator}。"),
        };
    }

    private static object? EvaluateBinaryExpression(
        BinaryExpression expression,
        long timestamp,
        SeriesEntry series,
        IReadOnlyDictionary<string, Dictionary<long, FieldValue>> fieldLookups)
    {
        var left = EvaluateScalarArgument(expression.Left, timestamp, series, fieldLookups);
        var right = EvaluateScalarArgument(expression.Right, timestamp, series, fieldLookups);

        return expression.Operator switch
        {
            SqlBinaryOperator.Add => RequireDouble(left, "+") + RequireDouble(right, "+"),
            SqlBinaryOperator.Subtract => RequireDouble(left, "-") - RequireDouble(right, "-"),
            SqlBinaryOperator.Multiply => RequireDouble(left, "*") * RequireDouble(right, "*"),
            SqlBinaryOperator.Divide => RequireDouble(left, "/") / RequireDouble(right, "/"),
            SqlBinaryOperator.Modulo => RequireDouble(left, "%") % RequireDouble(right, "%"),
            _ => throw new InvalidOperationException($"标量函数参数内不支持运算 {expression.Operator}。"),
        };
    }

    private static bool RequireBoolean(object? value, string operatorName)
    {
        if (value is bool b) return b;
        throw new InvalidOperationException($"运算 {operatorName} 需要布尔参数。");
    }

    private static double RequireDouble(object? value, string functionName)
    {
        return value switch
        {
            byte b => b,
            sbyte sb => sb,
            short s => s,
            ushort us => us,
            int i => i,
            uint ui => ui,
            long l => l,
            ulong ul => ul,
            float f => f,
            double d => d,
            decimal m => (double)m,
            null => throw new InvalidOperationException($"函数/运算 {functionName} 不接受 NULL 参数。"),
            _ => throw new InvalidOperationException($"函数/运算 {functionName} 需要数值参数。"),
        };
    }

    private static TimeBucketSpec? ResolveGroupByTime(IReadOnlyList<SqlExpression> groupBy)
    {
        if (groupBy.Count == 0)
            return null;

        if (groupBy.Count != 1 || groupBy[0] is not FunctionCallExpression fn
            || !string.Equals(fn.Name, "time", StringComparison.OrdinalIgnoreCase)
            || fn.IsStar
            || fn.Arguments.Count != 1
            || fn.Arguments[0] is not DurationLiteralExpression duration)
        {
            throw new InvalidOperationException("当前仅支持 GROUP BY time(duration)。");
        }

        if (duration.Milliseconds <= 0)
            throw new InvalidOperationException("GROUP BY time(...) 桶大小必须 > 0。");

        return new TimeBucketSpec(duration.Milliseconds);
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
        FieldType.Vector => v.AsVector().ToArray(),
        FieldType.GeoPoint => v.AsGeoPoint(),
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

        // 解析每个聚合投影：legacy 7 个聚合走 BucketState 快路径；
        // 扩展聚合（PR #52：stddev / percentile / tdigest_agg / ...）走 IAggregateAccumulator。
        var aggSpecs = projections.Select(p =>
        {
            var fn = p.Function!;
            var spec = ResolveAggregateSpec(fn, p.ColumnName, schema);
            if (spec.LegacyAggregator is Aggregator.First or Aggregator.Last
                && matchedSeries.Count > 1)
            {
                throw new InvalidOperationException(
                    $"{spec.LegacyAggregator} 聚合在多 series 场景下尚未支持（v1）；请用 WHERE 过滤到单一 series。");
            }
            return spec;
        }).ToList();

        // 为每个 (bucketStart, specIdx) 维护 AggSlot：legacy 用 BucketState，扩展聚合用 IAggregateAccumulator。
        var bucketAccumulators = new SortedDictionary<long, AggSlot[]>();

        for (int specIdx = 0; specIdx < aggSpecs.Count; specIdx++)
        {
            var spec = aggSpecs[specIdx];
            // count(*) 时跨所有 field 列累加
            var fields = spec.IsCountStar
                ? schema.FieldColumns.Select(c => c.Name).ToList()
                : [spec.FieldName!];

            foreach (var series in matchedSeries)
            {
                foreach (var fname in fields)
                {
                    var col = schema.TryGetColumn(fname);
                    // count(*) 时跨所有 field 列累加，但跳过非数值复合字段（语义上不参与数值统计）
                    if (spec.IsCountStar && col is not null && col.DataType is FieldType.String or FieldType.Vector or FieldType.GeoPoint)
                        continue;

                    foreach (var dp in tsdb.Query.Execute(new PointQuery(series.Id, fname, where.TimeRange)))
                    {
                        long bucketStart = bucketSizeMs > 0
                            ? TimeBucket.Floor(dp.Timestamp, bucketSizeMs)
                            : long.MinValue;

                        if (!bucketAccumulators.TryGetValue(bucketStart, out var slots))
                        {
                            slots = new AggSlot[aggSpecs.Count];
                            for (int k = 0; k < slots.Length; k++)
                                slots[k] = AggSlot.Create(aggSpecs[k]);
                            bucketAccumulators[bucketStart] = slots;
                        }

                        // count(*) 不需要数值；其他聚合需要把字段值转为 double
                        bool needsValue = !(spec.IsCountStar
                            || spec.LegacyAggregator == Aggregator.Count);
                        if (!needsValue)
                        {
                            slots[specIdx].UpdateCount(dp.Timestamp);
                        }
                        else
                        {
                            slots[specIdx].Update(dp.Timestamp, dp.Value, col);
                        }
                    }
                }
            }
        }

        var rows = new List<IReadOnlyList<object?>>(bucketAccumulators.Count);
        foreach (var (_, slots) in bucketAccumulators)
        {
            var row = new object?[aggSpecs.Count];
            for (int i = 0; i < aggSpecs.Count; i++)
                row[i] = slots[i].Finalize();
            rows.Add(row);
        }

        var columnNames = projections.Select(p => p.ColumnName).ToList();
        return new SelectExecutionResult(columnNames, rows);
    }

    private static double FieldValueToDouble(FieldValue v, MeasurementColumn? col)
    {
        if (v.TryGetNumeric(out var d)) return d;
        throw new InvalidOperationException(
            $"聚合仅支持数值字段（列 '{col?.Name ?? "?"}' 的类型为 {v.Type}）。");
    }

    private static object ComputeLegacyAggregateValue(Aggregator agg, BucketState st) => agg switch
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

    private static AggSpec ResolveAggregateSpec(
        FunctionCallExpression fn,
        string columnName,
        MeasurementSchema schema)
    {
        if (!FunctionRegistry.TryGetAggregate(fn.Name, out var aggregate))
            throw new InvalidOperationException($"未知聚合函数 '{fn.Name}'。");

        var fieldName = aggregate.ResolveFieldName(fn, schema);

        if (aggregate.LegacyAggregator is { } legacy)
            return new AggSpec(columnName, legacy, fieldName,
                ExtendedFunction: null, ExtendedCall: null, Schema: null);

        // 扩展聚合：保留函数与 AST 引用以便每个桶按需创建独立累加器。
        return new AggSpec(columnName, default, fieldName,
            ExtendedFunction: aggregate, ExtendedCall: fn, Schema: schema);
    }

    private sealed record AggSpec(
        string ColumnName,
        Aggregator LegacyAggregator,
        string? FieldName,
        IAggregateFunction? ExtendedFunction,
        FunctionCallExpression? ExtendedCall,
        MeasurementSchema? Schema)
    {
        public bool IsExtended => ExtendedFunction is not null;
        public bool IsCountStar => !IsExtended && LegacyAggregator == Aggregator.Count && FieldName is null;
    }

    /// <summary>每个 (bucket × spec) 的累加槽：legacy 走 <see cref="BucketState"/>，扩展聚合走累加器。</summary>
    private sealed class AggSlot
    {
        private readonly AggSpec _spec;
        private BucketState _legacy = BucketState.Empty;
        private readonly IAggregateAccumulator? _extended;

        private AggSlot(AggSpec spec, IAggregateAccumulator? extended)
        {
            _spec = spec;
            _extended = extended;
        }

        public static AggSlot Create(AggSpec spec)
        {
            if (!spec.IsExtended)
                return new AggSlot(spec, extended: null);

            var accumulator = spec.ExtendedFunction!.CreateAccumulator(spec.ExtendedCall!, spec.Schema!)
                ?? throw new InvalidOperationException(
                    $"扩展聚合 '{spec.ExtendedFunction.Name}' 未返回累加器实例。");
            return new AggSlot(spec, accumulator);
        }

        public void UpdateCount(long timestamp)
        {
            if (_extended is not null)
                throw new InvalidOperationException("扩展聚合不支持 count-only 更新路径。");
            _legacy = _legacy.Update(timestamp, 0.0);
        }

        public void Update(long timestamp, FieldValue value, MeasurementColumn? col)
        {
            if (_extended is null)
            {
                _legacy = _legacy.Update(timestamp, FieldValueToDouble(value, col));
            }
            else if (value.Type == FieldType.Vector)
            {
                _extended.Add(timestamp, value.AsVector());
            }
            else
            {
                _extended.Add(timestamp, FieldValueToDouble(value, col));
            }
        }

        public object? Finalize()
            => _extended is not null
                ? _extended.Finalize()
                : ComputeLegacyAggregateValue(_spec.LegacyAggregator, _legacy);
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
