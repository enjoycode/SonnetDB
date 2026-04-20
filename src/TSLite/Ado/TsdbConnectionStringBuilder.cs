using System.Data.Common;

namespace TSLite.Ado;

/// <summary>
/// <see cref="TsdbConnection"/> 的连接字符串解析器。当前仅支持一个键：
/// <c>Data Source</c>（大小写不敏感，由基类 <see cref="DbConnectionStringBuilder"/> 提供）。
/// 例如 <c>Data Source=./data/tslite</c>。
/// </summary>
public sealed class TsdbConnectionStringBuilder : DbConnectionStringBuilder
{
    private const string DataSourceKey = "Data Source";

    /// <summary>使用空连接字符串构造。</summary>
    public TsdbConnectionStringBuilder() { }

    /// <summary>用已有的连接字符串构造。</summary>
    /// <param name="connectionString">原始连接字符串。</param>
    public TsdbConnectionStringBuilder(string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
            ConnectionString = connectionString;
    }

    /// <summary>数据库根目录路径（必填）。</summary>
    public string DataSource
    {
        get => TryGetValue(DataSourceKey, out var v) ? v?.ToString() ?? string.Empty : string.Empty;
        set => base[DataSourceKey] = value;
    }
}
