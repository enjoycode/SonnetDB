using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SonnetDB.Auth;
using SonnetDB.Configuration;
using SonnetDB.Contracts;
using SonnetDB.Copilot;
using SonnetDB.Json;
using Xunit;

namespace SonnetDB.Tests;

/// <summary>
/// PR #67：Copilot 单轮聊天端点端到端测试。
/// </summary>
public sealed class CopilotChatEndpointTests : IAsyncLifetime
{
    private const string AdminToken = "copilot-admin-token";
    private const string DatabaseName = "alpha";

    private WebApplication? _app;
    private string _baseUrl = string.Empty;
    private string? _dataRoot;
    private string? _docsRoot;
    private string? _skillsRoot;
    private FakeChatProvider? _chatProvider;

    public async Task InitializeAsync()
    {
        _dataRoot = CreateTempDirectory("sndb-copilot-chat-data-");
        _docsRoot = CreateTempDirectory("sndb-copilot-chat-docs-");
        _skillsRoot = CreateTempDirectory("sndb-copilot-chat-skills-");

        File.WriteAllText(
            Path.Combine(_docsRoot, "cpu.md"),
            """
            # CPU Measurement

            ## Schema

            `cpu` measurement 包含 `host` 标签，以及 `usage`、`temp` 两个字段。
            """);

        File.WriteAllText(
            Path.Combine(_skillsRoot, "schema-design.md"),
            """
            ---
            name: schema-design
            description: 用于回答 measurement 的字段和 schema 问题
            triggers: [schema, 字段, 列]
            requires_tools: [describe_measurement, list_measurements]
            ---

            当用户询问 measurement 的字段、列、tag 或 field 时，优先使用 describe_measurement；
            如果用户只想看有哪些 measurement，再用 list_measurements。
            """);

        var options = new ServerOptions
        {
            DataRoot = _dataRoot,
            AutoLoadExistingDatabases = true,
            AllowAnonymousProbes = true,
            Tokens = new Dictionary<string, string>
            {
                [AdminToken] = ServerRoles.Admin,
            },
        };
        options.Copilot.Enabled = true;
        options.Copilot.Embedding.Provider = "openai";
        options.Copilot.Embedding.Endpoint = "https://embedding.example/v1/";
        options.Copilot.Embedding.ApiKey = "embedding-key";
        options.Copilot.Embedding.Model = "embedding-model";
        options.Copilot.Chat.Provider = "openai";
        options.Copilot.Chat.Endpoint = "https://chat.example/v1/";
        options.Copilot.Chat.ApiKey = "chat-key";
        options.Copilot.Chat.Model = "chat-model";
        options.Copilot.Docs.Roots = [_docsRoot];
        options.Copilot.Docs.AutoIngestOnStartup = false;
        options.Copilot.Skills.Root = _skillsRoot;
        options.Copilot.Skills.AutoIngestOnStartup = false;
        _chatProvider = new FakeChatProvider();

        _app = Program.BuildApp(
            ["--Kestrel:Endpoints:Http:Url=http://127.0.0.1:0"],
            options,
            services =>
            {
                services.AddSingleton<IEmbeddingProvider, FakeEmbeddingProvider>();
                services.AddSingleton<IChatProvider>(_chatProvider);
            });
        await _app.StartAsync();

        var addresses = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel 未暴露监听地址。");
        _baseUrl = addresses.Addresses.First();

        await _app.Services.GetRequiredService<DocsIngestor>()
            .IngestAsync([_docsRoot], force: true, dryRun: false);
        await _app.Services.GetRequiredService<SkillRegistry>()
            .IngestAsync(_skillsRoot, force: true, dryRun: false);

        using var admin = CreateClient(AdminToken);
        await CreateDatabaseAsync(admin, DatabaseName);
        await ExecuteSqlAsync(admin, "CREATE MEASUREMENT cpu (host TAG, usage FIELD FLOAT, temp FIELD INT)");
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        DeleteDirectory(_skillsRoot);
        DeleteDirectory(_docsRoot);
        DeleteDirectory(_dataRoot);
    }

    [Fact]
    public async Task CopilotChat_WithoutGrant_ReturnsForbidden()
    {
        using var admin = CreateClient(AdminToken);
        await ExecuteSqlAsync(admin, "CREATE USER nogrant WITH PASSWORD 'p'");
        var token = await LoginAsync("nogrant", "p");

        using var client = CreateClient(token);
        var response = await client.PostAsync(
            "/v1/copilot/chat",
            JsonContent.Create(
                new CopilotChatRequest(DatabaseName, "cpu 表有哪些字段？"),
                ServerJsonContext.Default.CopilotChatRequest));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("forbidden", body);
        Assert.Contains(DatabaseName, body);
    }

    [Fact]
    public async Task CopilotChat_WithReadGrant_ReturnsNdjsonEvents()
    {
        _chatProvider!.Reset();
        using var client = await CreateReaderClientAsync("reader_ndjson");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/copilot/chat")
        {
            Content = JsonContent.Create(
                new CopilotChatRequest(DatabaseName, "cpu 表有哪些字段？", DocsK: 3, SkillsK: 2),
                ServerJsonContext.Default.CopilotChatRequest),
        };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);

        var events = await ReadNdjsonEventsAsync(response);

        Assert.Equal(["start", "retrieval", "tool_call", "tool_result", "final", "done"], events.Select(static e => e.Type));

        var retrieval = Assert.Single(events, static e => e.Type == "retrieval");
        Assert.Contains("schema-design", retrieval.SkillNames ?? []);
        Assert.NotNull(retrieval.Citations);

        var final = Assert.Single(events, static e => e.Type == "final");
        Assert.Contains("cpu", final.Answer ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.True(final.Citations?.Count >= 3);
    }

    [Fact]
    public async Task CopilotChatStream_WithReadGrant_ReturnsSseEvents()
    {
        _chatProvider!.Reset();
        using var client = await CreateReaderClientAsync("reader_sse");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/copilot/chat/stream")
        {
            Content = JsonContent.Create(
                new CopilotChatRequest(DatabaseName, "cpu 表有哪些字段？", DocsK: 3, SkillsK: 2),
                ServerJsonContext.Default.CopilotChatRequest),
        };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var events = new List<CopilotChatEvent>();

        while (await reader.ReadLineAsync() is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]")
                break;
            if (data.Length == 0)
                continue;

            var evt = JsonSerializer.Deserialize(data, ServerJsonContext.Default.CopilotChatEvent);
            Assert.NotNull(evt);
            events.Add(evt!);
        }

        Assert.Contains(events, static e => e.Type == "tool_result");
        var final = Assert.Single(events, static e => e.Type == "final");
        Assert.Contains("host", final.Answer ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.True(final.Citations?.Count >= 3);
        Assert.Equal("done", events[^1].Type);
    }

    [Fact]
    public async Task CopilotChat_WithMessagesHistory_TrimsOldContextAndKeepsRecentTurns()
    {
        _chatProvider!.Reset();
        _chatProvider.PlannerHandler = static _ => """{"tools":[]}""";
        _chatProvider.AnswerHandler = static _ => "我已经结合最近上下文总结完成。[C1]";

        const string oldMarker = "OLDCTX_MARKER_12345";
        using var client = await CreateReaderClientAsync("reader_history");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/copilot/chat")
        {
            Content = JsonContent.Create(
                new CopilotChatRequest(
                    DatabaseName,
                    Messages:
                    [
                        new AiMessage("user", string.Concat(Enumerable.Repeat(oldMarker, 900))),
                        new AiMessage("assistant", "这一段旧回答也应该被裁掉。"),
                        new AiMessage("user", "上一次你告诉我 cpu measurement 里有 host 和 usage。"),
                        new AiMessage("assistant", "对，cpu 里有 host(tag)、usage(float64) 和 temp(int64)。"),
                        new AiMessage("user", "那请基于刚才的上下文，再帮我总结一下字段。"),
                    ]),
                ServerJsonContext.Default.CopilotChatRequest),
        };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var events = await ReadNdjsonEventsAsync(response);

        var start = Assert.Single(events, static e => e.Type == "start");
        Assert.Contains("裁剪", start.Message ?? string.Empty, StringComparison.Ordinal);

        var answerPrompt = _chatProvider.Calls
            .Select(static call => call[1].Content)
            .Last(static prompt => prompt.Contains("当前用户问题：", StringComparison.Ordinal));
        Assert.DoesNotContain(oldMarker, answerPrompt, StringComparison.Ordinal);
        Assert.Contains("cpu 里有 host(tag)、usage(float64) 和 temp(int64)。", answerPrompt, StringComparison.Ordinal);
        Assert.Contains("那请基于刚才的上下文，再帮我总结一下字段。", answerPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CopilotChat_WhenQuerySqlFails_RetriesWithRewrittenSql()
    {
        _chatProvider!.Reset();
        _chatProvider.PlannerHandler = static _ => """{"tools":[{"name":"query_sql","sql":"SELECT * FROM missing_cpu"}]}""";
        _chatProvider.RepairHandler = messages =>
        {
            Assert.Contains("SELECT * FROM missing_cpu", messages[1].Content, StringComparison.Ordinal);
            Assert.Contains("missing_cpu", messages[1].Content, StringComparison.Ordinal);
            return "SELECT * FROM cpu LIMIT 2";
        };
        _chatProvider.AnswerHandler = static _ => "我已经根据执行错误改写 SQL，并完成查询。[C1][C2]";

        using var client = await CreateReaderClientAsync("reader_retry");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/copilot/chat")
        {
            Content = JsonContent.Create(
                new CopilotChatRequest(
                    DatabaseName,
                    Messages:
                    [
                        new AiMessage("user", "帮我查询 cpu 最近两条记录。"),
                    ]),
                ServerJsonContext.Default.CopilotChatRequest),
        };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var events = await ReadNdjsonEventsAsync(response);

        var retry = Assert.Single(events, static e => e.Type == "tool_retry");
        Assert.Equal(1, retry.Attempt);
        Assert.Contains("SELECT * FROM cpu LIMIT 2", retry.ToolArguments ?? string.Empty, StringComparison.Ordinal);

        var toolResult = Assert.Single(events, static e => e.Type == "tool_result");
        Assert.Contains("SELECT * FROM cpu LIMIT 2", toolResult.ToolArguments ?? string.Empty, StringComparison.Ordinal);

        var final = Assert.Single(events, static e => e.Type == "final");
        Assert.Contains("改写 SQL", final.Answer ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CopilotChat_WhenCreateMeasurementAnswerFails_ReturnsDeterministicSql()
    {
        _chatProvider!.Reset();
        _chatProvider.PlannerHandler = static _ => """{"tools":[{"name":"list_measurements"}]}""";
        _chatProvider.AnswerHandler = static _ => throw new InvalidOperationException("answer unavailable");

        using var client = await CreateReaderClientAsync("reader_create_fallback");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/copilot/chat")
        {
            Content = JsonContent.Create(
                new CopilotChatRequest(
                    DatabaseName,
                    Messages:
                    [
                        new AiMessage("user", "帮我写一个sql语句，建一个温度和湿度监测的表。"),
                    ]),
                ServerJsonContext.Default.CopilotChatRequest),
        };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var events = await ReadNdjsonEventsAsync(response);

        Assert.Contains(events, static e => e.Type == "tool_call" && e.ToolName == "draft_sql");
        var draftResult = Assert.Single(events, static e => e.Type == "tool_result" && e.ToolName == "draft_sql");
        Assert.Contains("CREATE MEASUREMENT sensor_temperature", draftResult.ToolResult ?? string.Empty, StringComparison.Ordinal);

        var final = Assert.Single(events, static e => e.Type == "final");
        Assert.Contains("CREATE MEASUREMENT sensor_temperature", final.Answer ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("temperature FIELD FLOAT", final.Answer ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("humidity FIELD FLOAT", final.Answer ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("请结合返回的结构化结果继续确认或缩小问题范围", final.Answer ?? string.Empty, StringComparison.Ordinal);
    }

    private async Task<HttpClient> CreateReaderClientAsync(string userName)
    {
        using var admin = CreateClient(AdminToken);
        await ExecuteSqlAsync(admin, $"CREATE USER {userName} WITH PASSWORD 'p'");
        await ExecuteSqlAsync(admin, $"GRANT READ ON DATABASE {DatabaseName} TO {userName}");
        var token = await LoginAsync(userName, "p");
        return CreateClient(token);
    }

    private HttpClient CreateClient(string? token = null)
    {
        var client = new HttpClient { BaseAddress = new Uri(_baseUrl) };
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<string> LoginAsync(string username, string password)
    {
        using var client = CreateClient();
        var response = await client.PostAsync(
            "/v1/auth/login",
            JsonContent.Create(new LoginRequest(username, password), ServerJsonContext.Default.LoginRequest));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"登录失败：{(int)response.StatusCode} {body}");

        var login = JsonSerializer.Deserialize(body, ServerJsonContext.Default.LoginResponse);
        Assert.NotNull(login);
        return login!.Token;
    }

    private async Task ExecuteSqlAsync(HttpClient client, string sql)
    {
        var response = await client.PostAsync(
            $"/v1/db/{DatabaseName}/sql",
            JsonContent.Create(new SqlRequest(sql), ServerJsonContext.Default.SqlRequest));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"执行 SQL 失败：{(int)response.StatusCode} {body}");
    }

    private static async Task CreateDatabaseAsync(HttpClient client, string databaseName)
    {
        var response = await client.PostAsync(
            "/v1/db",
            JsonContent.Create(new CreateDatabaseRequest(databaseName), ServerJsonContext.Default.CreateDatabaseRequest));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"创建数据库失败：{(int)response.StatusCode} {body}");
    }

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string? path)
    {
        if (path is null || !Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private static async Task<List<CopilotChatEvent>> ReadNdjsonEventsAsync(HttpResponseMessage response)
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var events = new List<CopilotChatEvent>();
        while (await reader.ReadLineAsync() is { Length: > 0 } line)
        {
            var evt = JsonSerializer.Deserialize(line, ServerJsonContext.Default.CopilotChatEvent);
            Assert.NotNull(evt);
            events.Add(evt!);
            if (evt!.Type == "done")
                break;
        }

        return events;
    }

    private sealed class FakeEmbeddingProvider : IEmbeddingProvider
    {
        public ValueTask<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            var embedding = new float[DocsIngestor.ExpectedEmbeddingDimensions];
            embedding[0] = 1.0f;
            embedding[1] = text.Length;
            embedding[2] = text.Contains("cpu", StringComparison.OrdinalIgnoreCase) ? 2.0f : 0.5f;
            return ValueTask.FromResult(embedding);
        }
    }

    private sealed class FakeChatProvider : IChatProvider
    {
        public Func<IReadOnlyList<AiMessage>, string>? PlannerHandler { get; set; }

        public Func<IReadOnlyList<AiMessage>, string>? RepairHandler { get; set; }

        public Func<IReadOnlyList<AiMessage>, string>? AnswerHandler { get; set; }

        public List<IReadOnlyList<AiMessage>> Calls { get; } = [];

        public void Reset()
        {
            PlannerHandler = null;
            RepairHandler = null;
            AnswerHandler = null;
            Calls.Clear();
        }

        public ValueTask<string> CompleteAsync(
            IReadOnlyList<AiMessage> messages,
            string? modelOverride = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(messages.ToArray());
            var system = messages[0].Content;
            if (system.Contains("工具规划器", StringComparison.Ordinal))
            {
                return ValueTask.FromResult(
                    PlannerHandler?.Invoke(messages)
                    ?? """
                       {"tools":[{"name":"describe_measurement","measurement":"cpu"}]}
                       """);
            }

            if (system.Contains("SQL 纠错器", StringComparison.Ordinal))
            {
                return ValueTask.FromResult(
                    RepairHandler?.Invoke(messages)
                    ?? "SELECT * FROM cpu LIMIT 5");
            }

            return ValueTask.FromResult(
                AnswerHandler?.Invoke(messages)
                ?? "cpu measurement 包含 host(tag)、usage(float64) 和 temp(int64)。[C1][C2][C3]");
        }
    }
}
