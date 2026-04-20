using TSLite.Engine;
using TSLite.Server.Auth;
using TSLite.Server.Hosting;
using TSLite.Sql;
using TSLite.Sql.Ast;
using TSLite.Sql.Execution;
using Xunit;

namespace TSLite.Server.Tests.Auth;

/// <summary>
/// PR #34a-4：服务端 <see cref="ControlPlane"/> + <see cref="SqlExecutor"/> 控制面 DDL 集成测试。
/// </summary>
public sealed class ControlPlaneTests : IDisposable
{
    private readonly string _dir;
    private readonly UserStore _users;
    private readonly GrantsStore _grants;
    private readonly TsdbRegistry _registry;
    private readonly ControlPlane _controlPlane;

    public ControlPlaneTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tslite-cp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        var systemDir = Path.Combine(_dir, ".system");
        Directory.CreateDirectory(systemDir);
        _users = new UserStore(systemDir);
        _grants = new GrantsStore(systemDir);
        _registry = new TsdbRegistry(_dir);
        _controlPlane = new ControlPlane(_users, _grants, _registry);
    }

    public void Dispose()
    {
        _registry.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void CreateUser_ViaSql_PersistsAndAuthenticates()
    {
        var bootstrap = Tsdb.Open(new TSLite.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        try
        {
            SqlExecutor.Execute(bootstrap, "CREATE USER alice WITH PASSWORD 'pa$$'", _controlPlane);
            Assert.True(_users.VerifyPassword("alice", "pa$$"));
        }
        finally { bootstrap.Dispose(); }
    }

    [Fact]
    public void CreateDatabase_ViaSql_RegistersInRegistry()
    {
        using var bootstrap = Tsdb.Open(new TSLite.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        SqlExecutor.Execute(bootstrap, "CREATE DATABASE metrics", _controlPlane);
        Assert.True(_registry.TryGet("metrics", out _));
    }

    [Fact]
    public void GrantAndRevoke_ViaSql_FlowsToGrantsStore()
    {
        using var bootstrap = Tsdb.Open(new TSLite.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        SqlExecutor.Execute(bootstrap, "CREATE USER alice WITH PASSWORD 'p'", _controlPlane);
        SqlExecutor.Execute(bootstrap, "CREATE DATABASE metrics", _controlPlane);

        SqlExecutor.Execute(bootstrap, "GRANT WRITE ON DATABASE metrics TO alice", _controlPlane);
        Assert.Equal(DatabasePermission.Write, _grants.GetPermission("alice", "metrics"));

        SqlExecutor.Execute(bootstrap, "REVOKE ON DATABASE metrics FROM alice", _controlPlane);
        Assert.Equal(DatabasePermission.None, _grants.GetPermission("alice", "metrics"));
    }

    [Fact]
    public void DropUser_ViaSql_AlsoDeletesGrants()
    {
        using var bootstrap = Tsdb.Open(new TSLite.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        SqlExecutor.Execute(bootstrap, "CREATE USER alice WITH PASSWORD 'p'", _controlPlane);
        SqlExecutor.Execute(bootstrap, "CREATE DATABASE metrics", _controlPlane);
        SqlExecutor.Execute(bootstrap, "GRANT READ ON DATABASE metrics TO alice", _controlPlane);

        SqlExecutor.Execute(bootstrap, "DROP USER alice", _controlPlane);
        Assert.False(_users.Exists("alice"));
        Assert.Equal(DatabasePermission.None, _grants.GetPermission("alice", "metrics"));
    }

    [Fact]
    public void DropDatabase_ViaSql_RemovesFromRegistry_AndCascadesGrants()
    {
        using var bootstrap = Tsdb.Open(new TSLite.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        SqlExecutor.Execute(bootstrap, "CREATE USER alice WITH PASSWORD 'p'", _controlPlane);
        SqlExecutor.Execute(bootstrap, "CREATE DATABASE metrics", _controlPlane);
        SqlExecutor.Execute(bootstrap, "GRANT READ ON DATABASE metrics TO alice", _controlPlane);

        SqlExecutor.Execute(bootstrap, "DROP DATABASE metrics", _controlPlane);
        Assert.False(_registry.TryGet("metrics", out _));
        Assert.Equal(DatabasePermission.None, _grants.GetPermission("alice", "metrics"));
    }

    [Fact]
    public void AlterUser_ViaSql_ChangesPasswordAndRevokesTokens()
    {
        using var bootstrap = Tsdb.Open(new TSLite.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        SqlExecutor.Execute(bootstrap, "CREATE USER alice WITH PASSWORD 'old'", _controlPlane);
        var (token, _) = _users.IssueToken("alice");

        SqlExecutor.Execute(bootstrap, "ALTER USER alice WITH PASSWORD 'new'", _controlPlane);
        Assert.True(_users.VerifyPassword("alice", "new"));
        Assert.False(_users.VerifyPassword("alice", "old"));
        Assert.False(_users.TryAuthenticate(token, out _));
    }

    [Fact]
    public void Grant_OnNonexistentDatabase_Throws()
    {
        using var bootstrap = Tsdb.Open(new TSLite.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        SqlExecutor.Execute(bootstrap, "CREATE USER alice WITH PASSWORD 'p'", _controlPlane);
        Assert.Throws<InvalidOperationException>(() =>
            SqlExecutor.Execute(bootstrap, "GRANT READ ON DATABASE missing TO alice", _controlPlane));
    }

    [Fact]
    public void Grant_OnWildcardDatabase_DoesNotRequireExistingDatabase()
    {
        using var bootstrap = Tsdb.Open(new TSLite.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        SqlExecutor.Execute(bootstrap, "CREATE USER alice WITH PASSWORD 'p'", _controlPlane);
        SqlExecutor.Execute(bootstrap, "GRANT ADMIN ON DATABASE * TO alice", _controlPlane);
        Assert.Equal(DatabasePermission.Admin, _grants.GetPermission("alice", "any-db"));
    }

    [Fact]
    public void ControlPlaneDdl_WithoutControlPlane_Throws()
    {
        using var bootstrap = Tsdb.Open(new TSLite.Engine.TsdbOptions { RootDirectory = Path.Combine(_dir, "_bootstrap") });
        Assert.Throws<NotSupportedException>(() =>
            SqlExecutor.Execute(bootstrap, "CREATE USER alice WITH PASSWORD 'p'"));
    }
}
