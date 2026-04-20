using Microsoft.AspNetCore.Http;
using TSLite.Server.Configuration;

namespace TSLite.Server.Auth;

/// <summary>
/// 极简 Bearer token 认证中间件：
/// <list type="bullet">
///   <item>从请求头 <c>Authorization: Bearer &lt;token&gt;</c> 提取 token；</item>
///   <item>查 <see cref="ServerOptions.Tokens"/> 得到角色；</item>
///   <item>把角色写入 <see cref="HttpContext.Items"/> 中，供后续 endpoint 读取。</item>
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
    /// 检查并设置角色；若启用了 <see cref="ServerOptions.AllowAnonymousProbes"/>，<c>/healthz</c>
    /// 与 <c>/metrics</c> 跳过认证。
    /// </summary>
    /// <returns>
    /// <c>null</c> 表示通过；否则为该返回的 HTTP 状态码（401 / 403）。
    /// </returns>
    public static int? Authenticate(HttpContext context, ServerOptions options)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (options.AllowAnonymousProbes && (path.Equals("/healthz", StringComparison.Ordinal) || path.Equals("/metrics", StringComparison.Ordinal)))
        {
            return null;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return StatusCodes.Status401Unauthorized;
        }
        var token = header["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token) || !options.Tokens.TryGetValue(token, out var role))
        {
            return StatusCodes.Status401Unauthorized;
        }

        context.Items[RoleKey] = role;
        return null;
    }

    /// <summary>
    /// 从当前请求里取出已认证角色（中间件填充）。
    /// </summary>
    public static string? GetRole(HttpContext context)
        => context.Items.TryGetValue(RoleKey, out var v) ? v as string : null;

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
