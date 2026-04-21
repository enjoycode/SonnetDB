using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SonnetDB.Json;
using SonnetDB.Sql.Ast;
using SonnetDB.Sql.Execution;

namespace SonnetDB.Mcp;

/// <summary>
/// SonnetDB 服务端的只读 MCP resources。
/// </summary>
[McpServerResourceType]
internal sealed class SonnetDbMcpResources
{
    /// <summary>
    /// 当前数据库 measurement 列表资源。
    /// </summary>
    [McpServerResource(
        UriTemplate = "sonnetdb://schema/measurements",
        Name = "measurements",
        Title = "Measurement List",
        MimeType = "application/json")]
    public static TextResourceContents GetMeasurements(SonnetDbMcpContextAccessor contextAccessor)
    {
        var databaseName = contextAccessor.GetDatabaseName();
        var tsdb = contextAccessor.GetDatabase();
        var executionResult = SqlExecutor.ExecuteStatement(tsdb, new ShowMeasurementsStatement());
        var selectResult = (SelectExecutionResult)executionResult!;

        var names = new List<string>(Math.Min(selectResult.Rows.Count, SonnetDbMcpResults.ResourceRowLimit));
        for (int i = 0; i < selectResult.Rows.Count && i < SonnetDbMcpResults.ResourceRowLimit; i++)
            names.Add((string?)selectResult.Rows[i][0] ?? string.Empty);

        var payload = new McpMeasurementListResult(
            databaseName,
            names,
            Truncated: selectResult.Rows.Count > SonnetDbMcpResults.ResourceRowLimit);

        return SonnetDbMcpResults.Resource(
            "sonnetdb://schema/measurements",
            payload,
            ServerJsonContext.Default.McpMeasurementListResult);
    }

    /// <summary>
    /// 指定 measurement schema 资源。
    /// </summary>
    [McpServerResource(
        UriTemplate = "sonnetdb://schema/measurement/{name}",
        Name = "measurement_schema",
        Title = "Measurement Schema",
        MimeType = "application/json")]
    public static TextResourceContents GetMeasurementSchema(
        string name,
        SonnetDbMcpContextAccessor contextAccessor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var databaseName = contextAccessor.GetDatabaseName();
        var tsdb = contextAccessor.GetDatabase();
        var executionResult = SqlExecutor.ExecuteStatement(tsdb, new DescribeMeasurementStatement(name));
        var selectResult = (SelectExecutionResult)executionResult!;

        var columns = new List<McpMeasurementColumnResult>(selectResult.Rows.Count);
        foreach (var row in selectResult.Rows)
        {
            columns.Add(new McpMeasurementColumnResult(
                Name: (string?)row[0] ?? string.Empty,
                ColumnType: (string?)row[1] ?? string.Empty,
                DataType: (string?)row[2] ?? string.Empty));
        }

        var payload = new McpMeasurementSchemaResult(databaseName, name, columns);
        return SonnetDbMcpResults.Resource(
            $"sonnetdb://schema/measurement/{name}",
            payload,
            ServerJsonContext.Default.McpMeasurementSchemaResult);
    }

    /// <summary>
    /// 当前数据库统计资源。
    /// </summary>
    [McpServerResource(
        UriTemplate = "sonnetdb://stats/database",
        Name = "database_stats",
        Title = "Database Stats",
        MimeType = "application/json")]
    public static TextResourceContents GetDatabaseStats(SonnetDbMcpContextAccessor contextAccessor)
    {
        var databaseName = contextAccessor.GetDatabaseName();
        var tsdb = contextAccessor.GetDatabase();
        var payload = new McpDatabaseStatsResult(
            databaseName,
            MeasurementCount: tsdb.Measurements.Count,
            SegmentCount: tsdb.Segments.SegmentCount,
            MemTablePointCount: tsdb.MemTable.PointCount,
            NextSegmentId: tsdb.NextSegmentId,
            CheckpointLsn: tsdb.CheckpointLsn);

        return SonnetDbMcpResults.Resource(
            "sonnetdb://stats/database",
            payload,
            ServerJsonContext.Default.McpDatabaseStatsResult);
    }
}
