using System.Data;
using System.Data.Common;
using TSLite.Data.Internal;

namespace TSLite.Data;

/// <summary>
/// TSLite ADO.NET 命令对象。把 SQL 与参数交给当前连接的内部实现执行（嵌入式或远程）。
/// </summary>
/// <remarks>
/// <para>
/// 参数支持 <c>@name</c> / <c>:name</c> 两种占位符；执行前由 <see cref="ParameterBinder"/>
/// 把占位符替换为安全的字面量（字符串值会用单引号包裹并把内部 <c>'</c> 转义为 <c>''</c>），
/// 不会修改 SQL 中字符串字面量内的内容。绑定逻辑在嵌入式与远程下完全一致，
/// 便于客户端和服务器对参数语义保持兼容。
/// </para>
/// <para>
/// <see cref="ExecuteNonQuery"/> 返回值约定：INSERT 返回写入行数；DELETE 返回新增墓碑数；
/// CREATE MEASUREMENT 返回 0；SELECT 返回 -1（与 <see cref="DbCommand"/> 标准一致）。
/// </para>
/// </remarks>
public sealed class TsdbCommand : DbCommand
{
    private TsdbConnection? _connection;
    private string _commandText = string.Empty;
    private readonly TsdbParameterCollection _parameters = new();

    /// <summary>构造一个未关联连接的命令。</summary>
    public TsdbCommand() { }

    /// <summary>用 SQL 文本与连接构造命令。</summary>
    public TsdbCommand(string commandText, TsdbConnection? connection = null)
    {
        _commandText = commandText ?? string.Empty;
        _connection = connection;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string CommandText
    {
        get => _commandText;
        set => _commandText = value ?? string.Empty;
    }

    /// <inheritdoc />
    public override int CommandTimeout { get; set; }

    /// <inheritdoc />
    public override CommandType CommandType
    {
        get => CommandType.Text;
        set
        {
            if (value != CommandType.Text)
                throw new NotSupportedException("TSLite 仅支持 CommandType.Text。");
        }
    }

    /// <inheritdoc />
    public override bool DesignTimeVisible { get; set; }

    /// <inheritdoc />
    public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.None;

    /// <inheritdoc />
    protected override DbConnection? DbConnection
    {
        get => _connection;
        set => _connection = value as TsdbConnection
            ?? (value is null ? null : throw new InvalidCastException("Connection 必须是 TsdbConnection。"));
    }

    /// <inheritdoc />
    protected override DbParameterCollection DbParameterCollection => _parameters;

    /// <inheritdoc />
    protected override DbTransaction? DbTransaction
    {
        get => null;
        set
        {
            if (value != null)
                throw new NotSupportedException("TSLite 不支持事务。");
        }
    }

    /// <summary>强类型参数集合。</summary>
    public new TsdbParameterCollection Parameters => _parameters;

    /// <summary>强类型连接。</summary>
    public new TsdbConnection? Connection
    {
        get => _connection;
        set => _connection = value;
    }

    /// <inheritdoc />
    public override void Cancel() { /* no-op：单线程同步执行，不可取消 */ }

    /// <inheritdoc />
    public override void Prepare() { /* no-op */ }

    /// <inheritdoc />
    protected override DbParameter CreateDbParameter() => new TsdbParameter();

    /// <inheritdoc />
    public override int ExecuteNonQuery()
    {
        using var result = ExecuteCore(CommandBehavior.Default);
        // 对于 SELECT，把游标走完以保持语义一致（一般上层不会调用）
        if (result.RecordsAffected == -1)
        {
            while (result.ReadNextRow()) { }
        }
        return result.RecordsAffected;
    }

    /// <inheritdoc />
    public override object? ExecuteScalar()
    {
        using var result = ExecuteCore(CommandBehavior.Default);
        if (result.Columns.Count == 0)
            return null;
        if (!result.ReadNextRow())
            return null;
        var v = result.GetValue(0);
        // 把后续行消费掉
        while (result.ReadNextRow()) { }
        return v;
    }

    /// <inheritdoc />
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        var result = ExecuteCore(behavior);
        return new TsdbDataReader(result, behavior, _connection);
    }

    private IExecutionResult ExecuteCore(CommandBehavior behavior)
    {
        if (_connection is null)
            throw new InvalidOperationException("Command 没有关联 Connection。");
        if (string.IsNullOrWhiteSpace(_commandText))
            throw new InvalidOperationException("CommandText 为空。");

        var bound = ParameterBinder.Bind(_commandText, _parameters);
        var impl = _connection.GetOpenImpl();
        return impl.Execute(bound, _parameters, behavior);
    }
}
