using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace TSLite.Server.Hosting;

/// <summary>
/// 把嵌入式 Admin SPA 挂载到 <c>/admin/*</c> 路由。
/// </summary>
internal static class AdminUiEndpoints
{
    /// <summary>
    /// 注册 admin SPA 路由：<c>GET /admin</c>、<c>GET /admin/{**path}</c>。
    /// </summary>
    /// <remarks>
    /// 行为：
    /// <list type="bullet">
    ///   <item>请求路径若命中嵌入资源（命中包括 <c>/admin/assets/index-xxx.js</c> 等），返回原字节 + Content-Type。</item>
    ///   <item>未命中且 manifest 含 <c>index.html</c>，返回 <c>index.html</c>（SPA 客户端路由 fallback）。</item>
    ///   <item>未命中且无 <c>index.html</c>，返回 503 + 提示先 <c>npm run build</c>。</item>
    /// </list>
    /// </remarks>
    public static void MapAdminUi(this IEndpointRouteBuilder app)
    {
        // /admin 与 /admin/ 都重定向到 /admin/index.html 的内容（不真正 302，避免 SPA 路由抖动）。
        app.MapMethods("/admin", ["GET"], (RequestDelegate)(ctx => ServeAsync(ctx, "")));
        app.MapMethods("/admin/{**path}", ["GET"], (RequestDelegate)(ctx =>
        {
            var path = ctx.Request.RouteValues.TryGetValue("path", out var p) && p is string s ? s : string.Empty;
            return ServeAsync(ctx, path);
        }));
    }

    private static async Task ServeAsync(HttpContext ctx, string relativePath)
    {
        // 规范化：去掉前后斜杠；空路径 → index.html
        var path = string.IsNullOrEmpty(relativePath) ? "index.html" : relativePath.TrimStart('/');

        if (AdminUiAssets.Assets.TryGetValue(path, out var asset))
        {
            await WriteAsync(ctx, asset, StatusCodes.Status200OK).ConfigureAwait(false);
            return;
        }

        // 没命中具体文件 → 尝试 SPA fallback（带扩展的不 fallback，避免 .js/.css 404 被吞）
        if (!Path.HasExtension(path) && AdminUiAssets.Assets.TryGetValue("index.html", out var index))
        {
            await WriteAsync(ctx, index, StatusCodes.Status200OK).ConfigureAwait(false);
            return;
        }

        if (!AdminUiAssets.HasIndex)
        {
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            await ctx.Response.WriteAsync(
                "TSLite Admin UI 尚未构建：请先在 web/admin 目录执行 `npm install && npm run build`，再 `dotnet build src/TSLite.Server`。"
            ).ConfigureAwait(false);
            return;
        }

        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    private static async Task WriteAsync(HttpContext ctx, AdminAsset asset, int status)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = asset.ContentType;
        ctx.Response.ContentLength = asset.Bytes.Length;
        // SPA 入口不缓存；hash 化的静态资产可缓存（Vite 默认 contenthash 命名）。
        var path = ctx.Request.Path.Value ?? string.Empty;
        if (path.EndsWith("/admin", StringComparison.Ordinal) || path.EndsWith("/admin/", StringComparison.Ordinal)
            || path.EndsWith("/index.html", StringComparison.Ordinal))
        {
            ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        }
        else
        {
            ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
        await ctx.Response.Body.WriteAsync(asset.Bytes).ConfigureAwait(false);
    }
}
