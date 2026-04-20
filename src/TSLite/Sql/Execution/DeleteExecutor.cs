using TSLite.Engine;
using TSLite.Sql.Ast;

namespace TSLite.Sql.Execution;

/// <summary>
/// <c>DELETE</c> 语句执行的内部辅助。复用 <see cref="WhereClauseDecomposer"/> 解析 tag 过滤
/// 与时间窗，对所有命中的 series × schema 中所有 Field 列调用 <see cref="Tsdb.Delete(ulong, string, long, long)"/>。
/// </summary>
internal static class DeleteExecutor
{
    public static DeleteExecutionResult Execute(Tsdb tsdb, DeleteStatement statement)
    {
        var schema = tsdb.Measurements.TryGet(statement.Measurement)
            ?? throw new InvalidOperationException(
                $"Measurement '{statement.Measurement}' 不存在；请先执行 CREATE MEASUREMENT。");

        var where = WhereClauseDecomposer.Decompose(statement.Where, schema);
        var matchedSeries = tsdb.Catalog.Find(statement.Measurement, where.TagFilter);

        long from = where.TimeRange.FromInclusive;
        // TimeRange 是闭区间 [FromInclusive, ToInclusive]，与 Tsdb.Delete 的语义一致。
        long to = where.TimeRange.ToInclusive;

        int tombstones = 0;
        foreach (var series in matchedSeries)
        {
            foreach (var col in schema.FieldColumns)
            {
                tsdb.Delete(series.Id, col.Name, from, to);
                tombstones++;
            }
        }

        return new DeleteExecutionResult(schema.Name, matchedSeries.Count, tombstones);
    }
}
