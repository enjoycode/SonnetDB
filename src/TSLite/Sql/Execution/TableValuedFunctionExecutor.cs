using TSLite.Catalog;
using TSLite.Engine;
using TSLite.Model;
using TSLite.Query;
using TSLite.Query.Functions.Forecasting;
using TSLite.Sql.Ast;
using TSLite.Storage.Format;

namespace TSLite.Sql.Execution;

/// <summary>
/// FROM 子句中的表值函数（Table-Valued Function，TVF）执行器；当前仅支持
/// PR #55 引入的 <c>forecast(measurement, field, horizon, 'algo'[, season])</c>。
/// </summary>
internal static class TableValuedFunctionExecutor
{
    public static SelectExecutionResult Execute(Tsdb tsdb, SelectStatement statement)
    {
        var call = statement.TableValuedFunction
            ?? throw new InvalidOperationException("内部错误：TVF 调用为空。");

        // 优先匹配用户注册的 TVF（PR #56）
        var udf = TSLite.Query.Functions.UserFunctionRegistry.Current;
        if (udf is not null && udf.TryGetTableValuedFunction(call.Name, out var executor))
            return executor(tsdb, statement);

        return call.Name.ToLowerInvariant() switch
        {
            "forecast" => ExecuteForecast(tsdb, statement, call),
            _ => throw new InvalidOperationException(
                $"未知表值函数 '{call.Name}'；当前 FROM 子句仅支持 forecast(...) 及通过 Tsdb.Functions 注册的 UDF。"),
        };
    }

    // ── forecast(measurement, field, horizon, 'algo'[, season]) ───────────

    private static SelectExecutionResult ExecuteForecast(Tsdb tsdb, SelectStatement statement, FunctionCallExpression call)
    {
        if (call.IsStar)
            throw new InvalidOperationException("forecast(*) 非法。");
        if (call.Arguments.Count is < 4 or > 5)
            throw new InvalidOperationException(
                "forecast(measurement, field, horizon, 'algo'[, season]) 需要 4~5 个参数。");

        // 第 1 个参数：measurement（已由 parser 提取到 statement.Measurement）
        var schema = tsdb.Measurements.TryGet(statement.Measurement)
            ?? throw new InvalidOperationException(
                $"forecast(...) 引用的 measurement '{statement.Measurement}' 不存在。");

        // 第 2 个参数：field
        if (call.Arguments[1] is not IdentifierExpression fieldId)
            throw new InvalidOperationException("forecast 第 2 个参数必须是字段列名。");
        var fieldCol = schema.TryGetColumn(fieldId.Name)
            ?? throw new InvalidOperationException(
                $"forecast 引用了未知字段 '{fieldId.Name}'。");
        if (fieldCol.Role != MeasurementColumnRole.Field)
            throw new InvalidOperationException(
                $"forecast 第 2 个参数 '{fieldId.Name}' 必须是 FIELD 列。");
        if (fieldCol.DataType == FieldType.String)
            throw new InvalidOperationException(
                $"forecast 不支持 String 字段 '{fieldId.Name}'。");

        // 第 3 个参数：horizon（正整数字面量）
        int horizon = ResolvePositiveIntLiteral(call.Arguments[2], "horizon");

        // 第 4 个参数：算法
        var algorithm = ResolveAlgorithm(call.Arguments[3]);

        // 第 5 个参数（可选）：季节长度
        int season = 0;
        if (call.Arguments.Count == 5)
            season = ResolveNonNegativeIntLiteral(call.Arguments[4], "season");

        // WHERE 子句：复用普通 SELECT 的 tag/time 过滤。
        var where = WhereClauseDecomposer.Decompose(statement.Where, schema);
        var matchedSeries = tsdb.Catalog.Find(statement.Measurement, where.TagFilter);

        // 输出列：time, value, lower, upper + 所有 tag 列（按 schema 顺序）
        var tagColumns = schema.Columns
            .Where(c => c.Role == MeasurementColumnRole.Tag)
            .ToList();
        var columnNames = new List<string>(4 + tagColumns.Count) { "time", "value", "lower", "upper" };
        foreach (var t in tagColumns) columnNames.Add(t.Name);

        // SELECT 列表必须是 *（TVF 输出 schema 由 TVF 自身决定）
        if (!IsSelectStar(statement.Projections))
            throw new InvalidOperationException(
                "forecast(...) 表值函数当前仅支持 SELECT *；请在外层查询投影具体列。");

        var rows = new List<IReadOnlyList<object?>>();

        foreach (var series in matchedSeries)
        {
            var points = QueryPoints(tsdb, series.Id, fieldCol.Name, where.TimeRange);
            if (points.Count < 2)
                continue;

            var ts = new long[points.Count];
            var values = new double[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                ts[i] = points[i].Timestamp;
                values[i] = points[i].Value.TryGetNumeric(out var d) ? d : double.NaN;
            }

            var forecast = TimeSeriesForecaster.Forecast(ts, values, horizon, algorithm, season);
            foreach (var p in forecast)
            {
                var row = new object?[columnNames.Count];
                row[0] = p.TimestampMs;
                row[1] = p.Value;
                row[2] = p.Lower;
                row[3] = p.Upper;
                for (int t = 0; t < tagColumns.Count; t++)
                    row[4 + t] = series.Tags.TryGetValue(tagColumns[t].Name, out var tv) ? tv : null;
                rows.Add(row);
            }
        }

        return new SelectExecutionResult(columnNames, rows);
    }

    private static bool IsSelectStar(IReadOnlyList<SelectItem> projections)
        => projections.Count == 1 && projections[0].Expression is StarExpression && projections[0].Alias is null;

    private static int ResolvePositiveIntLiteral(SqlExpression arg, string name)
    {
        if (arg is LiteralExpression { Kind: SqlLiteralKind.Integer, IntegerValue: > 0 and <= int.MaxValue } lit)
            return (int)lit.IntegerValue;
        throw new InvalidOperationException($"forecast 参数 '{name}' 必须是正整数字面量。");
    }

    private static int ResolveNonNegativeIntLiteral(SqlExpression arg, string name)
    {
        if (arg is LiteralExpression { Kind: SqlLiteralKind.Integer, IntegerValue: >= 0 and <= int.MaxValue } lit)
            return (int)lit.IntegerValue;
        throw new InvalidOperationException($"forecast 参数 '{name}' 必须是非负整数字面量。");
    }

    private static ForecastAlgorithm ResolveAlgorithm(SqlExpression arg)
    {
        if (arg is not LiteralExpression { Kind: SqlLiteralKind.String, StringValue: { } s })
            throw new InvalidOperationException(
                "forecast 第 4 个参数必须是字符串字面量 'linear' / 'holt_winters'。");
        return s.ToLowerInvariant() switch
        {
            "linear" => ForecastAlgorithm.Linear,
            "holt_winters" or "holt-winters" or "holtwinters" or "hw" => ForecastAlgorithm.HoltWinters,
            _ => throw new InvalidOperationException(
                $"forecast 不支持算法 '{s}'，仅支持 'linear' / 'holt_winters'。"),
        };
    }

    private static IReadOnlyList<DataPoint> QueryPoints(Tsdb tsdb, ulong seriesId, string fieldName, TimeRange range)
    {
        var query = new PointQuery(seriesId, fieldName, range);
        return tsdb.Query.Execute(query).ToList();
    }
}
