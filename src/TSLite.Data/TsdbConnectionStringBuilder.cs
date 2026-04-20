using System.Data.Common;

namespace TSLite.Data;

/// <summary>
/// <see cref="TsdbConnection"/> 的连接字符串解析器。同时承载嵌入式与远程两种模式。
/// </summary>
/// <remarks>
/// <para>支持的键（大小写不敏感）：</para>
/// <list type="table">
///   <listheader><term>键</term><description>含义</description></listheader>
///   <item><term><c>Mode</c></term><description>显式指定 <see cref="TsdbProviderMode.Embedded"/> 或 <see cref="TsdbProviderMode.Remote"/>；省略时按 <c>Data Source</c> 推断。</description></item>
///   <item><term><c>Data Source</c></term><description>
///     嵌入式：本地目录路径（如 <c>./data</c> 或 <c>tslite://./data</c>）。
///     远程：服务器 URL，scheme 必须为 <c>http</c>/<c>https</c>/<c>tslite+http</c>/<c>tslite+https</c>，
///     例如 <c>tslite+http://127.0.0.1:5050/mydb</c>，URL 路径段会被解析为 <see cref="Database"/>。
///   </description></item>
///   <item><term><c>Database</c></term><description>远程模式下的目标数据库名；若同时在 URL 路径中出现以本键为准。</description></item>
///   <item><term><c>Token</c></term><description>远程模式下的 Bearer token。</description></item>
///   <item><term><c>Timeout</c></term><description>远程模式下 HTTP 请求超时（秒），默认 100。</description></item>
/// </list>
/// </remarks>
public sealed class TsdbConnectionStringBuilder : DbConnectionStringBuilder
{
    private const string KeyMode = "Mode";
    private const string KeyDataSource = "Data Source";
    private const string KeyDatabase = "Database";
    private const string KeyToken = "Token";
    private const string KeyTimeout = "Timeout";

    /// <summary>使用空连接字符串构造。</summary>
    public TsdbConnectionStringBuilder() { }

    /// <summary>用已有的连接字符串构造。</summary>
    public TsdbConnectionStringBuilder(string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
            ConnectionString = connectionString;
    }

    /// <summary>显式模式；未设置时由 <see cref="ResolveMode"/> 按 <see cref="DataSource"/> 推断。</summary>
    public TsdbProviderMode? Mode
    {
        get
        {
            if (!TryGetValue(KeyMode, out var raw) || raw is null) return null;
            var s = raw.ToString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            return Enum.TryParse<TsdbProviderMode>(s, ignoreCase: true, out var m)
                ? m
                : throw new FormatException($"无效的 Mode 值 '{s}'，应为 Embedded 或 Remote。");
        }
        set
        {
            if (value is null) Remove(KeyMode);
            else base[KeyMode] = value.Value.ToString();
        }
    }

    /// <summary>原始 <c>Data Source</c> 值（路径或 URL）。</summary>
    public string DataSource
    {
        get => TryGetValue(KeyDataSource, out var v) ? v?.ToString() ?? string.Empty : string.Empty;
        set => base[KeyDataSource] = value;
    }

    /// <summary>远程模式下的数据库名。</summary>
    public string? Database
    {
        get => TryGetValue(KeyDatabase, out var v) ? v?.ToString() : null;
        set
        {
            if (value is null) Remove(KeyDatabase);
            else base[KeyDatabase] = value;
        }
    }

    /// <summary>远程模式下的 Bearer token。</summary>
    public string? Token
    {
        get => TryGetValue(KeyToken, out var v) ? v?.ToString() : null;
        set
        {
            if (value is null) Remove(KeyToken);
            else base[KeyToken] = value;
        }
    }

    /// <summary>远程模式下 HTTP 请求超时（秒），默认 100。</summary>
    public int Timeout
    {
        get => TryGetValue(KeyTimeout, out var v) && int.TryParse(v?.ToString(), out var t) ? t : 100;
        set => base[KeyTimeout] = value;
    }

    /// <summary>
    /// 推断当前连接字符串应使用的运行模式：优先取 <see cref="Mode"/>，其次按 <see cref="DataSource"/> scheme。
    /// </summary>
    public TsdbProviderMode ResolveMode()
    {
        if (Mode is { } explicitMode) return explicitMode;

        var ds = DataSource;
        if (string.IsNullOrWhiteSpace(ds))
            return TsdbProviderMode.Embedded;

        // scheme://...
        int idx = ds.IndexOf("://", StringComparison.Ordinal);
        if (idx <= 0) return TsdbProviderMode.Embedded;
        var scheme = ds[..idx].ToLowerInvariant();
        return scheme switch
        {
            "http" or "https" or "tslite+http" or "tslite+https" => TsdbProviderMode.Remote,
            _ => TsdbProviderMode.Embedded,
        };
    }
}
