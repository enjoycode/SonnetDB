using TSLite.Sql.Ast;

namespace TSLite.Sql.Execution;

/// <summary>
/// 控制面（用户、权限、数据库管理）操作抽象。
/// </summary>
/// <remarks>
/// <para>
/// 仅在<b>服务端模式</b>由 <c>TSLite.Server</c> 注入实现；嵌入式 <see cref="Tsdb"/> 调用方
/// 不应实现该接口。<see cref="SqlExecutor"/> 在执行控制面 DDL（CREATE USER / GRANT 等）时，
/// 若未传入 <see cref="IControlPlane"/>，将抛出 <see cref="NotSupportedException"/>。
/// </para>
/// <para>
/// 实现需保证线程安全；持久化由实现自身负责（典型实现是原子写 JSON 文件）。
/// </para>
/// </remarks>
public interface IControlPlane
{
    /// <summary>创建用户；若同名用户已存在抛 <see cref="InvalidOperationException"/>。</summary>
    /// <param name="userName">用户名。</param>
    /// <param name="password">明文密码（不持久化，立即 PBKDF2 哈希）。</param>
    /// <param name="isSuperuser">是否超级用户。</param>
    void CreateUser(string userName, string password, bool isSuperuser);

    /// <summary>修改用户密码；若用户不存在抛 <see cref="InvalidOperationException"/>。</summary>
    /// <param name="userName">用户名。</param>
    /// <param name="newPassword">新密码。</param>
    void AlterUserPassword(string userName, string newPassword);

    /// <summary>删除用户及其所有 token、grant；若用户不存在抛 <see cref="InvalidOperationException"/>。</summary>
    /// <param name="userName">用户名。</param>
    void DropUser(string userName);

    /// <summary>授予用户在某数据库上的权限（已存在更高权限则保持不变）。</summary>
    /// <param name="userName">用户名。</param>
    /// <param name="database">数据库名（<c>*</c> 表示全部）。</param>
    /// <param name="permission">权限级别。</param>
    void Grant(string userName, string database, GrantPermission permission);

    /// <summary>撤销用户在某数据库上的全部权限。</summary>
    /// <param name="userName">用户名。</param>
    /// <param name="database">数据库名（<c>*</c> 表示全部）。</param>
    void Revoke(string userName, string database);

    /// <summary>创建数据库；若同名数据库已存在抛 <see cref="InvalidOperationException"/>。</summary>
    /// <param name="databaseName">数据库名。</param>
    void CreateDatabase(string databaseName);

    /// <summary>删除数据库（含其所有 measurement、segment、grant）。</summary>
    /// <param name="databaseName">数据库名。</param>
    void DropDatabase(string databaseName);
}
