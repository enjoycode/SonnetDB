using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using TSLite.Server.Configuration;
using TSLite.Server.Contracts;
using TSLite.Server.Hosting;
using TSLite.Server.Json;
using Xunit;

namespace TSLite.Server.Tests;

/// <summary>
/// SSE 端到端测试：验证 <c>/v1/events</c> 推送
/// <c>db</c> / <c>slow_query</c> / <c>metrics</c> / <c>hello</c> 事件，
/// 并验证 <c>?access_token=</c> query 鉴权。
/// </summary>
public sealed class SseEndToEndTests : IAsyncLifetime
{
    private WebApplication? _app;
    private string? _baseUrl;
    private string? _dataRoot;
    private const string _adminToken = "admin-sse-token";

    public async Task InitializeAsync()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "tslite-sse-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRoot);
        var options = new ServerOptions
        {
            DataRoot = _dataRoot,
            AutoLoadExistingDatabases = true,
            AllowAnonymousProbes = true,
            // 阈值压到 0 → 任何 SQL 都会广播 slow_query，便于断言
            SlowQueryThresholdMs = 0,
            // metrics tick 拉到 1s，加快测试
            MetricsTickSeconds = 1,
            Tokens = new Dictionary<string, string>
            {
                [_adminToken] = ServerRoles.Admin,
            },
        };
        _app = Program.BuildApp(["--Kestrel:Endpoints:Http:Url=http://127.0.0.1:0"], options);
        await _app.StartAsync();
        var addresses = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
        _baseUrl = addresses.Addresses.First();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        if (_dataRoot is not null && Directory.Exists(_dataRoot))
        {
            try { Directory.Delete(_dataRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Events_RequiresToken_ReturnsUnauthorized()
    {
        using var client = new HttpClient { BaseAddress = new Uri(_baseUrl!) };
        var resp = await client.GetAsync("/v1/events");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Events_AccessTokenQueryString_ReceivesHelloAndDbEvents()
    {
        // 1) 打开 SSE 流（用 query token，模拟 EventSource）
        using var sseClient = new HttpClient { BaseAddress = new Uri(_baseUrl!) };
        using var streamReq = new HttpRequestMessage(HttpMethod.Get, $"/v1/events?access_token={_adminToken}&stream=db,slow_query,metrics");
        var streamResp = await sseClient.SendAsync(streamReq, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, streamResp.StatusCode);
        Assert.Equal("text/event-stream", streamResp.Content.Headers.ContentType?.MediaType);

        var buffer = new StringBuilder();
        var stream = await streamResp.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // 先收 hello
        var hello = await ReadOneEventAsync(reader, buffer, TimeSpan.FromSeconds(5));
        Assert.Equal("hello", hello.Event);

        // 2) 用另一个 client 触发 CREATE/DROP DATABASE
        using var apiClient = new HttpClient { BaseAddress = new Uri(_baseUrl!) };
        apiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        var dbName = "ssetest_" + Guid.NewGuid().ToString("N")[..8];
        var create = await apiClient.PostAsync("/v1/db",
            JsonContent.Create(new CreateDatabaseRequest(dbName), ServerJsonContext.Default.CreateDatabaseRequest));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        // 3) 流上应当能收到 db.created 事件
        var dbEvt = await ReadEventOfTypeAsync(reader, buffer, "db", TimeSpan.FromSeconds(5));
        using (var doc = JsonDocument.Parse(dbEvt.Data))
        {
            Assert.Equal(dbName, doc.RootElement.GetProperty("database").GetString());
            Assert.Equal("created", doc.RootElement.GetProperty("action").GetString());
        }

        // 4) 触发一条 SQL，因为阈值 = 0，必收到 slow_query
        await apiClient.PostAsync($"/v1/db/{dbName}/sql",
            JsonContent.Create(new SqlRequest("CREATE MEASUREMENT m (host TAG, v FIELD FLOAT)"), ServerJsonContext.Default.SqlRequest));
        var slow = await ReadEventOfTypeAsync(reader, buffer, "slow_query", TimeSpan.FromSeconds(5));
        using (var doc = JsonDocument.Parse(slow.Data))
        {
            Assert.Equal(dbName, doc.RootElement.GetProperty("database").GetString());
            Assert.False(doc.RootElement.GetProperty("failed").GetBoolean());
        }

        // 5) metrics tick = 1s，等 ≤ 3 秒应能收到至少一次 metrics
        var metrics = await ReadEventOfTypeAsync(reader, buffer, "metrics", TimeSpan.FromSeconds(4));
        using (var doc = JsonDocument.Parse(metrics.Data))
        {
            Assert.True(doc.RootElement.GetProperty("databases").GetInt32() >= 1);
            Assert.True(doc.RootElement.GetProperty("subscriberCount").GetInt32() >= 1);
        }

        // 6) DROP → db.dropped
        var drop = await apiClient.DeleteAsync($"/v1/db/{dbName}");
        Assert.Equal(HttpStatusCode.OK, drop.StatusCode);
        var dropEvt = await ReadEventOfTypeAsync(reader, buffer, "db", TimeSpan.FromSeconds(5));
        using (var doc = JsonDocument.Parse(dropEvt.Data))
        {
            Assert.Equal(dbName, doc.RootElement.GetProperty("database").GetString());
            Assert.Equal("dropped", doc.RootElement.GetProperty("action").GetString());
        }
    }

    private static async Task<(string Event, string Data)> ReadEventOfTypeAsync(
        StreamReader reader, StringBuilder buffer, string expectedType, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;
            var evt = await ReadOneEventAsync(reader, buffer, remaining);
            if (evt.Event == expectedType)
                return evt;
            // 忽略其他通道（hello / metrics / db / slow_query）继续等
        }
        throw new TimeoutException($"未在 {timeout.TotalSeconds:F1}s 内收到 type='{expectedType}' 事件。");
    }

    /// <summary>
    /// 从 SSE 流读取一个完整事件（以空行结尾）。注释行（": ..."）被跳过。
    /// </summary>
    private static async Task<(string Event, string Data)> ReadOneEventAsync(
        StreamReader reader, StringBuilder buffer, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        string evtType = "message";
        var data = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
            if (line is null)
                throw new IOException("SSE 流已关闭。");
            if (line.Length == 0)
            {
                if (data.Length > 0 || evtType != "message")
                    return (evtType, data.ToString().TrimEnd('\n'));
                continue; // 空事件，继续
            }
            if (line.StartsWith(':')) continue; // 注释 / 心跳
            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                evtType = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line["data: ".Length..]);
            }
            // 忽略 id: / retry:
        }
    }
}
