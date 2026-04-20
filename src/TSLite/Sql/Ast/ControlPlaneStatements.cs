namespace TSLite.Sql.Ast;

// PR #34a：控制面（用户、权限、数据库）DDL AST。
// 这些语句**仅在服务端模式 + 注入了 IControlPlane 的情况下**可执行；
// 嵌入式模式（无控制面）SqlExecutor 会抛 NotSupportedException。

/// <summary><c>CREATE USER name WITH PASSWORD 'pwd' [SUPERUSER]</c>。</summary>
/// <param name="UserName">用户名。</param>
/// <param name="Password">明文密码。</param>
/// <param name="IsSuperuser">是否超级用户（PR #34b-3 起 grammar 解析尾部可选 <c>SUPERUSER</c>）。</param>
public sealed record CreateUserStatement(
    string UserName,
    string Password,
    bool IsSuperuser) : SqlStatement;

/// <summary><c>ALTER USER name WITH PASSWORD 'pwd'</c>。</summary>
/// <param name="UserName">用户名。</param>
/// <param name="NewPassword">新明文密码。</param>
public sealed record AlterUserPasswordStatement(
    string UserName,
    string NewPassword) : SqlStatement;

/// <summary><c>DROP USER name</c>。</summary>
/// <param name="UserName">用户名。</param>
public sealed record DropUserStatement(string UserName) : SqlStatement;

/// <summary><c>GRANT READ|WRITE|ADMIN ON DATABASE db TO user</c>。</summary>
/// <param name="Permission">授权级别。</param>
/// <param name="Database">数据库名（<c>*</c> 表示全部）。</param>
/// <param name="UserName">被授权用户。</param>
public sealed record GrantStatement(
    GrantPermission Permission,
    string Database,
    string UserName) : SqlStatement;

/// <summary><c>REVOKE ON DATABASE db FROM user</c>。撤销 (user, db) 的全部权限。</summary>
/// <param name="Database">数据库名（<c>*</c> 表示全部）。</param>
/// <param name="UserName">被撤销用户。</param>
public sealed record RevokeStatement(
    string Database,
    string UserName) : SqlStatement;

/// <summary><c>CREATE DATABASE name</c>。</summary>
/// <param name="DatabaseName">数据库名。</param>
public sealed record CreateDatabaseStatement(string DatabaseName) : SqlStatement;

/// <summary><c>DROP DATABASE name</c>。</summary>
/// <param name="DatabaseName">数据库名。</param>
public sealed record DropDatabaseStatement(string DatabaseName) : SqlStatement;

/// <summary>SQL 层 GRANT 的权限关键字。映射到服务端 <c>DatabasePermission</c>。</summary>
public enum GrantPermission
{
    /// <summary>SELECT。</summary>
    Read = 1,
    /// <summary>SELECT / INSERT / DELETE / CREATE MEASUREMENT。</summary>
    Write = 2,
    /// <summary>所有 DML/DDL（不含用户管理）。</summary>
    Admin = 3,
}

// PR #34b-1：SHOW 控制面查询语句。结果统一映射到 SelectExecutionResult，
// 走与 SELECT 相同的 ndjson 渲染管线。

/// <summary><c>SHOW USERS</c>：列出所有用户。返回列：<c>name</c>(string)、<c>is_superuser</c>(bool)、<c>created_utc</c>(string ISO-8601)、<c>token_count</c>(long)。</summary>
public sealed record ShowUsersStatement : SqlStatement;

/// <summary>
/// <c>SHOW GRANTS [FOR &lt;user&gt;]</c>：列出全部 grants 或某用户的 grants。
/// 返回列：<c>user_name</c>(string)、<c>database</c>(string)、<c>permission</c>(string，"Read"/"Write"/"Admin")。
/// </summary>
/// <param name="UserName">可选用户名筛选；<c>null</c> 表示列出全部。</param>
public sealed record ShowGrantsStatement(string? UserName) : SqlStatement;

/// <summary><c>SHOW DATABASES</c>：列出所有已注册数据库。返回列：<c>name</c>(string)。</summary>
public sealed record ShowDatabasesStatement : SqlStatement;

