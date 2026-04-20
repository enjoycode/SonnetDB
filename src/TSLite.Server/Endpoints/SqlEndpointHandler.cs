using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using TSLite.Engine;
using TSLite.Server.Contracts;
using TSLite.Server.Hosting;
using TSLite.Server.Json;
using TSLite.Sql;
using TSLite.Sql.Ast;
using TSLite.Sql.Execution;

namespace TSLite.Server.Endpoints;

/// <summary>
/// 提供 <c>POST /v1/db/{db}/sql</c> 与 <c>POST /v1/db/{db}/sql/batch</c> 两个端点的处理逻辑。
/// 结果集以 <c>application/x-ndjson</c> 流式输出。
/// </summary>
internal static class SqlEndpointHandler
{
    private static readonly byte[] s_newline = "\n"u8.ToArray();

    /// <summary>
    /// 处理单条 SQL 请求。
    /// </summary>
    public static async Task HandleSingleAsync(
        HttpContext context,
        Tsdb tsdb,
        SqlRequest request,
        ServerMetrics metrics,
        bool canWrite,
        bool isAdmin,
        IControlPlane? controlPlane)
    {
        await ExecuteAsync(context, tsdb, [request], metrics, canWrite, isAdmin, controlPlane).ConfigureAwait(false);
    }

    /// <summary>
    /// 处理批量 SQL 请求。所有语句串行执行。
    /// </summary>
    public static async Task HandleBatchAsync(
        HttpContext context,
        Tsdb tsdb,
        SqlBatchRequest request,
        ServerMetrics metrics,
        bool canWrite,
        bool isAdmin,
        IControlPlane? controlPlane)
    {
        await ExecuteAsync(context, tsdb, request.Statements, metrics, canWrite, isAdmin, controlPlane).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(
        HttpContext context,
        Tsdb tsdb,
        IReadOnlyList<SqlRequest> statements,
        ServerMetrics metrics,
        bool canWrite,
        bool isAdmin,
        IControlPlane? controlPlane)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/x-ndjson; charset=utf-8";
        var writerOptions = new JsonWriterOptions { Indented = false, SkipValidation = false };

        for (int s = 0; s < statements.Count; s++)
        {
            var stmt = statements[s];
            metrics.RecordSqlRequest();
            var sw = Stopwatch.StartNew();

            SqlStatement parsed;
            try
            {
                parsed = SqlParser.Parse(stmt.Sql);
            }
            catch (Exception ex)
            {
                metrics.RecordSqlError();
                await WriteErrorAsync(context, "sql_error", ex.Message).ConfigureAwait(false);
                return;
            }

            if (IsControlPlaneStatement(parsed) && !isAdmin)
            {
                metrics.RecordSqlError();
                await WriteErrorAsync(context, "forbidden", "控制面 SQL（CREATE USER / GRANT / CREATE DATABASE / SHOW USERS 等）仅 admin 可执行。").ConfigureAwait(false);
                return;
            }

            object? result;
            try
            {
                result = SqlExecutor.ExecuteStatement(tsdb, parsed, controlPlane);
            }
            catch (Exception ex)
            {
                metrics.RecordSqlError();
                await WriteErrorAsync(context, "sql_error", ex.Message).ConfigureAwait(false);
                return;
            }

            switch (result)
            {
                case SelectExecutionResult sel:
                {
                    long rowCount = await WriteSelectAsync(context, sel, writerOptions).ConfigureAwait(false);
                    metrics.AddReturnedRows(rowCount);
                    await WriteEndAsync(context, writerOptions, rowCount, recordsAffected: -1, sw.Elapsed.TotalMilliseconds).ConfigureAwait(false);
                    break;
                }
                case InsertExecutionResult ins:
                {
                    if (!canWrite)
                    {
                        metrics.RecordSqlError();
                        await WriteErrorAsync(context, "forbidden", "INSERT 需要 readwrite 或 admin 角色。").ConfigureAwait(false);
                        return;
                    }
                    metrics.AddInsertedRows(ins.RowsInserted);
                    await WriteEndAsync(context, writerOptions, rowCount: 0, recordsAffected: ins.RowsInserted, sw.Elapsed.TotalMilliseconds).ConfigureAwait(false);
                    break;
                }
                case DeleteExecutionResult del:
                {
                    if (!canWrite)
                    {
                        metrics.RecordSqlError();
                        await WriteErrorAsync(context, "forbidden", "DELETE 需要 readwrite 或 admin 角色。").ConfigureAwait(false);
                        return;
                    }
                    await WriteEndAsync(context, writerOptions, rowCount: 0, recordsAffected: del.TombstonesAdded, sw.Elapsed.TotalMilliseconds).ConfigureAwait(false);
                    break;
                }
                default:
                {
                    // CREATE MEASUREMENT 、CREATE USER 等 DDL：返回受影响行数 0
                    // 控制面 DDL 已在上面单独鉴权 isAdmin，这里仅校验需 canWrite 的普通 DDL。
                    if (!IsControlPlaneStatement(parsed) && !canWrite)
                    {
                        metrics.RecordSqlError();
                        await WriteErrorAsync(context, "forbidden", "DDL 需要 readwrite 或 admin 角色。").ConfigureAwait(false);
                        return;
                    }
                    await WriteEndAsync(context, writerOptions, rowCount: 0, recordsAffected: 0, sw.Elapsed.TotalMilliseconds).ConfigureAwait(false);
                    break;
                }
            }
        }
    }

    private static async Task<long> WriteSelectAsync(HttpContext context, SelectExecutionResult result, JsonWriterOptions options)
    {
        var body = context.Response.BodyWriter;

        // 1) meta 行
        var meta = new ResultMeta("meta", result.Columns);
        await using (var metaWriter = new Utf8JsonWriter(body, options))
        {
            JsonSerializer.Serialize(metaWriter, meta, ServerJsonContext.Default.ResultMeta);
        }
        await body.WriteAsync(s_newline, context.RequestAborted).ConfigureAwait(false);

        // 2) 行数据：每行一条 ndjson
        long count = 0;
        for (int r = 0; r < result.Rows.Count; r++)
        {
            await using (var rowWriter = new Utf8JsonWriter(body, options))
            {
                NdjsonRowWriter.WriteRow(rowWriter, result.Rows[r]);
            }
            await body.WriteAsync(s_newline, context.RequestAborted).ConfigureAwait(false);
            count++;
        }

        return count;
    }

    private static async Task WriteEndAsync(HttpContext context, JsonWriterOptions options, long rowCount, int recordsAffected, double elapsedMs)
    {
        var body = context.Response.BodyWriter;
        var end = new ResultEnd("end", rowCount, recordsAffected, elapsedMs);
        await using (var w = new Utf8JsonWriter(body, options))
        {
            JsonSerializer.Serialize(w, end, ServerJsonContext.Default.ResultEnd);
        }
        await body.WriteAsync(s_newline, context.RequestAborted).ConfigureAwait(false);
        await body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteErrorAsync(HttpContext context, string code, string message)
    {
        // 若响应尚未开始：用 4xx 状态码
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = code switch
            {
                "forbidden" => StatusCodes.Status403Forbidden,
                "db_not_found" => StatusCodes.Status404NotFound,
                "unauthorized" => StatusCodes.Status401Unauthorized,
                _ => StatusCodes.Status400BadRequest,
            };
            context.Response.ContentType = "application/json; charset=utf-8";
            var err = new ErrorResponse(code, message);
            await JsonSerializer.SerializeAsync(context.Response.Body, err, ServerJsonContext.Default.ErrorResponse, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        // 已经在 ndjson 流中：附加一条错误行（type=error）
        var body = context.Response.BodyWriter;
        await using (var w = new Utf8JsonWriter(body, new JsonWriterOptions { Indented = false }))
        {
            JsonSerializer.Serialize(w, new ErrorResponse(code, message), ServerJsonContext.Default.ErrorResponse);
        }
        await body.WriteAsync(s_newline, context.RequestAborted).ConfigureAwait(false);
        await body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>判别是否为控制面 DDL（需要超级用户权限）。SHOW DATABASES 不在此列，普通认证用户可调用。</summary>
    private static bool IsControlPlaneStatement(SqlStatement statement) => statement is
        CreateUserStatement or
        AlterUserPasswordStatement or
        DropUserStatement or
        GrantStatement or
        RevokeStatement or
        CreateDatabaseStatement or
        DropDatabaseStatement or
        ShowUsersStatement or
        ShowGrantsStatement;
}
