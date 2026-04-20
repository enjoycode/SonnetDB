using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using TSLite.Server.Configuration;
using Xunit;

namespace TSLite.Server.Tests;

/// <summary>
/// PR #34b-2：嵌入式 Admin SPA 静态资源端到端测试。
/// </summary>
public sealed class AdminUiEndToEndTests : IAsyncLifetime
{
    private WebApplication? _app;
    private string? _baseUrl;
    private string? _dataRoot;

    public async Task InitializeAsync()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "tslite-admin-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRoot);

        var options = new ServerOptions
        {
            DataRoot = _dataRoot,
            AllowAnonymousProbes = true,
        };

        _app = Program.BuildApp(["--Kestrel:Endpoints:Http:Url=http://127.0.0.1:0"], options);
        await _app.StartAsync();
        _baseUrl = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
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
            try { Directory.Delete(_dataRoot, recursive: true); } catch { /* best-effort */ }
        }
    }

    private HttpClient CreateClient() => new() { BaseAddress = new Uri(_baseUrl!) };

    [Fact]
    public async Task GetAdminRoot_AnonymouslyReturnsHtml()
    {
        using var client = CreateClient();
        var resp = await client.GetAsync("/admin");
        // 嵌入资源存在 → 200 + text/html；不存在 → 503（CI 未 build 前端时跳过）。
        if (resp.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            return;
        }
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.StartsWith("text/html", resp.Content.Headers.ContentType?.MediaType ?? string.Empty);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("<div id=\"app\">", body);
    }

    [Fact]
    public async Task GetAdminSubpath_FallsBackToIndexHtml()
    {
        using var client = CreateClient();
        var resp = await client.GetAsync("/admin/login");
        if (resp.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            return;
        }
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.StartsWith("text/html", resp.Content.Headers.ContentType?.MediaType ?? string.Empty);
    }

    [Fact]
    public async Task GetAdminAssetWithExtension_NotFound_Returns404()
    {
        using var client = CreateClient();
        var resp = await client.GetAsync("/admin/this-does-not-exist.js");
        if (!Hosting.AdminUiAssets.HasIndex)
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
            return;
        }
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetAdmin_DoesNotRequireBearerToken()
    {
        using var client = CreateClient();
        // 不带 Authorization
        var resp = await client.GetAsync("/admin/index.html");
        if (resp.StatusCode == HttpStatusCode.ServiceUnavailable) return;
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task GetAdminFavicon_ReturnsSvg()
    {
        using var client = CreateClient();
        var resp = await client.GetAsync("/admin/favicon.svg");
        if (resp.StatusCode == HttpStatusCode.ServiceUnavailable) return;
        if (resp.StatusCode == HttpStatusCode.NotFound) return; // favicon 可选
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("image/svg+xml", resp.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetOtherEndpoints_StillRequireAuth()
    {
        // 验证 admin 路径豁免不会误伤其他端点：/v1/db 仍需 Bearer。
        using var client = CreateClient();
        var resp = await client.GetAsync("/v1/db");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
