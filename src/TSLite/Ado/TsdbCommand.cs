using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using TSLite.Sql;
using TSLite.Sql.Ast;
using TSLite.Sql.Execution;

namespace TSLite.Ado;

/// <summary>
/// TSLite ADO.NET 命令对象。支持单条 SQL 执行，结果分发到对应的执行器。
/// </summary>
/// <remarks>
/// <para>
/// 参数支持 <c>@name</c> / <c>:name</c> 两种占位符；执行前通过文本扫描把占位符替换为安全的字面量
/// （字符串值会用单引号包裹并把内部 <c>'</c> 转义为 <c>''</c>），不会修改 SQL 中字符串字面量内的内容。
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
        var (statement, tsdb) = ParseAndPrepare();
        return statement switch
        {
            InsertStatement ins => SqlExecutor.ExecuteInsert(tsdb, ins).RowsInserted,
            DeleteStatement del => SqlExecutor.ExecuteDelete(tsdb, del).TombstonesAdded,
            CreateMeasurementStatement create =>
                ExecuteCreateAndReturnZero(tsdb, create),
            SelectStatement => -1,
            _ => throw new NotSupportedException(
                $"语句类型 '{statement.GetType().Name}' 暂不支持 ExecuteNonQuery。"),
        };
    }

    private static int ExecuteCreateAndReturnZero(Engine.Tsdb tsdb, CreateMeasurementStatement create)
    {
        SqlExecutor.ExecuteCreateMeasurement(tsdb, create);
        return 0;
    }

    /// <inheritdoc />
    public override object? ExecuteScalar()
    {
        var (statement, tsdb) = ParseAndPrepare();
        if (statement is SelectStatement select)
        {
            var result = SqlExecutor.ExecuteSelect(tsdb, select);
            if (result.Rows.Count == 0 || result.Columns.Count == 0)
                return null;
            return result.Rows[0][0];
        }
        // 非查询语句：执行后返回 null（与典型 ADO.NET 行为一致）
        SqlExecutor.ExecuteStatement(tsdb, statement);
        return null;
    }

    /// <inheritdoc />
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        var (statement, tsdb) = ParseAndPrepare();
        if (statement is SelectStatement select)
        {
            var result = SqlExecutor.ExecuteSelect(tsdb, select);
            return new TsdbDataReader(result, recordsAffected: -1, behavior, _connection);
        }

        // 非 SELECT：执行并返回 0 行 reader，附带 RecordsAffected
        int affected = statement switch
        {
            InsertStatement ins => SqlExecutor.ExecuteInsert(tsdb, ins).RowsInserted,
            DeleteStatement del => SqlExecutor.ExecuteDelete(tsdb, del).TombstonesAdded,
            CreateMeasurementStatement create => ExecuteCreateAndReturnZero(tsdb, create),
            _ => throw new NotSupportedException(
                $"语句类型 '{statement.GetType().Name}' 暂不支持 ExecuteReader。"),
        };
        var empty = new SelectExecutionResult(Array.Empty<string>(), Array.Empty<IReadOnlyList<object?>>());
        return new TsdbDataReader(empty, affected, behavior, _connection);
    }

    private (SqlStatement Statement, Engine.Tsdb Tsdb) ParseAndPrepare()
    {
        if (_connection is null)
            throw new InvalidOperationException("Command 没有关联 Connection。");
        var tsdb = _connection.GetOpenTsdb();
        if (string.IsNullOrWhiteSpace(_commandText))
            throw new InvalidOperationException("CommandText 为空。");

        var bound = ParameterBinder.Bind(_commandText, _parameters);
        var statement = SqlParser.Parse(bound);
        return (statement, tsdb);
    }

    /// <summary>
    /// 把 <c>@name</c> / <c>:name</c> 占位符按字面量安全替换到 SQL 文本中。
    /// 已通过状态机跳过字符串字面量与双引号标识符内的内容。
    /// </summary>
    internal static class ParameterBinder
    {
        public static string Bind(string sql, TsdbParameterCollection parameters)
        {
            if (sql.IndexOf('@') < 0 && sql.IndexOf(':') < 0)
                return sql;

            var byName = new Dictionary<string, TsdbParameter>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in parameters.Items)
            {
                var n = TsdbParameterCollection.NormalizeName(p.ParameterName);
                if (string.IsNullOrEmpty(n))
                    throw new InvalidOperationException("参数名不能为空。");
                byName[n] = p;
            }

            var sb = new StringBuilder(sql.Length + 16);
            int i = 0;
            while (i < sql.Length)
            {
                char ch = sql[i];

                // 跳过 '...' 字符串字面量（' 内的 '' 视为转义）
                if (ch == '\'')
                {
                    sb.Append(ch); i++;
                    while (i < sql.Length)
                    {
                        char c = sql[i++];
                        sb.Append(c);
                        if (c == '\'')
                        {
                            if (i < sql.Length && sql[i] == '\'') { sb.Append(sql[i++]); continue; }
                            break;
                        }
                    }
                    continue;
                }

                // 跳过 "..." 双引号标识符
                if (ch == '"')
                {
                    sb.Append(ch); i++;
                    while (i < sql.Length)
                    {
                        char c = sql[i++];
                        sb.Append(c);
                        if (c == '"') break;
                    }
                    continue;
                }

                // 跳过单行注释 -- ...
                if (ch == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
                {
                    while (i < sql.Length && sql[i] != '\n') sb.Append(sql[i++]);
                    continue;
                }

                if ((ch == '@' || ch == ':') && i + 1 < sql.Length && IsIdentStart(sql[i + 1]))
                {
                    int start = i + 1;
                    int end = start;
                    while (end < sql.Length && IsIdentPart(sql[end])) end++;
                    string name = sql.Substring(start, end - start);
                    if (!byName.TryGetValue(name, out var p))
                        throw new InvalidOperationException($"未提供参数 '{ch}{name}' 的值。");
                    sb.Append(FormatLiteral(p.Value));
                    i = end;
                    continue;
                }

                sb.Append(ch);
                i++;
            }
            return sb.ToString();
        }

        private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
        private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';

        internal static string FormatLiteral(object? value)
        {
            if (value is null || value is DBNull)
                return "NULL";

            return value switch
            {
                string s => "'" + s.Replace("'", "''") + "'",
                bool b => b ? "true" : "false",
                byte u8 => u8.ToString(CultureInfo.InvariantCulture),
                short i16 => i16.ToString(CultureInfo.InvariantCulture),
                int i32 => i32.ToString(CultureInfo.InvariantCulture),
                long i64 => i64.ToString(CultureInfo.InvariantCulture),
                float f => f.ToString("R", CultureInfo.InvariantCulture),
                double d => d.ToString("R", CultureInfo.InvariantCulture),
                decimal m => m.ToString(CultureInfo.InvariantCulture),
                DateTime dt => new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                    : dt).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                DateTimeOffset dto => dto.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                _ => throw new NotSupportedException(
                    $"不支持的参数类型 '{value.GetType().FullName}'。"),
            };
        }
    }
}
