using Microsoft.AspNetCore.Http;
using SonnetDB.Configuration;

namespace SonnetDB.Auth;

/// <summary>
/// 计算当前请求对指定数据库的有效权限。
/// </summary>
internal static class DatabaseAccessEvaluator
{
    /// <summary>
    /// 解析当前请求对指定数据库的有效权限。
    /// </summary>
    public static DatabasePermission GetEffectivePermission(
        HttpContext context,
        GrantsStore grantsStore,
        string database)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(grantsStore);
        ArgumentException.ThrowIfNullOrEmpty(database);

        var user = BearerAuthMiddleware.GetUser(context);
        if (user is AuthenticatedUser authenticatedUser)
        {
            if (authenticatedUser.IsSuperuser)
                return DatabasePermission.Admin;

            return grantsStore.GetPermission(authenticatedUser.UserName, database);
        }

        return BearerAuthMiddleware.GetRole(context) switch
        {
            ServerRoles.Admin => DatabasePermission.Admin,
            ServerRoles.ReadWrite => DatabasePermission.Write,
            ServerRoles.ReadOnly => DatabasePermission.Read,
            _ => DatabasePermission.None,
        };
    }

    /// <summary>
    /// 判断有效权限是否满足最低要求。
    /// </summary>
    public static bool HasPermission(DatabasePermission actual, DatabasePermission required)
        => actual >= required;

    /// <summary>
    /// 获取当前请求可见的数据库列表。
    /// </summary>
    public static IReadOnlyList<string> GetVisibleDatabases(
        HttpContext context,
        GrantsStore grantsStore,
        IReadOnlyList<string> allDatabases)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(grantsStore);
        ArgumentNullException.ThrowIfNull(allDatabases);

        var user = BearerAuthMiddleware.GetUser(context);
        if (user is AuthenticatedUser authenticatedUser)
        {
            if (authenticatedUser.IsSuperuser)
                return allDatabases;

            var visible = new List<string>(allDatabases.Count);
            foreach (var database in allDatabases)
            {
                if (grantsStore.GetPermission(authenticatedUser.UserName, database) >= DatabasePermission.Read)
                    visible.Add(database);
            }
            return visible;
        }

        return BearerAuthMiddleware.GetRole(context) is ServerRoles.Admin or ServerRoles.ReadWrite or ServerRoles.ReadOnly
            ? allDatabases
            : [];
    }

    /// <summary>
    /// 判断当前请求是否具备服务端管理员权限。
    /// </summary>
    public static bool IsServerAdmin(HttpContext context)
        => BearerAuthMiddleware.IsAdmin(BearerAuthMiddleware.GetRole(context));
}
