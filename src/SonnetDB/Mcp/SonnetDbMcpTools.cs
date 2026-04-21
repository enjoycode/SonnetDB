using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SonnetDB.Json;
using SonnetDB.Sql;
using SonnetDB.Sql.Ast;
using SonnetDB.Sql.Execution;

namespace SonnetDB.Mcp;

/// <summary>
/// SonnetDB 服务端的只读 MCP tools。
/// </summary>
[McpServerToolType]
internal sealed class SonnetDbMcpTools
{
    /// <summary>
    /// 执行只读 SQL 查询。仅允许 <c>SELECT</c> / <c>SHOW MEASUREMENTS</c> / <c>SHOW TABLES</c> /
    /// <c>DESCRIBE [MEASUREMENT]</c>，并自动限制最大返回行数。
    /// </summary>
    [McpServerTool(
        Name = "query_sql",
        Title = "Query SQL",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(McpSqlQueryResult))]
    public static CallToolResult QuerySql(
        string sql,
        int? maxRows,
        SonnetDbMcpContextAccessor contextAccessor)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sql);
            var databaseName = contextAccessor.GetDatabaseName();
            var tsdb = contextAccessor.GetDatabase();
            var normalizedLimit = SonnetDbMcpResults.NormalizeToolRowLimit(maxRows);
            var statement = SqlParser.Parse(sql);

            if (statement is not SelectStatement and not ShowMeasurementsStatement and not DescribeMeasurementStatement)
                return SonnetDbMcpResults.Error(
                    "query_sql 仅支持 SELECT、SHOW MEASUREMENTS / SHOW TABLES 与 DESCRIBE [MEASUREMENT]。");

            SqlStatement executable = statement;
            var canTruncate = false;
            if (statement is SelectStatement select)
                executable = SonnetDbMcpResults.ApplyToolRowLimit(select, normalizedLimit, out canTruncate);

            var executionResult = SqlExecutor.ExecuteStatement(tsdb, executable);
            if (executionResult is not SelectExecutionResult selectResult)
                return SonnetDbMcpResults.Error("只读 SQL 未返回结果集。");

            var (rows, truncated) = SonnetDbMcpResults.SliceRows(selectResult, normalizedLimit, canTruncate);
            var payload = new McpSqlQueryResult(
                databaseName,
                StatementType: GetStatementType(statement),
                selectResult.Columns,
                rows,
                rows.Count,
                truncated);

            return SonnetDbMcpResults.Success(payload, ServerJsonContext.Default.McpSqlQueryResult);
        }
        catch (Exception ex)
        {
            return SonnetDbMcpResults.Error(ex.Message);
        }
    }

    /// <summary>
    /// 列出当前数据库的全部 measurement 名称。
    /// </summary>
    [McpServerTool(
        Name = "list_measurements",
        Title = "List Measurements",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(McpMeasurementListResult))]
    public static CallToolResult ListMeasurements(
        int? maxRows,
        SonnetDbMcpContextAccessor contextAccessor)
    {
        try
        {
            var databaseName = contextAccessor.GetDatabaseName();
            var tsdb = contextAccessor.GetDatabase();
            var normalizedLimit = SonnetDbMcpResults.NormalizeToolRowLimit(maxRows);
            var executionResult = SqlExecutor.ExecuteStatement(tsdb, new ShowMeasurementsStatement());
            if (executionResult is not SelectExecutionResult selectResult)
                return SonnetDbMcpResults.Error("SHOW MEASUREMENTS 未返回结果集。");

            var names = new List<string>(Math.Min(selectResult.Rows.Count, normalizedLimit));
            for (int i = 0; i < selectResult.Rows.Count && i < normalizedLimit; i++)
                names.Add((string?)selectResult.Rows[i][0] ?? string.Empty);

            var payload = new McpMeasurementListResult(
                databaseName,
                names,
                Truncated: selectResult.Rows.Count > normalizedLimit);

            return SonnetDbMcpResults.Success(payload, ServerJsonContext.Default.McpMeasurementListResult);
        }
        catch (Exception ex)
        {
            return SonnetDbMcpResults.Error(ex.Message);
        }
    }

    /// <summary>
    /// 描述指定 measurement 的列结构。
    /// </summary>
    [McpServerTool(
        Name = "describe_measurement",
        Title = "Describe Measurement",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(McpMeasurementSchemaResult))]
    public static CallToolResult DescribeMeasurement(
        string name,
        SonnetDbMcpContextAccessor contextAccessor)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var databaseName = contextAccessor.GetDatabaseName();
            var tsdb = contextAccessor.GetDatabase();
            var executionResult = SqlExecutor.ExecuteStatement(tsdb, new DescribeMeasurementStatement(name));
            if (executionResult is not SelectExecutionResult selectResult)
                return SonnetDbMcpResults.Error("DESCRIBE MEASUREMENT 未返回结果集。");

            var columns = new List<McpMeasurementColumnResult>(selectResult.Rows.Count);
            foreach (var row in selectResult.Rows)
            {
                columns.Add(new McpMeasurementColumnResult(
                    Name: (string?)row[0] ?? string.Empty,
                    ColumnType: (string?)row[1] ?? string.Empty,
                    DataType: (string?)row[2] ?? string.Empty));
            }

            var payload = new McpMeasurementSchemaResult(databaseName, name, columns);
            return SonnetDbMcpResults.Success(payload, ServerJsonContext.Default.McpMeasurementSchemaResult);
        }
        catch (Exception ex)
        {
            return SonnetDbMcpResults.Error(ex.Message);
        }
    }

    private static string GetStatementType(SqlStatement statement) => statement switch
    {
        SelectStatement => "select",
        ShowMeasurementsStatement => "show_measurements",
        DescribeMeasurementStatement => "describe_measurement",
        _ => "unknown",
    };
}
