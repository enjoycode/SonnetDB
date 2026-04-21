using System.Data.Common;

namespace SonnetDB.Data;

/// <summary>
/// SonnetDB 的 <see cref="DbProviderFactory"/> 实现，便于在通用 ADO.NET 代码（如 Dapper）中以工厂模式获取连接 / 命令 / 参数。
/// </summary>
public sealed class TsdbProviderFactory : DbProviderFactory
{
    /// <summary>共享单例。</summary>
    public static readonly TsdbProviderFactory Instance = new();

    private TsdbProviderFactory() { }

    /// <inheritdoc />
    public override DbConnection CreateConnection() => new TsdbConnection();

    /// <inheritdoc />
    public override DbCommand CreateCommand() => new TsdbCommand();

    /// <inheritdoc />
    public override DbParameter CreateParameter() => new TsdbParameter();

    /// <inheritdoc />
    public override DbConnectionStringBuilder CreateConnectionStringBuilder()
        => new TsdbConnectionStringBuilder();
}
