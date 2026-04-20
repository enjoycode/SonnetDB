using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using TSLite.Engine;
using TSLite.Ingest;
using TSLite.Server.Contracts;
using TSLite.Server.Hosting;
using TSLite.Server.Json;

namespace TSLite.Server.Endpoints;

/// <summary>
/// 三个批量入库端点的统一处理器：
/// <c>POST /v1/db/{db}/measurements/{m}/lp</c>（Line Protocol）、
/// <c>POST /v1/db/{db}/measurements/{m}/json</c>（JSON points）、
/// <c>POST /v1/db/{db}/measurements/{m}/bulk</c>（INSERT VALUES 快路径）。
/// 全部走 <see cref="BulkIngestor"/>，绕开 SQL 解析器。
/// </summary>
internal static class BulkIngestEndpointHandler
{
    /// <summary>批量入库目标格式。</summary>
    public enum Format
    {
        /// <summary>Line Protocol。</summary>
        LineProtocol,
        /// <summary>JSON points。</summary>
        Json,
        /// <summary>INSERT INTO ... VALUES 快路径。</summary>
        BulkValues,
    }

    /// <summary>处理一次批量入库请求。</summary>
    public static async Task HandleAsync(
        HttpContext context,
        Tsdb tsdb,
        string measurement,
        Format format,
        ServerMetrics metrics)
    {
        var sw = Stopwatch.StartNew();
        BulkErrorPolicy errorPolicy = ParseOnError(context);
        bool flushOnComplete = ParseFlush(context);

        // 一次性读取请求体；批量入库的 payload 通常不算大（MB 级），无需流式。
        // 若未来需要更大 payload，可改为分块读取并按 reader 协议增量解析。
        byte[] body = await ReadAllAsync(context).ConfigureAwait(false);

        IPointReader? reader = null;
        try
        {
            reader = format switch
            {
                Format.LineProtocol => new LineProtocolReader(
                    Encoding.UTF8.GetString(body).AsMemory(),
                    measurementOverride: measurement),
                Format.Json => new JsonPointsReader(
                    new ReadOnlyMemory<byte>(body),
                    measurementOverride: measurement),
                Format.BulkValues => SchemaBoundBulkValuesReader.Create(
                    tsdb,
                    Encoding.UTF8.GetString(body),
                    measurementOverride: measurement),
                _ => throw new InvalidOperationException($"未知格式 {format}。"),
            };

            BulkIngestResult result;
            try
            {
                result = BulkIngestor.Ingest(tsdb, reader, errorPolicy, flushOnComplete);
            }
            catch (BulkIngestException ex)
            {
                await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "bulk_ingest_error", ex.Message).ConfigureAwait(false);
                return;
            }

            metrics.AddInsertedRows(result.Written);
            var resp = new BulkIngestResponse(result.Written, result.Skipped, sw.Elapsed.TotalMilliseconds);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json; charset=utf-8";
            await JsonSerializer.SerializeAsync(
                context.Response.Body,
                resp,
                ServerJsonContext.Default.BulkIngestResponse,
                context.RequestAborted).ConfigureAwait(false);
        }
        finally
        {
            (reader as IDisposable)?.Dispose();
        }
    }

    private static BulkErrorPolicy ParseOnError(HttpContext context)
    {
        if (context.Request.Query.TryGetValue("onerror", out var v)
            && string.Equals(v.ToString(), "skip", StringComparison.OrdinalIgnoreCase))
            return BulkErrorPolicy.Skip;
        return BulkErrorPolicy.FailFast;
    }

    private static bool ParseFlush(HttpContext context)
    {
        if (context.Request.Query.TryGetValue("flush", out var v)
            && bool.TryParse(v.ToString(), out var b))
            return b;
        return false;
    }

    private static async Task<byte[]> ReadAllAsync(HttpContext context)
    {
        // 优先按 Content-Length 一次性分配，避免多次扩容。
        if (context.Request.ContentLength is long len && len > 0 && len <= int.MaxValue)
        {
            var buffer = new byte[(int)len];
            int offset = 0;
            while (offset < buffer.Length)
            {
                int n = await context.Request.Body.ReadAsync(
                    buffer.AsMemory(offset, buffer.Length - offset),
                    context.RequestAborted).ConfigureAwait(false);
                if (n == 0) break;
                offset += n;
            }
            return offset == buffer.Length ? buffer : buffer.AsSpan(0, offset).ToArray();
        }

        // 未知长度：用 MemoryStream 累积。
        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms, context.RequestAborted).ConfigureAwait(false);
        return ms.ToArray();
    }

    private static async Task WriteErrorAsync(HttpContext ctx, int statusCode, string code, string message)
    {
        if (ctx.Response.HasStarted) return;
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, new ErrorResponse(code, message),
            ServerJsonContext.Default.ErrorResponse, ctx.RequestAborted).ConfigureAwait(false);
    }
}
