using System.Data;
using System.Data.Common;
using TSLite.Data.Internal;
using TSLite.Engine;
using TSLite.Sql;
using TSLite.Sql.Ast;
using TSLite.Sql.Execution;

namespace TSLite.Data.Embedded;

/// <summary>
/// 嵌入式连接实现：直接打开本地目录上的 <see cref="Tsdb"/>，并在进程内共享。
/// </summary>
internal sealed class EmbeddedConnectionImpl : IConnectionImpl
{
    private readonly TsdbConnectionStringBuilder _builder;
    private Tsdb? _tsdb;
    private ConnectionState _state = ConnectionState.Closed;

    public EmbeddedConnectionImpl(TsdbConnectionStringBuilder builder)
    {
        _builder = builder;
    }

    public string DataSource => NormalizeDataSource(_builder.DataSource);

    public string Database => DataSource;

    public string ServerVersion => typeof(Tsdb).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    public ConnectionState State => _state;

    internal Tsdb? Tsdb => _tsdb;

    public void Open()
    {
        if (_state == ConnectionState.Open) return;
        var path = NormalizeDataSource(_builder.DataSource);
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("ConnectionString 缺少 'Data Source'。");

        _tsdb = SharedTsdbRegistry.Acquire(new TsdbOptions { RootDirectory = path });
        _state = ConnectionState.Open;
    }

    public void Close()
    {
        if (_state == ConnectionState.Closed) return;
        var t = _tsdb;
        _tsdb = null;
        _state = ConnectionState.Closed;
        if (t != null)
            SharedTsdbRegistry.Release(t);
    }

    public void Dispose() => Close();

    public IExecutionResult Execute(string sql, TsdbParameterCollection parameters, CommandBehavior behavior)
    {
        if (_tsdb is null || _state != ConnectionState.Open)
            throw new InvalidOperationException("连接未打开。");

        var statement = SqlParser.Parse(sql);
        return statement switch
        {
            InsertStatement ins => MaterializedExecutionResult.NonQuery(SqlExecutor.ExecuteInsert(_tsdb, ins).RowsInserted),
            DeleteStatement del => MaterializedExecutionResult.NonQuery(SqlExecutor.ExecuteDelete(_tsdb, del).TombstonesAdded),
            CreateMeasurementStatement create => ExecuteCreate(_tsdb, create),
            SelectStatement sel => MaterializedExecutionResult.FromSelect(SqlExecutor.ExecuteSelect(_tsdb, sel)),
            _ => throw new NotSupportedException(
                $"语句类型 '{statement.GetType().Name}' 暂不支持。"),
        };
    }

    private static IExecutionResult ExecuteCreate(Tsdb tsdb, CreateMeasurementStatement create)
    {
        SqlExecutor.ExecuteCreateMeasurement(tsdb, create);
        return MaterializedExecutionResult.NonQuery(0);
    }

    /// <summary>
    /// 兼容 <c>tslite://path</c> 形式：去掉 scheme 前缀，得到真实文件系统路径。
    /// </summary>
    private static string NormalizeDataSource(string ds)
    {
        if (string.IsNullOrWhiteSpace(ds)) return ds;
        const string prefix = "tslite://";
        if (ds.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return ds[prefix.Length..];
        return ds;
    }
}
