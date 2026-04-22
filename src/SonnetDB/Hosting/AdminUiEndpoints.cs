using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace SonnetDB.Hosting;

/// <summary>
/// Configures the Admin SPA for development and published hosting modes.
/// </summary>
internal static class AdminUiEndpoints
{
    private const string DefaultSpaProxyServerUrl = "https://localhost:5173";

    /// <summary>
    /// Registers Admin SPA routes.
    /// </summary>
    /// <param name="app">The current web application.</param>
    public static void MapAdminUi(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            MapDevelopmentAdminUi(app);
            return;
        }

        app.UseDefaultFiles(new DefaultFilesOptions
        {
            RequestPath = "/admin",
        });
        app.UseStaticFiles();

        if (app.Environment.WebRootFileProvider.GetFileInfo("admin/index.html").Exists)
        {
            app.MapGet("/admin", static () => Results.Redirect("/admin/"));
            app.MapFallbackToFile("/admin/{*path:nonfile}", "admin/index.html");
            return;
        }

        app.MapMethods("/admin", ["GET"], static (HttpContext ctx) => WriteUnavailableAsync(ctx));
        app.MapMethods("/admin/{**path}", ["GET"], static (HttpContext ctx) => WriteUnavailableAsync(ctx));
    }

    private static void MapDevelopmentAdminUi(WebApplication app)
    {
        var proxyServerUrl = GetSpaProxyServerUrl(app.Configuration);

        app.MapMethods("/admin", ["GET"], (HttpContext ctx) => WriteLaunchPageAsync(ctx, proxyServerUrl, string.Empty));
        app.MapMethods("/admin/{**path}", ["GET"], (HttpContext ctx) =>
        {
            var path = ctx.Request.RouteValues.TryGetValue("path", out var routeValue) && routeValue is string text
                ? text
                : string.Empty;
            return WriteLaunchPageAsync(ctx, proxyServerUrl, path);
        });
    }

    private static string GetSpaProxyServerUrl(IConfiguration configuration)
    {
        var serverUrl = configuration["SpaProxyServer:ServerUrl"];
        return string.IsNullOrWhiteSpace(serverUrl)
            ? DefaultSpaProxyServerUrl
            : serverUrl.TrimEnd('/');
    }

    private static async Task WriteLaunchPageAsync(HttpContext ctx, string proxyServerUrl, string relativePath)
    {
        if (!IsSpaEntryRequest(relativePath))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var normalizedPath = string.IsNullOrWhiteSpace(relativePath)
            || relativePath.Equals("index.html", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : "/" + relativePath.Trim('/');
        var targetUrl = $"{proxyServerUrl}/admin{normalizedPath}{ctx.Request.QueryString}";
        var probeUrl = $"{proxyServerUrl}/admin/";
        var htmlEncodedTargetUrl = HtmlEncoder.Default.Encode(targetUrl);
        var jsEncodedTargetUrl = JavaScriptEncoder.Default.Encode(targetUrl);
        var jsEncodedProbeUrl = JavaScriptEncoder.Default.Encode(probeUrl);

        var html =
$$"""
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>SonnetDB Admin Dev Proxy</title>
    <style>
      :root { color-scheme: light; }
      body {
        margin: 0;
        min-height: 100vh;
        display: grid;
        place-items: center;
        background: linear-gradient(135deg, #f7fafc, #edf2f7);
        font-family: "Segoe UI", "Microsoft YaHei UI", sans-serif;
        color: #1a202c;
      }
      main {
        width: min(640px, calc(100vw - 32px));
        background: rgba(255, 255, 255, 0.92);
        border: 1px solid rgba(148, 163, 184, 0.35);
        border-radius: 20px;
        padding: 32px;
        box-shadow: 0 24px 60px rgba(15, 23, 42, 0.12);
      }
      h1 { margin: 0 0 12px; font-size: 28px; }
      p { margin: 0 0 12px; line-height: 1.6; }
      code {
        display: inline-block;
        padding: 2px 8px;
        border-radius: 999px;
        background: #e2e8f0;
        font-family: Consolas, "Cascadia Code", monospace;
      }
      a { color: #0f766e; }
    </style>
  </head>
  <body>
    <main>
      <h1>Connecting SonnetDB Admin to the Vite dev server</h1>
      <p>The backend is running in SPA debug mode and will redirect to the frontend as soon as Vite is ready.</p>
      <p>If this is the first run, execute <code>npm install</code> once in the <code>web</code> folder.</p>
      <p>If the browser does not redirect automatically, open: <a href="{{htmlEncodedTargetUrl}}">{{htmlEncodedTargetUrl}}</a></p>
    </main>
    <script>
      const targetUrl = "{{jsEncodedTargetUrl}}";
      const probeUrl = "{{jsEncodedProbeUrl}}";
      const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

      async function redirectWhenReady() {
        for (;;) {
          try {
            await fetch(probeUrl, { cache: 'no-store', mode: 'no-cors' });
            window.location.replace(targetUrl);
            return;
          } catch {
            await sleep(1000);
          }
        }
      }

      redirectWhenReady();
    </script>
  </body>
</html>
""";

        ctx.Response.StatusCode = StatusCodes.Status200OK;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        await ctx.Response.WriteAsync(html).ConfigureAwait(false);
    }

    private static bool IsSpaEntryRequest(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return true;

        var normalized = relativePath.Trim('/');
        return normalized.Equals("index.html", StringComparison.OrdinalIgnoreCase)
            || !Path.HasExtension(normalized);
    }

    private static async Task WriteUnavailableAsync(HttpContext ctx)
    {
        ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        await ctx.Response.WriteAsync(
            "SonnetDB Admin static files are missing. Run `npm install && npm run build` in `web`, or publish the server with `dotnet publish src/SonnetDB/SonnetDB.csproj`."
        ).ConfigureAwait(false);
    }
}
