using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SonnetDB.Auth;
using SonnetDB.Contracts;
using SonnetDB.Copilot;
using SonnetDB.Engine;
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
        if (req is null)
        {
            await WriteErrorAsync(ctx, StatusCodes.Status400BadRequest, "bad_request", "请求体格式无效。")
                .ConfigureAwait(false);
            return;
        }

        var request = req;

        var messages = NormalizeMessages(request);
        if (messages.Count == 0)
        {
            await WriteErrorAsync(ctx, StatusCodes.Status400BadRequest, "bad_request", "请求体需包含 message 或 messages。").ConfigureAwait(false);
            return;
        }

        var provisioningIntent = CopilotProvisioning.TryExtractIntent(messages[^1].Content);
        var selectedDb = string.IsNullOrWhiteSpace(request.Db) ? null : request.Db.Trim();
        var visibleDatabases = DatabaseAccessEvaluator.GetVisibleDatabases(ctx, grantsStore, registry.ListDatabases());
        var isServerAdmin = DatabaseAccessEvaluator.IsServerAdmin(ctx);

        string contextDatabaseName;
        Tsdb? tsdb = null;
        var targetPermission = DatabasePermission.None;

        if (provisioningIntent is not null)
        {
            contextDatabaseName = provisioningIntent.DatabaseName;
            if (visibleDatabases.Any(database => string.Equals(database, contextDatabaseName, StringComparison.OrdinalIgnoreCase))
                && registry.TryGet(contextDatabaseName, out var visibleTsdb))
            {
                tsdb = visibleTsdb;
                targetPermission = DatabaseAccessEvaluator.GetEffectivePermission(ctx, grantsStore, contextDatabaseName);
            }
        }
        else
        {
            if (req is null || string.IsNullOrWhiteSpace(selectedDb))
            {
                await WriteErrorAsync(ctx, StatusCodes.Status400BadRequest, "bad_request", "请求体需包含 db；如果你想让 Copilot 直接帮你建库，请在消息里明确说明“新建/创建数据库”。").ConfigureAwait(false);
                return;
            }

            if (!TsdbRegistry.IsValidName(selectedDb))
            {
                await WriteErrorAsync(ctx, StatusCodes.Status400BadRequest, "bad_request", $"非法数据库名 '{selectedDb}'。")
                    .ConfigureAwait(false);
                return;
            }

            // 系统内置库（如 __copilot__）不允许从对话端点直接操作，避免 LLM 把它当成业务库去 SHOW / CREATE。
            if (DatabaseAccessEvaluator.IsSystemDatabase(selectedDb))
            {
                await WriteErrorAsync(ctx, StatusCodes.Status403Forbidden, "system_database", $"数据库 '{selectedDb}' 是系统内置库，不可在 Copilot 对话中直接使用，请选择一个业务数据库。")
                    .ConfigureAwait(false);
                return;
            }

            if (!registry.TryGet(selectedDb, out var selectedTsdb))
            {
                await WriteErrorAsync(ctx, StatusCodes.Status404NotFound, "db_not_found", $"数据库 '{selectedDb}' 不存在。")
                    .ConfigureAwait(false);
                return;
            }

            targetPermission = DatabaseAccessEvaluator.GetEffectivePermission(ctx, grantsStore, selectedDb);
            if (!DatabaseAccessEvaluator.HasPermission(targetPermission, DatabasePermission.Read))
            {
                await WriteErrorAsync(ctx, StatusCodes.Status403Forbidden, "forbidden", $"当前凭据对数据库 '{selectedDb}' 没有 read 权限。")
                    .ConfigureAwait(false);
                return;
            }

            contextDatabaseName = selectedDb;
            tsdb = selectedTsdb;
        }
        // M7：客户端可显式声明 read-only 模式，强制收紧 execute_sql 写权限。
        // 仅 read-write 走凭据自身的权限上限；其它取值（含未提供 / read-only / 任意拼写）一律按只读处理。
        var requestedMode = request.Mode?.Trim();
        var clientAllowsWrite = string.Equals(requestedMode, "read-write", StringComparison.OrdinalIgnoreCase);
        var canWrite = DatabaseAccessEvaluator.HasPermission(targetPermission, DatabasePermission.Write);
        var effectiveCanWrite = (canWrite || isServerAdmin) && clientAllowsWrite;
        // M8\uff1a\u5141\u8bb8\u5ba2\u6237\u7aef\u4e3a\u672c\u6b21\u8bf7\u6c42\u4e34\u65f6\u9009\u62e9 chat \u6a21\u578b\uff1b\u4e3a\u7a7a\u65f6\u8d70\u670d\u52a1\u7aef CopilotChatOptions.Model \u9ed8\u8ba4\u503c\u3002
        var modelOverride = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim();
        var context = new CopilotAgentContext(contextDatabaseName, tsdb, visibleDatabases, effectiveCanWrite, modelOverride, isServerAdmin);

        ctx.Response.StatusCode = StatusCodes.Status200OK;
        ctx.Response.ContentType = sse
            ? "text/event-stream; charset=utf-8"
            : "application/x-ndjson; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Append("X-Accel-Buffering", "no");

        try
        {
            await foreach (var evt in copilotAgent.RunAsync(context, messages, request.DocsK, request.SkillsK, ctx.RequestAborted).ConfigureAwait(false))
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
