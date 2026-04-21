using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SonnetDB.Auth;
using SonnetDB.Configuration;
using SonnetDB.Contracts;
using SonnetDB.Engine;
using SonnetDB.Hosting;
using SonnetDB.Json;

namespace SonnetDB.Endpoints;

/// <summary>
/// AI 助手相关端点。
/// </summary>
internal static class AiEndpointHandler
{
    /// <summary>固定服务商 URL 映射，避免用户自定义上游地址带来 SSRF 风险。</summary>
    private static readonly Dictionary<string, string> _providerUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["international"] = "https://sonnet.vip",
        ["domestic"] = "https://ai.sonnetdb.com",
    };

    private static string GetBaseUrl(string provider) =>
        _providerUrls.TryGetValue(provider, out var url) ? url : _providerUrls["international"];

    /// <summary>
    /// 向应用注册 AI 端点。
    /// </summary>
    public static void Map(
        WebApplication app,
        AiConfigStore configStore,
        GrantsStore grantsStore,
        TsdbRegistry registry,
        IHttpClientFactory httpClientFactory)
    {
        app.MapGet("/v1/ai/status", () =>
        {
            var cfg = configStore.Get();
            return Results.Json(
                new AiStatusResponse(cfg.Enabled, cfg.Provider, cfg.Model),
                ServerJsonContext.Default.AiStatusResponse);
        });

        app.MapGet("/v1/admin/ai-config", (HttpContext ctx) =>
        {
            if (!BearerAuthMiddleware.IsAdmin(BearerAuthMiddleware.GetRole(ctx)))
            {
                return Results.Json(
                    new ErrorResponse("forbidden", "仅 admin 可读取 AI 配置。"),
                    ServerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var cfg = configStore.Get();
            return Results.Json(
                new AiConfigResponse(cfg.Enabled, cfg.Provider, cfg.Model, cfg.TimeoutSeconds, !string.IsNullOrEmpty(cfg.ApiKey)),
                ServerJsonContext.Default.AiConfigResponse);
        });

        app.MapMethods("/v1/admin/ai-config", ["PUT"], (RequestDelegate)(async ctx =>
        {
            if (!BearerAuthMiddleware.IsAdmin(BearerAuthMiddleware.GetRole(ctx)))
            {
                await WriteErrorAsync(ctx, StatusCodes.Status403Forbidden, "forbidden", "仅 admin 可修改 AI 配置。").ConfigureAwait(false);
                return;
            }

            var req = await ReadJsonAsync(ctx, ServerJsonContext.Default.AiConfigRequest).ConfigureAwait(false);
            if (req is null)
            {
                await WriteErrorAsync(ctx, StatusCodes.Status400BadRequest, "bad_request", "请求体不能为空。").ConfigureAwait(false);
                return;
            }

            if (!_providerUrls.ContainsKey(req.Provider))
            {
                await WriteErrorAsync(ctx, StatusCodes.Status400BadRequest, "bad_request", "provider 必须为 international 或 domestic。").ConfigureAwait(false);
                return;
            }

            var existing = configStore.Get();
            configStore.Save(new AiOptions
            {
                Enabled = req.Enabled,
                Provider = req.Provider,
                ApiKey = req.ApiKey ?? existing.ApiKey,
                Model = req.Model,
                TimeoutSeconds = req.TimeoutSeconds > 0 ? req.TimeoutSeconds : existing.TimeoutSeconds,
            });

            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }));

        app.MapMethods("/v1/ai/chat", ["POST"], (RequestDelegate)(async ctx =>
        {
            var req = await ReadJsonAsync(ctx, ServerJsonContext.Default.AiChatRequest).ConfigureAwait(false);
            if (req is null || req.Messages.Count == 0)
            {
                await WriteErrorAsync(ctx, StatusCodes.Status400BadRequest, "bad_request", "messages 不可为空。").ConfigureAwait(false);
                return;
            }

            string? sqlGenPrompt = null;
            if (req.Mode == "sql_gen" && req.Db is not null)
            {
                sqlGenPrompt = await TryBuildAuthorizedSqlGenPromptAsync(ctx, req.Db, registry, grantsStore).ConfigureAwait(false);
                if (sqlGenPrompt is null)
                    return;
            }

            var cfg = configStore.Get();
            if (!cfg.Enabled)
            {
                await WriteErrorAsync(ctx, StatusCodes.Status503ServiceUnavailable, "ai_disabled",
                    "AI 助手未启用，请联系管理员在 AI 设置中开启并配置。").ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrEmpty(cfg.ApiKey))
            {
                await WriteErrorAsync(ctx, StatusCodes.Status503ServiceUnavailable, "no_api_key",
                    "AI 助手尚未配置 API Key，请联系管理员。").ConfigureAwait(false);
                return;
            }

            var messages = BuildMessages(req, sqlGenPrompt);

            ctx.Response.StatusCode = StatusCodes.Status200OK;
            ctx.Response.ContentType = "text/event-stream; charset=utf-8";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Append("X-Accel-Buffering", "no");

            try
            {
                await ProxyOpenAiAsync(ctx, cfg, messages, httpClientFactory).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await WriteSseErrorAsync(ctx, ex.Message).ConfigureAwait(false);
            }
        }));
    }

    private static List<AiMessage> BuildMessages(AiChatRequest req, string? sqlGenPrompt)
    {
        var messages = new List<AiMessage>(req.Messages.Count + 1);
        if (!string.IsNullOrEmpty(sqlGenPrompt))
            messages.Add(new AiMessage("system", sqlGenPrompt));

        messages.AddRange(req.Messages);
        return messages;
    }

    private static async Task<string?> TryBuildAuthorizedSqlGenPromptAsync(
        HttpContext ctx,
        string db,
        TsdbRegistry registry,
        GrantsStore grantsStore)
    {
        if (!TsdbRegistry.IsValidName(db))
        {
            await WriteErrorAsync(ctx, StatusCodes.Status400BadRequest, "bad_request", $"非法数据库名 '{db}'。").ConfigureAwait(false);
            return null;
        }

        if (!registry.TryGet(db, out var tsdb))
        {
            await WriteErrorAsync(ctx, StatusCodes.Status404NotFound, "db_not_found", $"数据库 '{db}' 不存在。").ConfigureAwait(false);
            return null;
        }

        var permission = DatabaseAccessEvaluator.GetEffectivePermission(ctx, grantsStore, db);
        if (!DatabaseAccessEvaluator.HasPermission(permission, DatabasePermission.Read))
        {
            await WriteErrorAsync(ctx, StatusCodes.Status403Forbidden, "forbidden",
                $"当前凭据对数据库 '{db}' 没有 read 权限。").ConfigureAwait(false);
            return null;
        }

        return BuildSqlGenPrompt(db, tsdb);
    }

    private static string BuildSqlGenPrompt(string db, Tsdb tsdb)
    {
        var sb = new StringBuilder();
        sb.AppendLine("你是 SonnetDB 时序数据库的 SQL 专家。");
        sb.AppendLine($"数据库 \"{db}\" 的表结构如下：");
        sb.AppendLine();

        foreach (var measurement in tsdb.Measurements.Snapshot())
        {
            sb.Append($"表名: {measurement.Name}");
            var tags = string.Join(", ", measurement.TagColumns.Select(column => column.Name));
            var fields = string.Join(", ", measurement.FieldColumns.Select(column => $"{column.Name}({column.DataType})"));
            if (!string.IsNullOrEmpty(tags))
                sb.Append($"  标签: {tags}");
            if (!string.IsNullOrEmpty(fields))
                sb.Append($"  字段: {fields}");
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("SonnetDB SQL 规则：");
        sb.AppendLine("- 时间戳列名为 ts（Unix 毫秒或 ISO-8601 字符串）");
        sb.AppendLine("- 标签（tag）用于过滤和 GROUP BY");
        sb.AppendLine("- 聚合函数：avg(), min(), max(), sum(), count(), first(), last()");
        sb.AppendLine("- 时间函数：date_trunc('minute'/'hour'/'day', ts), now()");
        sb.AppendLine("- 示例：SELECT avg(temperature) FROM sensors WHERE ts >= '2025-01-01' GROUP BY location");
        sb.AppendLine();
        sb.AppendLine("请根据用户的自然语言描述生成 SQL 语句。只返回 SQL，不包含任何解释或 Markdown 代码块。");
        return sb.ToString();
    }

    private static async Task ProxyOpenAiAsync(
        HttpContext ctx,
        AiOptions cfg,
        List<AiMessage> messages,
        IHttpClientFactory factory)
    {
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(cfg.TimeoutSeconds);

        var requestBody = new OpenAiRequest(cfg.Model, messages, Stream: true);
        var json = JsonSerializer.Serialize(requestBody, ServerJsonContext.Default.OpenAiRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var baseUrl = GetBaseUrl(cfg.Provider);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/v1/chat/completions")
        {
            Content = content,
        };
        httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);

        using var resp = await client.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ctx.RequestAborted).ConfigureAwait(false);
            await WriteSseErrorAsync(ctx, $"AI 服务错误 {(int)resp.StatusCode}: {err}").ConfigureAwait(false);
            return;
        }

        using var stream = await resp.Content.ReadAsStreamAsync(ctx.RequestAborted).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!ctx.RequestAborted.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ctx.RequestAborted).ConfigureAwait(false);
            if (line is null)
                break;
            if (line.Length == 0 || !line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]")
                break;

            OpenAiChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize(data, ServerJsonContext.Default.OpenAiChunk);
            }
            catch (JsonException)
            {
                continue;
            }

            if (chunk?.Choices is null || chunk.Choices.Count == 0)
                continue;

            var token = chunk.Choices[0].Delta?.Content;
            if (!string.IsNullOrEmpty(token))
                await WriteSseTokenAsync(ctx, token).ConfigureAwait(false);
        }

        await WriteSseDoneAsync(ctx).ConfigureAwait(false);
    }

    private static async Task WriteSseTokenAsync(HttpContext ctx, string token)
    {
        var evt = JsonSerializer.Serialize(new SseTokenEvent(token), ServerJsonContext.Default.SseTokenEvent);
        await ctx.Response.WriteAsync($"data: {evt}\n\n", ctx.RequestAborted).ConfigureAwait(false);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteSseErrorAsync(HttpContext ctx, string error)
    {
        var evt = JsonSerializer.Serialize(new SseErrorEvent(error), ServerJsonContext.Default.SseErrorEvent);
        await ctx.Response.WriteAsync($"data: {evt}\n\n", ctx.RequestAborted).ConfigureAwait(false);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteSseDoneAsync(HttpContext ctx)
    {
        await ctx.Response.WriteAsync("data: [DONE]\n\n", ctx.RequestAborted).ConfigureAwait(false);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteErrorAsync(HttpContext ctx, int status, string code, string message)
    {
        if (ctx.Response.HasStarted)
            return;

        ctx.Response.StatusCode = status;
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
}
