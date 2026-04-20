using System.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using TSLite.Data;
using TSLite.Data.Remote;
using TSLite.Server.Configuration;
using Xunit;

namespace TSLite.Server.Tests;

/// <summary>
/// 端到端测试：启动真实 Kestrel + 用 <see cref="TsdbConnection"/> 远程模式作为客户端调用。
/// 验证 PR #33：远程客户端 + 嵌入式客户端共享同一套 ADO.NET API，仅 ConnectionString scheme 不同。
/// </summary>
public sealed class RemoteAdoEndToEndTests : IAsyncLifetime
{
    private WebApplication? _app;
    private string _baseUrl = string.Empty;
    private string? _dataRoot;
    private const string AdminToken = "remote-admin";
    private const string ReadOnlyToken = "remote-ro";
    private const string DbName = "remote_e2e";

    public async Task InitializeAsync()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "tslite-remote-ado-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRoot);

        var options = new ServerOptions
        {
            DataRoot = _dataRoot,
            AutoLoadExistingDatabases = true,
            AllowAnonymousProbes = true,
            Tokens = new Dictionary<string, string>
            {
                [AdminToken] = ServerRoles.Admin,
                [ReadOnlyToken] = ServerRoles.ReadOnly,
            },
        };
        _app = Program.BuildApp(["--Kestrel:Endpoints:Http:Url=http://127.0.0.1:0"], options);
        await _app.StartAsync();
        var addresses = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel 未暴露监听地址。");
        _baseUrl = addresses.Addresses.First();

        // 创建数据库
        using var http = new HttpClient { BaseAddress = new Uri(_baseUrl) };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AdminToken);
        var resp = await http.PostAsync("/v1/db", new StringContent(
            $"{{\"name\":\"{DbName}\"}}", System.Text.Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
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

    private string RemoteConnString(string token = AdminToken)
        => $"Data Source=tslite+http://{new Uri(_baseUrl).Authority}/{DbName};Token={token};Timeout=30";

    private TsdbConnection OpenRemote(string token = AdminToken)
    {
        var c = new TsdbConnection(RemoteConnString(token));
        c.Open();
        return c;
    }

    [Fact]
    public void ConnectionString_Scheme_DispatchesToRemote()
    {
        using var c = OpenRemote();
        Assert.Equal(TsdbProviderMode.Remote, c.ProviderMode);
        Assert.Equal(ConnectionState.Open, c.State);
        Assert.Null(c.UnderlyingTsdb); // 远程模式没有本地 Tsdb
        Assert.Equal(DbName, c.Database);
    }

    [Fact]
    public void EmbeddedConnectionString_StaysEmbedded()
    {
        // 与远程同一连接字符串体系，仅 scheme 不同 → 自动走嵌入式实现
        var path = Path.Combine(Path.GetTempPath(), "tslite-emb-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var c = new TsdbConnection($"Data Source={path}");
            c.Open();
            Assert.Equal(TsdbProviderMode.Embedded, c.ProviderMode);
            Assert.NotNull(c.UnderlyingTsdb);
        }
        finally
        {
            try { Directory.Delete(path, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Remote_CreateInsertSelect_RoundTrip()
    {
        using var c = OpenRemote();
        using (var ddl = c.CreateCommand())
        {
            ddl.CommandText = "CREATE MEASUREMENT cpu (host TAG, value FIELD FLOAT)";
            ddl.ExecuteNonQuery();
        }
        using (var ins = c.CreateCommand())
        {
            ins.CommandText = "INSERT INTO cpu (time, host, value) VALUES (1000, 'a', 1.5), (2000, 'a', 2.5)";
            Assert.Equal(2, ins.ExecuteNonQuery());
        }
        using var sel = c.CreateCommand();
        sel.CommandText = "SELECT time, host, value FROM cpu";
        using var r = sel.ExecuteReader();

        Assert.Equal(3, r.FieldCount);
        Assert.Equal("time", r.GetName(0));
        Assert.True(r.HasRows);
        Assert.True(r.Read());
        Assert.Equal(1000L, r.GetInt64(0));
        Assert.Equal("a", r.GetString(1));
        Assert.Equal(1.5, r.GetDouble(2));
        Assert.True(r.Read());
        Assert.Equal(2000L, r.GetInt64(0));
        Assert.False(r.Read());
        Assert.Equal(-1, r.RecordsAffected);
    }

    [Fact]
    public void Remote_Parameters_AreInlinedAndEscaped()
    {
        using var c = OpenRemote();
        using (var ddl = c.CreateCommand())
        {
            ddl.CommandText = "CREATE MEASUREMENT m1 (host TAG, v FIELD FLOAT)";
            ddl.ExecuteNonQuery();
        }
        using (var ins = c.CreateCommand())
        {
            ins.CommandText = "INSERT INTO m1 (time, host, v) VALUES (@t, @h, @v)";
            ins.Parameters.AddWithValue("@t", 5000L);
            // 含单引号的字符串：应被安全转义，不会引发服务端 SQL 错误
            ins.Parameters.AddWithValue("@h", "o'reilly");
            ins.Parameters.AddWithValue("@v", 7.25);
            Assert.Equal(1, ins.ExecuteNonQuery());
        }
        using var sel = c.CreateCommand();
        sel.CommandText = "SELECT host, v FROM m1 WHERE host = @h";
        sel.Parameters.AddWithValue("@h", "o'reilly");
        using var r = sel.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal("o'reilly", r.GetString(0));
        Assert.Equal(7.25, r.GetDouble(1));
        Assert.False(r.Read());
    }

    [Fact]
    public void Remote_ExecuteScalar_ReturnsCount()
    {
        using var c = OpenRemote();
        using (var ddl = c.CreateCommand())
        {
            ddl.CommandText = "CREATE MEASUREMENT m2 (host TAG, v FIELD INT)";
            ddl.ExecuteNonQuery();
        }
        using (var ins = c.CreateCommand())
        {
            ins.CommandText = "INSERT INTO m2 (time, host, v) VALUES (1, 'a', 1), (2, 'a', 2), (3, 'a', 3)";
            ins.ExecuteNonQuery();
        }
        using var cnt = c.CreateCommand();
        cnt.CommandText = "SELECT count(*) FROM m2";
        var v = cnt.ExecuteScalar();
        Assert.Equal(3L, v);
    }

    [Fact]
    public void Remote_ReadOnlyToken_InsertForbidden()
    {
        // 先用 admin 建表
        using (var admin = OpenRemote(AdminToken))
        using (var ddl = admin.CreateCommand())
        {
            ddl.CommandText = "CREATE MEASUREMENT m3 (host TAG, v FIELD FLOAT)";
            ddl.ExecuteNonQuery();
        }

        using var c = OpenRemote(ReadOnlyToken);
        using var ins = c.CreateCommand();
        ins.CommandText = "INSERT INTO m3 (time, host, v) VALUES (1, 'a', 1)";
        var ex = Assert.Throws<TsdbServerException>(() => ins.ExecuteNonQuery());
        Assert.Equal("forbidden", ex.Error);
    }

    [Fact]
    public void Remote_BadSql_ThrowsTsdbServerException()
    {
        using var c = OpenRemote();
        using var bad = c.CreateCommand();
        bad.CommandText = "SELECT FROM nope_table";
        var ex = Assert.Throws<TsdbServerException>(() => bad.ExecuteNonQuery());
        Assert.Equal("sql_error", ex.Error);
    }

    [Fact]
    public void Remote_MissingToken_Unauthorized()
    {
        var cs = $"Data Source=tslite+http://{new Uri(_baseUrl).Authority}/{DbName}";
        using var c = new TsdbConnection(cs);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM whatever";
        var ex = Assert.Throws<TsdbServerException>(() => cmd.ExecuteNonQuery());
        Assert.Equal("unauthorized", ex.Error);
    }

    [Fact]
    public void Remote_UnknownDatabase_NotFound()
    {
        var cs = $"Data Source=tslite+http://{new Uri(_baseUrl).Authority}/no_such_db;Token={AdminToken}";
        using var c = new TsdbConnection(cs);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM x";
        var ex = Assert.Throws<TsdbServerException>(() => cmd.ExecuteNonQuery());
        Assert.Equal("db_not_found", ex.Error);
    }
}
