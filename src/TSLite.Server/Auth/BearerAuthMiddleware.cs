using Microsoft.AspNetCore.Http;
using TSLite.Server.Configuration;

namespace TSLite.Server.Auth;

/// <summary>
/// 极简 Bearer token 认证中间件：
/// <list type="bullet">
///   <item>从请求头 <c>Authorization: Bearer &lt;token&gt;</c> 提取 token；</item>
///   <item>先查 <see cref="ServerOptions.Tokens"/>（静态 token → 角色映射）；</item>
///   <item>未命中则查 <see cref="UserStore"/>（PR #34a 引入：动态颁发的用户 token）；</item>
///   <item>把角色与可选的 <see cref="AuthenticatedUser"/> 写入 <see cref="HttpContext.Items"/>，
///       供后续 endpoint 读取。</item>
/// </list>
/// 不引入 ASP.NET Core 的 <c>AddAuthentication</c> + <c>AddJwtBearer</c> —— 它们的反射量较大，AOT 不友好。
/// </summary>
public static class BearerAuthMiddleware
{
    /// <summary>
    /// 角色键。endpoint 通过 <c>HttpContext.Items[RoleKey]</c> 取出当前调用者角色（可能为空）。
    /// </summary>
    public const string RoleKey = "tslite.role";

    /// <summary>
    /// 已认证用户键。仅当 token 通过 <see cref="UserStore"/> 命中时设置。
    /// </summary>
    public const string UserKey = "tslite.user";

    /// <summary>
    /// 检查并设置角色；若启用了 <see cref="ServerOptions.AllowAnonymousProbes"/>，<c>/healthz</c>
    /// 与 <c>/metrics</c> 跳过认证。<c>/v1/auth/login</c> 始终匿名（用户名密码即凭证）。
    /// </summary>
    /// <returns>
    /// <c>null</c> 表示通过；否则为该返回的 HTTP 状态码（401 / 403）。
    /// </returns>
    public static int? Authenticate(HttpContext context, ServerOptions options, UserStore? userStore)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (options.AllowAnonymousProbes && (path.Equals("/healthz", StringComparison.Ordinal) || path.Equals("/metrics", StringComparison.Ordinal)))
        {
            return null;
        }
        if (path.Equals("/v1/auth/login", StringComparison.Ordinal))
        {
            return null;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return StatusCodes.Status401Unauthorized;
        }
        var token = header["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return StatusCodes.Status401Unauthorized;
        }

        // 1) 静态 token 表（appsettings 注入）
        if (options.Tokens.TryGetValue(token, out var role))
        {
            context.Items[RoleKey] = role;
            return null;
        }

        // 2) UserStore 动态 token（CREATE USER + IssueToken 颁发）
        if (userStore is not null && userStore.TryAuthenticate(token, out var user))
        {
            context.Items[UserKey] = user;
            // 用户态下，超级用户映射到 admin 角色；普通用户暂时映射到 readwrite
            // （SELECT/INSERT/DELETE 实际权限由 GrantsStore 在 endpoint 层细分；
            //  此处的 role 仅用于兼容现有 IsAdmin/CanWrite 短路检查）。
            context.Items[RoleKey] = user.IsSuperuser ? ServerRoles.Admin : ServerRoles.ReadWrite;
            return null;
        }

        return StatusCodes.Status401Unauthorized;
    }

    /// <summary>
    /// 从当前请求里取出已认证角色（中间件填充）。
    /// </summary>
    public static string? GetRole(HttpContext context)
        => context.Items.TryGetValue(RoleKey, out var v) ? v as string : null;

    /// <summary>
    /// 从当前请求里取出已认证用户；仅当 token 通过 <see cref="UserStore"/> 命中时返回非 null。
    /// </summary>
    public static AuthenticatedUser? GetUser(HttpContext context)
        => context.Items.TryGetValue(UserKey, out var v) && v is AuthenticatedUser u ? u : null;

    /// <summary>
    /// 判断角色是否拥有写入权限（admin / readwrite）。
    /// </summary>
    public static bool CanWrite(string? role)
        => role is ServerRoles.Admin or ServerRoles.ReadWrite;

    /// <summary>
    /// 判断角色是否拥有管理员权限（admin）。
    /// </summary>
    public static bool IsAdmin(string? role)
        => role == ServerRoles.Admin;
}
