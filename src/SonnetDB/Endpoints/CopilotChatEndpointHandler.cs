using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SonnetDB.Auth;
using SonnetDB.Contracts;
using SonnetDB.Copilot;
using SonnetDB.Hosting;
using SonnetDB.Json;

namespace SonnetDB.Endpoints;

/// <summary>
/// PR #67 / #68：Copilot 聊天端点。
/// </summary>
internal static class CopilotChatEndpointHandler
{
    public static void Map(
        WebApplication app,
        CopilotReadiness copilotReadiness,
        CopilotAgent copilotAgent,
        GrantsStore grantsStore,
        TsdbRegistry registry)
    {
        app.MapMethods("/v1/copilot/chat", ["POST"], (RequestDelegate)(ctx =>
            HandleAsync(ctx, copilotReadiness, copilotAgent, grantsStore, registry, sse: false)));

        app.MapMethods("/v1/copilot/chat/stream", ["POST"], (RequestDelegate)(ctx =>
            HandleAsync(ctx, copilotReadiness, copilotAgent, grantsStore, registry, sse: true)));
    }

    private static async Task HandleAsync(
        HttpContext ctx,
        CopilotReadiness copilotReadiness,
        CopilotAgent copilotAgent,
        GrantsStore grantsStore,
        TsdbRegistry registry,
        bool sse)
    {
        var readiness = copilotReadiness.Evaluate();
        if (!readiness.Enabled)
        {
            await WriteErrorAsync(ctx, StatusCodes.Status409Conflict, "copilot_disabled", "Copilot 子系统已禁用。").ConfigureAwait(false);
            return;
        }

        if (!readiness.Ready)
        {
            await WriteErrorAsync(
                ctx,
                StatusCodes.Status503ServiceUnavailable,
                "copilot_not_ready",
                $"Copilot 子系统未就绪：{readiness.Reason ?? "unknown"}。").ConfigureAwait(false);
            return;
        }

        var req = await ReadJsonAsync(ctx, ServerJsonContext.Default.CopilotChatRequest).ConfigureAwait(false);
        if (req is null || string.IsNullOrWhiteSpace(req.Db))
        {
            await WriteErrorAsync(ctx, StatusCodes.Status400BadRequest, "bad_request", "请求体需包含 db。").ConfigureAwait(false);
            return;
        }

        var messages = NormalizeMessages(req);
        if (messages.Count == 0)
        {
            await WriteErrorAsync(ctx, StatusCodes.Status400BadRequest, "bad_request", "请求体需包含 message 或 messages。").ConfigureAwait(false);
            return;
        }

        if (!TsdbRegistry.IsValidName(req.Db))
        {
            await WriteErrorAsync(ctx, StatusCodes.Status400BadRequest, "bad_request", $"非法数据库名 '{req.Db}'。").ConfigureAwait(false);
            return;
        }

        if (!registry.TryGet(req.Db, out var tsdb))
        {
            await WriteErrorAsync(ctx, StatusCodes.Status404NotFound, "db_not_found", $"数据库 '{req.Db}' 不存在。").ConfigureAwait(false);
            return;
        }

        var permission = DatabaseAccessEvaluator.GetEffectivePermission(ctx, grantsStore, req.Db);
        if (!DatabaseAccessEvaluator.HasPermission(permission, DatabasePermission.Read))
        {
            await WriteErrorAsync(ctx, StatusCodes.Status403Forbidden, "forbidden", $"当前凭据对数据库 '{req.Db}' 没有 read 权限。").ConfigureAwait(false);
            return;
        }

        var visibleDatabases = DatabaseAccessEvaluator.GetVisibleDatabases(ctx, grantsStore, registry.ListDatabases());
        var context = new CopilotAgentContext(req.Db, tsdb, visibleDatabases);

        ctx.Response.StatusCode = StatusCodes.Status200OK;
        ctx.Response.ContentType = sse
            ? "text/event-stream; charset=utf-8"
            : "application/x-ndjson; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Append("X-Accel-Buffering", "no");

        try
        {
            await foreach (var evt in copilotAgent.RunAsync(context, messages, req.DocsK, req.SkillsK, ctx.RequestAborted).ConfigureAwait(false))
            {
                if (sse)
                {
                    await WriteSseEventAsync(ctx, evt).ConfigureAwait(false);
                }
                else
                {
                    await WriteNdjsonEventAsync(ctx, evt).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var errorEvent = new CopilotChatEvent("error", Message: ex.Message);
            var doneEvent = new CopilotChatEvent("done", Message: "completed");
            if (sse)
            {
                await WriteSseEventAsync(ctx, errorEvent).ConfigureAwait(false);
                await WriteSseEventAsync(ctx, doneEvent).ConfigureAwait(false);
            }
            else
            {
                await WriteNdjsonEventAsync(ctx, errorEvent).ConfigureAwait(false);
                await WriteNdjsonEventAsync(ctx, doneEvent).ConfigureAwait(false);
            }
        }

        if (sse)
        {
            await ctx.Response.WriteAsync("data: [DONE]\n\n", ctx.RequestAborted).ConfigureAwait(false);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted).ConfigureAwait(false);
        }
    }

    private static async Task WriteNdjsonEventAsync(HttpContext ctx, CopilotChatEvent evt)
    {
        await JsonSerializer.SerializeAsync(
            ctx.Response.Body,
            evt,
            ServerJsonContext.Default.CopilotChatEvent,
            ctx.RequestAborted).ConfigureAwait(false);
        await ctx.Response.WriteAsync("\n", ctx.RequestAborted).ConfigureAwait(false);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteSseEventAsync(HttpContext ctx, CopilotChatEvent evt)
    {
        var json = JsonSerializer.Serialize(evt, ServerJsonContext.Default.CopilotChatEvent);
        await ctx.Response.WriteAsync($"data: {json}\n\n", ctx.RequestAborted).ConfigureAwait(false);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteErrorAsync(HttpContext ctx, int statusCode, string code, string message)
    {
        if (ctx.Response.HasStarted)
            return;

        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            ctx.Response.Body,
            new ErrorResponse(code, message),
            ServerJsonContext.Default.ErrorResponse,
            ctx.RequestAborted).ConfigureAwait(false);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpContext ctx, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        where T : class
    {
        if (ctx.Request.ContentLength == 0)
            return null;

        try
        {
            return await JsonSerializer.DeserializeAsync(ctx.Request.Body, typeInfo, ctx.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<AiMessage> NormalizeMessages(CopilotChatRequest request)
    {
        if (request.Messages is { Count: > 0 })
        {
            return request.Messages
                .Where(static message => !string.IsNullOrWhiteSpace(message.Content))
                .Select(static message => new AiMessage(message.Role, message.Content.Trim()))
                .ToList();
        }

        return string.IsNullOrWhiteSpace(request.Message)
            ? []
            : [new AiMessage("user", request.Message.Trim())];
    }
}
