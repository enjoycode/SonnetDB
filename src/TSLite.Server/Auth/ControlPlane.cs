using TSLite.Server.Auth;
using TSLite.Server.Hosting;
using TSLite.Sql.Ast;
using TSLite.Sql.Execution;

namespace TSLite.Server.Auth;

/// <summary>
/// 服务端 <see cref="IControlPlane"/> 实现：把控制面 DDL 翻译为
/// <see cref="UserStore"/> / <see cref="GrantsStore"/> / <see cref="TsdbRegistry"/> 操作。
/// </summary>
/// <remarks>
/// 所有方法是线程安全的，依赖底层 store 的内部锁；
/// 不在执行器层做权限校验，调用者（鉴权中间件）需保证只有超级用户才能触发。
/// </remarks>
public sealed class ControlPlane : IControlPlane
{
    private readonly UserStore _users;
    private readonly GrantsStore _grants;
    private readonly TsdbRegistry _registry;

    /// <summary>构造服务端控制面。</summary>
    public ControlPlane(UserStore users, GrantsStore grants, TsdbRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(registry);
        _users = users;
        _grants = grants;
        _registry = registry;
    }

    /// <inheritdoc />
    public void CreateUser(string userName, string password, bool isSuperuser)
        => _users.CreateUser(userName, password, isSuperuser);

    /// <inheritdoc />
    public void AlterUserPassword(string userName, string newPassword)
        => _users.ChangePassword(userName, newPassword);

    /// <inheritdoc />
    public void DropUser(string userName)
    {
        if (!_users.DeleteUser(userName))
            throw new InvalidOperationException($"用户 '{userName}' 不存在。");
        _grants.DeleteUserGrants(userName);
    }

    /// <inheritdoc />
    public void Grant(string userName, string database, GrantPermission permission)
    {
        EnsureUserExists(userName);
        EnsureDatabaseExistsOrWildcard(database);
        _grants.Grant(userName, database, MapPermission(permission));
    }

    /// <inheritdoc />
    public void Revoke(string userName, string database)
    {
        EnsureUserExists(userName);
        _grants.Revoke(userName, database);
    }

    /// <inheritdoc />
    public void CreateDatabase(string databaseName)
    {
        if (!_registry.TryCreate(databaseName, out _))
            throw new InvalidOperationException($"数据库 '{databaseName}' 已存在。");
    }

    /// <inheritdoc />
    public void DropDatabase(string databaseName)
    {
        if (!_registry.Drop(databaseName))
            throw new InvalidOperationException($"数据库 '{databaseName}' 不存在。");
        _grants.DeleteDatabaseGrants(databaseName);
    }

    private void EnsureUserExists(string userName)
    {
        if (!_users.Exists(userName))
            throw new InvalidOperationException($"用户 '{userName}' 不存在。");
    }

    private void EnsureDatabaseExistsOrWildcard(string database)
    {
        if (database == "*") return;
        if (!_registry.TryGet(database, out _))
            throw new InvalidOperationException($"数据库 '{database}' 不存在。");
    }

    private static DatabasePermission MapPermission(GrantPermission permission) => permission switch
    {
        GrantPermission.Read => DatabasePermission.Read,
        GrantPermission.Write => DatabasePermission.Write,
        GrantPermission.Admin => DatabasePermission.Admin,
        _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, "未知的 GRANT 权限级别。"),
    };
}
