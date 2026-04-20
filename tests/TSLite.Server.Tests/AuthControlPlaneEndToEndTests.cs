using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using TSLite.Server.Auth;
using TSLite.Server.Configuration;
using TSLite.Server.Contracts;
using TSLite.Server.Json;
using Xunit;

namespace TSLite.Server.Tests;

/// <summary>
/// PR #34a-5：服务端用户/权限/控制面 SQL 端到端测试。
/// </summary>
public sealed class AuthControlPlaneEndToEndTests : IAsyncLifetime
{
    private WebApplication? _app;
    private string? _baseUrl;
    private string? _dataRoot;
    private const string AdminStaticToken = "static-admin-token";

    public async Task InitializeAsync()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "tslite-auth-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRoot);

        var options = new ServerOptions
        {
            DataRoot = _dataRoot,
            AutoLoadExistingDatabases = true,
            AllowAnonymousProbes = true,
            Tokens = new Dictionary<string, string>
            {
                [AdminStaticToken] = ServerRoles.Admin,
            },
        };

        _app = Program.BuildApp(["--Kestrel:Endpoints:Http:Url=http://127.0.0.1:0"], options);
        await _app.StartAsync();

        var addresses = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel 未暴露监听地址。");
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
            try { Directory.Delete(_dataRoot, recursive: true); } catch { /* best-effort */ }
        }
    }

    private HttpClient CreateClient(string? token)
    {
        var client = new HttpClient { BaseAddress = new Uri(_baseUrl!) };
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Login_WithoutCredentials_Returns400()
    {
        using var client = CreateClient(token: null);
        var resp = await client.PostAsync("/v1/auth/login",
            JsonContent.Create(new LoginRequest("", ""), ServerJsonContext.Default.LoginRequest));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownUser_Returns401()
    {
        using var client = CreateClient(token: null);
        var resp = await client.PostAsync("/v1/auth/login",
            JsonContent.Create(new LoginRequest("ghost", "irrelevant"), ServerJsonContext.Default.LoginRequest));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task EndToEnd_CreateUser_Login_AndUseToken()
    {
        // 1) 用静态 admin token 通过 SQL 端点创建用户 + 数据库 + 授权
        await CreateDatabaseAsync("metrics");
        await ExecuteSqlAsync("metrics", "CREATE USER alice WITH PASSWORD 'pa$$'", AdminStaticToken);
        await ExecuteSqlAsync("metrics", "GRANT WRITE ON DATABASE metrics TO alice", AdminStaticToken);

        // 2) alice 用密码登录获取 token
        using var anon = CreateClient(token: null);
        var loginResp = await anon.PostAsync("/v1/auth/login",
            JsonContent.Create(new LoginRequest("alice", "pa$$"), ServerJsonContext.Default.LoginRequest));
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var login = await loginResp.Content.ReadFromJsonAsync<LoginResponse>(ServerJsonContext.Default.LoginResponse);
        Assert.NotNull(login);
        Assert.Equal("alice", login!.Username);
        Assert.False(login.IsSuperuser);
        Assert.False(string.IsNullOrEmpty(login.Token));
        Assert.StartsWith("tok_", login.TokenId);

        // 3) 用动态 token 调用 /healthz 与 SQL（应通过认证）
        using var alice = CreateClient(login.Token);
        var hz = await alice.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, hz.StatusCode);

        // 4) alice 是普通用户，无法执行控制面 DDL
        var ddlResp = await alice.PostAsync("/v1/db/metrics/sql",
            JsonContent.Create(new SqlRequest("CREATE USER bob WITH PASSWORD 'p'"), ServerJsonContext.Default.SqlRequest));
        Assert.Equal(HttpStatusCode.Forbidden, ddlResp.StatusCode);
    }

    [Fact]
    public async Task ControlPlaneDdl_WithNonAdminDynamicToken_IsForbidden()
    {
        await CreateDatabaseAsync("foo");
        await ExecuteSqlAsync("foo", "CREATE USER carol WITH PASSWORD 'p'", AdminStaticToken);

        using var anon = CreateClient(token: null);
        var loginResp = await anon.PostAsync("/v1/auth/login",
            JsonContent.Create(new LoginRequest("carol", "p"), ServerJsonContext.Default.LoginRequest));
        var login = await loginResp.Content.ReadFromJsonAsync<LoginResponse>(ServerJsonContext.Default.LoginResponse);
        Assert.NotNull(login);

        using var carol = CreateClient(login!.Token);
        var resp = await carol.PostAsync("/v1/db/foo/sql",
            JsonContent.Create(new SqlRequest("DROP USER ghost"), ServerJsonContext.Default.SqlRequest));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task RevokedToken_AfterAlterUserPassword_FailsAuth()
    {
        await CreateDatabaseAsync("m1");
        await ExecuteSqlAsync("m1", "CREATE USER dave WITH PASSWORD 'old'", AdminStaticToken);

        using var anon = CreateClient(token: null);
        var loginResp = await anon.PostAsync("/v1/auth/login",
            JsonContent.Create(new LoginRequest("dave", "old"), ServerJsonContext.Default.LoginRequest));
        var login = await loginResp.Content.ReadFromJsonAsync<LoginResponse>(ServerJsonContext.Default.LoginResponse);
        Assert.NotNull(login);

        // admin 改密码 → dave 旧 token 失效
        await ExecuteSqlAsync("m1", "ALTER USER dave WITH PASSWORD 'new'", AdminStaticToken);

        using var dave = CreateClient(login!.Token);
        var hz = await dave.GetAsync("/v1/db");
        Assert.Equal(HttpStatusCode.Unauthorized, hz.StatusCode);
    }

    private async Task CreateDatabaseAsync(string name)
    {
        using var admin = CreateClient(AdminStaticToken);
        var resp = await admin.PostAsync("/v1/db",
            JsonContent.Create(new CreateDatabaseRequest(name), ServerJsonContext.Default.CreateDatabaseRequest));
        Assert.True(resp.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            $"创建数据库 {name} 失败：{resp.StatusCode}");
    }

    private async Task ExecuteSqlAsync(string db, string sql, string token)
    {
        using var client = CreateClient(token);
        var resp = await client.PostAsync($"/v1/db/{db}/sql",
            JsonContent.Create(new SqlRequest(sql), ServerJsonContext.Default.SqlRequest));
        Assert.True(resp.IsSuccessStatusCode, $"SQL '{sql}' 失败：{resp.StatusCode} / {await resp.Content.ReadAsStringAsync()}");
    }
}
