using System.Data;
using System.Data.Common;
using TSLite.Data.Embedded;
using TSLite.Data.Internal;
using TSLite.Data.Remote;

namespace TSLite.Data;

/// <summary>
/// TSLite 的 ADO.NET 连接对象。同一类型同时承载嵌入式与远程两种实现，
/// 由 <see cref="TsdbConnectionStringBuilder.ResolveMode"/> 推断分发。
/// </summary>
/// <remarks>
/// <para>
/// 嵌入式：连接字符串形如 <c>Data Source=./data</c> 或 <c>Data Source=tslite://./data</c>，
/// 内部直接打开 <see cref="TSLite.Engine.Tsdb"/>，并通过引用计数共享同一进程内的同目录实例。
/// </para>
/// <para>
/// 远程：连接字符串形如 <c>Data Source=tslite+http://host:port/dbname;Token=xxx</c>，
/// 内部使用 <see cref="System.Net.Http.HttpClient"/> 调用 <c>POST /v1/db/{db}/sql</c>，
/// 结果以 ndjson 流式反序列化。
/// </para>
/// <para>当前版本不支持事务。</para>
/// </remarks>
public sealed class TsdbConnection : DbConnection
{
    private string _connectionString = string.Empty;
    private TsdbConnectionStringBuilder _builder = new();
    private IConnectionImpl? _impl;
    private bool _disposed;

    /// <summary>使用空连接字符串构造，必须随后赋值 <see cref="ConnectionString"/> 再 <see cref="Open"/>。</summary>
    public TsdbConnection() { }

    /// <summary>使用指定的连接字符串构造。</summary>
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
            if (State != ConnectionState.Closed)
                throw new InvalidOperationException("不能在连接打开状态下修改 ConnectionString。");
            _connectionString = value ?? string.Empty;
            _builder = new TsdbConnectionStringBuilder(_connectionString);
        }
    }

    /// <inheritdoc />
    public override string Database => _impl?.Database ?? _builder.Database ?? _builder.DataSource;

    /// <inheritdoc />
    public override string DataSource => _impl?.DataSource ?? _builder.DataSource;

    /// <inheritdoc />
    public override string ServerVersion
        => _impl?.ServerVersion ?? typeof(TsdbConnection).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    /// <inheritdoc />
    public override ConnectionState State => _impl?.State ?? ConnectionState.Closed;

    /// <summary>当前连接所采用的运行模式。</summary>
    public TsdbProviderMode ProviderMode => _builder.ResolveMode();

    /// <summary>
    /// 仅嵌入式模式可用：返回底层 <see cref="TSLite.Engine.Tsdb"/> 引擎实例；远程模式或未打开时为 null。
    /// </summary>
    public TSLite.Engine.Tsdb? UnderlyingTsdb
        => _impl is EmbeddedConnectionImpl emb ? emb.Tsdb : null;

    /// <inheritdoc />
    public override void ChangeDatabase(string databaseName)
        => throw new NotSupportedException("TSLite 不支持 ChangeDatabase；请关闭连接后用新的 ConnectionString 重新打开。");

    /// <inheritdoc />
    public override void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (State == ConnectionState.Open)
            return;

        _impl = _builder.ResolveMode() switch
        {
            TsdbProviderMode.Embedded => new EmbeddedConnectionImpl(_builder),
            TsdbProviderMode.Remote => new RemoteConnectionImpl(_builder),
            _ => throw new InvalidOperationException("未知的 TsdbProviderMode。"),
        };
        try
        {
            _impl.Open();
        }
        catch
        {
            _impl.Dispose();
            _impl = null;
            throw;
        }
    }

    /// <inheritdoc />
    public override void Close()
    {
        var impl = _impl;
        _impl = null;
        impl?.Close();
        impl?.Dispose();
    }

    /// <inheritdoc />
    protected override DbCommand CreateDbCommand() => new TsdbCommand { Connection = this };

    /// <inheritdoc />
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => throw new NotSupportedException("TSLite 当前版本不支持事务。");

    /// <summary>使用强类型返回 <see cref="TsdbCommand"/>。</summary>
    public new TsdbCommand CreateCommand() => new() { Connection = this };

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing) Close();
        _disposed = true;
        base.Dispose(disposing);
    }

    internal IConnectionImpl GetOpenImpl()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_impl is null || _impl.State != ConnectionState.Open)
            throw new InvalidOperationException("连接未打开。");
        return _impl;
    }
}
