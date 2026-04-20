using System.Collections.Frozen;
using System.Reflection;

namespace TSLite.Server.Hosting;

/// <summary>
/// 加载并缓存嵌入式 Admin SPA 静态资源。
/// </summary>
/// <remarks>
/// 资源命名约定：所有 <c>web/admin/dist/**</c> 文件以 <c>EmbeddedResource</c> 形式嵌入，
/// 通过 csproj 的 <c>LogicalName</c> 改写后统一前缀为 <c>tslite.admin/</c>。
/// 例如 <c>web/admin/dist/index.html</c> → manifest 名 <c>tslite.admin/index.html</c>。
/// </remarks>
internal static class AdminUiAssets
{
    /// <summary>资源前缀（不含 assembly 默认 namespace 噪音）。</summary>
    public const string ResourcePrefix = "tslite.admin/";

    /// <summary>已加载的资源映射：<c>相对路径</c> → <c>(字节, content-type)</c>。</summary>
    public static readonly FrozenDictionary<string, AdminAsset> Assets = LoadAll();

    /// <summary>是否包含可服务的 SPA（至少存在 index.html）。</summary>
    public static bool HasIndex => Assets.ContainsKey("index.html");

    private static FrozenDictionary<string, AdminAsset> LoadAll()
    {
        var asm = typeof(AdminUiAssets).Assembly;
        var dict = new Dictionary<string, AdminAsset>(StringComparer.Ordinal);
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                continue;
            // LogicalName 在 Windows 下的 RecursiveDir 部分会带反斜杠，统一规范化为正斜杠。
            var relative = name[ResourcePrefix.Length..].Replace('\\', '/');
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) continue;
            var ms = new MemoryStream(checked((int)stream.Length));
            stream.CopyTo(ms);
            dict[relative] = new AdminAsset(ms.ToArray(), GuessContentType(relative));
        }
        return dict.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// 根据扩展名猜测 Content-Type。AOT 友好：纯 switch + 常量字符串。
    /// </summary>
    public static string GuessContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".js" or ".mjs" => "application/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".map" => "application/json",
            ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream",
        };
    }
}

/// <summary>
/// 单个 Admin SPA 静态资源。
/// </summary>
/// <param name="Bytes">资源原始字节。</param>
/// <param name="ContentType">MIME 类型。</param>
internal readonly record struct AdminAsset(byte[] Bytes, string ContentType);
