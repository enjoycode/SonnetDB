using System.Data;
using System.Data.Common;
using TSLite.Engine;

namespace TSLite.Ado;

/// <summary>
/// TSLite 的 ADO.NET 连接对象。轻量门面，包装一个共享的 <see cref="Tsdb"/> 引擎实例。
/// </summary>
/// <remarks>
/// <para>
/// 同一进程多次以相同 <see cref="ConnectionString"/> 打开会通过引用计数共享同一 <see cref="Tsdb"/>。
/// 跨进程仍受 WAL 文件锁约束，不允许并发打开同一目录。
/// </para>
/// <para>
/// 当前版本不支持事务（<see cref="BeginDbTransaction"/> 抛出 <see cref="NotSupportedException"/>），
/// 也不支持 <see cref="ChangeDatabase"/>。
/// </para>
/// </remarks>
public sealed class TsdbConnection : DbConnection
{
    private string _connectionString = string.Empty;
    private TsdbConnectionStringBuilder _builder = new();
    private Tsdb? _tsdb;
    private ConnectionState _state = ConnectionState.Closed;
    private bool _disposed;

    /// <summary>使用空连接字符串构造，必须随后赋值 <see cref="ConnectionString"/> 再 <see cref="Open"/>。</summary>
    public TsdbConnection() { }

    /// <summary>使用指定的连接字符串构造。</summary>
    /// <param name="connectionString">如 <c>Data Source=./data</c>。</param>
    public TsdbConnection(string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
            ConnectionString = connectionString;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ConnectionString
    {
        get => _connectionString;
        set
        {
            if (_state != ConnectionState.Closed)
                throw new InvalidOperationException("不能在连接打开状态下修改 ConnectionString。");
            _connectionString = value ?? string.Empty;
            _builder = new TsdbConnectionStringBuilder(_connectionString);
        }
    }

    /// <inheritdoc />
    public override string Database => _builder.DataSource;

    /// <inheritdoc />
    public override string DataSource => _builder.DataSource;

    /// <inheritdoc />
    public override string ServerVersion => typeof(Tsdb).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    /// <inheritdoc />
    public override ConnectionState State => _state;

    /// <summary>底层 <see cref="Tsdb"/> 引擎实例；连接未打开时为 null。</summary>
    public Tsdb? UnderlyingTsdb => _tsdb;

    /// <inheritdoc />
    public override void ChangeDatabase(string databaseName)
        => throw new NotSupportedException("TSLite 不支持 ChangeDatabase；请关闭连接后用新的 ConnectionString 重新打开。");

    /// <inheritdoc />
    public override void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_state == ConnectionState.Open)
            return;

        var path = _builder.DataSource;
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("ConnectionString 缺少 'Data Source'。");

        var options = new TsdbOptions { RootDirectory = path };
        _tsdb = SharedTsdbRegistry.Acquire(options);
        _state = ConnectionState.Open;
    }

    /// <inheritdoc />
    public override void Close()
    {
        if (_state == ConnectionState.Closed)
            return;
        var tsdb = _tsdb;
        _tsdb = null;
        _state = ConnectionState.Closed;
        if (tsdb != null)
            SharedTsdbRegistry.Release(tsdb);
    }

    /// <inheritdoc />
    protected override DbCommand CreateDbCommand()
        => new TsdbCommand { Connection = this };

    /// <inheritdoc />
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => throw new NotSupportedException("TSLite 当前版本不支持事务。");

    /// <summary>使用强类型返回 <see cref="TsdbCommand"/>。</summary>
    public new TsdbCommand CreateCommand() => new() { Connection = this };

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;
        if (disposing)
            Close();
        _disposed = true;
        base.Dispose(disposing);
    }

    internal Tsdb GetOpenTsdb()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_state != ConnectionState.Open || _tsdb is null)
            throw new InvalidOperationException("连接未打开。");
        return _tsdb;
    }
}
