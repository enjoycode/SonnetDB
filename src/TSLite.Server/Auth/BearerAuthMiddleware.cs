using Microsoft.AspNetCore.Http;
using TSLite.Server.Configuration;

namespace TSLite.Server.Auth;

/// <summary>
/// 极简 Bearer Token 认证中间件。
/// </summary>
public static class BearerAuthMiddleware
{
    /// <summary>
    /// 当前请求角色在 <see cref="HttpContext.Items"/> 中的键。
    /// </summary>
    public const string RoleKey = "tslite.role";

    /// <summary>
    /// 当前请求用户在 <see cref="HttpContext.Items"/> 中的键。
    /// </summary>
    public const string UserKey = "tslite.user";

    /// <summary>
    /// 执行 Bearer Token 认证；返回 <c>null</c> 表示通过，否则返回应写出的 HTTP 状态码。
    /// </summary>
    /// <param name="context">当前 HTTP 上下文。</param>
    /// <param name="options">服务端配置。</param>
    /// <param name="userStore">用户与动态 Token 存储。</param>
    /// <returns>认证通过时返回 <c>null</c>，失败时返回状态码。</returns>
    public static int? Authenticate(HttpContext context, ServerOptions options, UserStore? userStore)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (options.AllowAnonymousProbes
            && (path.Equals("/healthz", StringComparison.Ordinal) || path.Equals("/metrics", StringComparison.Ordinal)))
        {
            return null;
        }

        if (path.Equals("/v1/auth/login", StringComparison.Ordinal))
        {
            return null;
        }

        if (path.Equals("/v1/setup/status", StringComparison.Ordinal)
            || path.Equals("/v1/setup/initialize", StringComparison.Ordinal))
        {
            return null;
        }

        if (path.Equals("/help", StringComparison.Ordinal) || path.StartsWith("/help/", StringComparison.Ordinal))
        {
            return null;
        }

        // Admin SPA 静态资源匿名可读，真正的管理动作仍通过登录后的 API 执行。
        if (path.StartsWith("/admin", StringComparison.Ordinal))
        {
            return null;
        }

        var header = context.Request.Headers.Authorization.ToString();
        string token;
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            // 浏览器 EventSource 无法自定义请求头，因此仅对 SSE 端点放开 query token。
            if (path.Equals("/v1/events", StringComparison.Ordinal)
                && context.Request.Query.TryGetValue("access_token", out var qsToken)
                && !string.IsNullOrWhiteSpace(qsToken))
            {
                token = qsToken.ToString().Trim();
            }
            else
            {
                return StatusCodes.Status401Unauthorized;
            }
        }
        else
        {
            token = header["Bearer ".Length..].Trim();
        }

        if (string.IsNullOrEmpty(token))
        {
            return StatusCodes.Status401Unauthorized;
        }

        if (options.Tokens.TryGetValue(token, out var role))
        {
            context.Items[RoleKey] = role;
            return null;
        }

        if (userStore is not null && userStore.TryAuthenticate(token, out var user))
        {
            context.Items[UserKey] = user;
            context.Items[RoleKey] = user.IsSuperuser ? ServerRoles.Admin : ServerRoles.ReadWrite;
            return null;
        }

        return StatusCodes.Status401Unauthorized;
    }

    /// <summary>
    /// 获取当前请求上下文中的角色。
    /// </summary>
    /// <param name="context">当前 HTTP 上下文。</param>
    /// <returns>角色名；若未认证则返回 <c>null</c>。</returns>
    public static string? GetRole(HttpContext context)
        => context.Items.TryGetValue(RoleKey, out var value) ? value as string : null;

    /// <summary>
    /// 获取当前请求上下文中的认证用户。
    /// </summary>
    /// <param name="context">当前 HTTP 上下文。</param>
    /// <returns>认证用户；若当前 Token 不是用户 Token 则返回 <c>null</c>。</returns>
    public static AuthenticatedUser? GetUser(HttpContext context)
        => context.Items.TryGetValue(UserKey, out var value) && value is AuthenticatedUser user ? user : null;

    /// <summary>
    /// 判断角色是否具备写权限。
    /// </summary>
    /// <param name="role">角色名称。</param>
    /// <returns>具备写权限时返回 <c>true</c>。</returns>
    public static bool CanWrite(string? role)
        => role is ServerRoles.Admin or ServerRoles.ReadWrite;

    /// <summary>
    /// 判断角色是否为管理员。
    /// </summary>
    /// <param name="role">角色名称。</param>
    /// <returns>管理员返回 <c>true</c>。</returns>
    public static bool IsAdmin(string? role)
        => role == ServerRoles.Admin;
}
