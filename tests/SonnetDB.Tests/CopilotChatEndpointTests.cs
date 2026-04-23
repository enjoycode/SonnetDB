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

        _app = Program.BuildApp(
            ["--Kestrel:Endpoints:Http:Url=http://127.0.0.1:0"],
            options,
            services =>
            {
                services.AddSingleton<IEmbeddingProvider, FakeEmbeddingProvider>();
                services.AddSingleton<IChatProvider, FakeChatProvider>();
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
        public ValueTask<string> CompleteAsync(IReadOnlyList<AiMessage> messages, CancellationToken cancellationToken = default)
        {
            var system = messages[0].Content;
            if (system.Contains("工具规划器", StringComparison.Ordinal))
            {
                return ValueTask.FromResult(
                    """
                    {"tools":[{"name":"describe_measurement","measurement":"cpu"}]}
                    """);
            }

            return ValueTask.FromResult("cpu measurement 包含 host(tag)、usage(float64) 和 temp(int64)。[C1][C2][C3]");
        }
    }
}
