using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SonnetDB.Contracts;
using SonnetDB.Hosting;
using SonnetDB.Json;

namespace SonnetDB.Endpoints;

/// <summary>
/// SSE（Server-Sent Events）端点：<c>GET /v1/events</c>。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>使用 <c>text/event-stream</c> 长连接，每条事件按 <c>event:</c> + <c>data:</c> + 空行格式输出。</item>
///   <item>支持 query 参数 <c>?stream=metrics,slow_query,db</c> 过滤通道；缺省订阅全部。</item>
///   <item>连接建立后立即发送一条 <c>event: hello</c>，便于客户端确认握手成功。</item>
///   <item>每 30 秒发送一次注释行（以 <c>:</c> 开头）作为心跳，避免中间代理切断空闲连接。</item>
/// </list>
/// </remarks>
internal static class SseEndpointHandler
{
    private static readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 处理 <c>GET /v1/events</c>。在客户端断开或 host shutdown 时退出。
    /// </summary>
    public static async Task HandleAsync(HttpContext context, EventBroadcaster broadcaster)
    {
        ArgumentNullException.ThrowIfNull(broadcaster);

        var filter = ParseStreamFilter(context.Request.Query["stream"].ToString());
        ConfigureResponse(context);

        // hello 帧：写一个握手事件，方便客户端 onopen/onmessage 调试
        await WriteEventAsync(context, "hello", "{\"ok\":true}").ConfigureAwait(false);
        await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);

        using var sub = broadcaster.Subscribe(filter, capacity: 128);
        var reader = sub.Reader;

        var heartbeatCts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, heartbeatCts.Token);

        try
        {
            while (!context.RequestAborted.IsCancellationRequested)
            {
                // 等待事件或心跳超时
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                timeoutCts.CancelAfter(_heartbeatInterval);

                bool gotEvent;
                try
                {
                    gotEvent = await reader.WaitToReadAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
                {
                    // 心跳超时：发送注释行心跳后继续
                    await WriteHeartbeatAsync(context).ConfigureAwait(false);
                    continue;
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (!gotEvent)
                    break; // 通道已 complete

                while (reader.TryRead(out var evt))
                {
                    await WriteEventAsync(context, evt.Type, evt.Data, evt.TimestampMs).ConfigureAwait(false);
                }
                await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 客户端断开 → 静默退出
        }
        finally
        {
            heartbeatCts.Cancel();
        }
    }

    private static void ConfigureResponse(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache, no-store, no-transform";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no"; // 防 nginx 缓冲
        // 关闭 ASP.NET Core 默认的 response body 缓冲，确保 flush 立即送出
        var bufferingFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
        bufferingFeature?.DisableBuffering();
    }

    private static async Task WriteEventAsync(HttpContext context, string type, string jsonData, long? timestampMs = null)
    {
        var sb = new StringBuilder(jsonData.Length + 64);
        sb.Append("event: ").Append(type).Append('\n');
        if (timestampMs.HasValue)
            sb.Append("id: ").Append(timestampMs.Value).Append('\n');
        // data 字段必须按行拆分，每行前缀 "data: "
        foreach (var line in jsonData.Split('\n'))
        {
            sb.Append("data: ").Append(line).Append('\n');
        }
        sb.Append('\n');
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteHeartbeatAsync(HttpContext context)
    {
        var bytes = ": heartbeat\n\n"u8.ToArray();
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
        await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
    }

    private static IReadOnlySet<string>? ParseStreamFilter(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            set.Add(part);
        }
        return set.Count == 0 ? null : set;
    }
}

/// <summary>
/// 把强类型事件序列化为 <see cref="ServerEvent"/> 的便利工厂。集中放在这里，
/// 避免广播器与 <see cref="ServerJsonContext"/> 之间多次往返序列化。
/// </summary>
internal static class ServerEventFactory
{
    /// <summary>构造 <c>metrics</c> 事件。</summary>
    public static ServerEvent Metrics(MetricsSnapshotEvent payload)
    {
        var json = JsonSerializer.Serialize(payload, ServerJsonContext.Default.MetricsSnapshotEvent);
        return new ServerEvent(ServerEvent.ChannelMetrics, json, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>构造 <c>slow_query</c> 事件。</summary>
    public static ServerEvent SlowQuery(SlowQueryEvent payload)
    {
        var json = JsonSerializer.Serialize(payload, ServerJsonContext.Default.SlowQueryEvent);
        return new ServerEvent(ServerEvent.ChannelSlowQuery, json, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>构造 <c>db</c> 事件。</summary>
    public static ServerEvent Database(DatabaseEvent payload)
    {
        var json = JsonSerializer.Serialize(payload, ServerJsonContext.Default.DatabaseEvent);
        return new ServerEvent(ServerEvent.ChannelDatabase, json, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }
}
