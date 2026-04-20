using TSLite.Catalog;
using TSLite.Engine;
using TSLite.Model;
using TSLite.Sql.Ast;
using TSLite.Storage.Format;

namespace TSLite.Sql.Execution;

/// <summary>
/// 把 <see cref="SqlStatement"/> AST 应用到 <see cref="Tsdb"/> 实例的执行器。
/// 当前 Milestone 支持 <see cref="CreateMeasurementStatement"/>、<see cref="InsertStatement"/>、
/// <see cref="SelectStatement"/> 与 <see cref="DeleteStatement"/>。
/// </summary>
public static class SqlExecutor
{
    /// <summary>
    /// 解析并执行单条 SQL 语句。
    /// </summary>
    /// <param name="tsdb">目标数据库实例。</param>
    /// <param name="sql">单条 SQL 文本。</param>
    /// <returns>语句执行结果对象（具体类型取决于语句种类）。</returns>
    /// <exception cref="ArgumentNullException">任何参数为 null。</exception>
    /// <exception cref="NotSupportedException">语句类型尚未实现。</exception>
    public static object? Execute(Tsdb tsdb, string sql)
        => Execute(tsdb, sql, controlPlane: null);

    /// <summary>
    /// 解析并执行单条 SQL 语句，可选传入控制面以支持 CREATE USER / GRANT 等 DDL。
    /// </summary>
    /// <param name="tsdb">目标数据库实例。</param>
    /// <param name="sql">单条 SQL 文本。</param>
    /// <param name="controlPlane">控制面实现；为 <c>null</c> 时控制面 DDL 抛 <see cref="NotSupportedException"/>。</param>
    /// <returns>语句执行结果对象。</returns>
    public static object? Execute(Tsdb tsdb, string sql, IControlPlane? controlPlane)
    {
        ArgumentNullException.ThrowIfNull(tsdb);
        ArgumentNullException.ThrowIfNull(sql);

        var statement = SqlParser.Parse(sql);
        return ExecuteStatement(tsdb, statement, controlPlane);
    }

    /// <summary>
    /// 执行一条已解析的 SQL 语句。
    /// </summary>
    /// <param name="tsdb">目标数据库实例。</param>
    /// <param name="statement">已解析的语句 AST。</param>
    /// <returns>执行结果。</returns>
    /// <exception cref="ArgumentNullException">任何参数为 null。</exception>
    /// <exception cref="NotSupportedException">语句类型尚未实现。</exception>
    public static object? ExecuteStatement(Tsdb tsdb, SqlStatement statement)
        => ExecuteStatement(tsdb, statement, controlPlane: null);

    /// <summary>
    /// 执行一条已解析的 SQL 语句，可选传入控制面以支持控制面 DDL。
    /// </summary>
    /// <param name="tsdb">目标数据库实例。</param>
    /// <param name="statement">已解析的语句 AST。</param>
    /// <param name="controlPlane">控制面实现；为 <c>null</c> 时控制面 DDL 抛 <see cref="NotSupportedException"/>。</param>
    public static object? ExecuteStatement(Tsdb tsdb, SqlStatement statement, IControlPlane? controlPlane)
    {
        ArgumentNullException.ThrowIfNull(tsdb);
        ArgumentNullException.ThrowIfNull(statement);

        return statement switch
        {
            CreateMeasurementStatement create => ExecuteCreateMeasurement(tsdb, create),
            InsertStatement insert => ExecuteInsert(tsdb, insert),
            SelectStatement select => ExecuteSelect(tsdb, select),
            DeleteStatement delete => ExecuteDelete(tsdb, delete),
            CreateUserStatement createUser => ExecuteControlPlane(controlPlane,
                cp => { cp.CreateUser(createUser.UserName, createUser.Password, createUser.IsSuperuser); return (object)1; }),
            AlterUserPasswordStatement alterUser => ExecuteControlPlane(controlPlane,
                cp => { cp.AlterUserPassword(alterUser.UserName, alterUser.NewPassword); return (object)1; }),
            DropUserStatement dropUser => ExecuteControlPlane(controlPlane,
                cp => { cp.DropUser(dropUser.UserName); return (object)1; }),
            GrantStatement grant => ExecuteControlPlane(controlPlane,
                cp => { cp.Grant(grant.UserName, grant.Database, grant.Permission); return (object)1; }),
            RevokeStatement revoke => ExecuteControlPlane(controlPlane,
                cp => { cp.Revoke(revoke.UserName, revoke.Database); return (object)1; }),
            CreateDatabaseStatement createDb => ExecuteControlPlane(controlPlane,
                cp => { cp.CreateDatabase(createDb.DatabaseName); return (object)1; }),
            DropDatabaseStatement dropDb => ExecuteControlPlane(controlPlane,
                cp => { cp.DropDatabase(dropDb.DatabaseName); return (object)1; }),
            _ => throw new NotSupportedException(
                $"SQL 语句类型 '{statement.GetType().Name}' 尚未实现。"),
        };
    }

    private static object ExecuteControlPlane(IControlPlane? controlPlane, Func<IControlPlane, object> action)
    {
        if (controlPlane is null)
            throw new NotSupportedException("控制面 DDL（CREATE USER / GRANT / CREATE DATABASE 等）仅在服务端模式可用。");
        return action(controlPlane);
    }

    /// <summary>
    /// 执行 <c>CREATE MEASUREMENT</c> 语句：把 AST 列定义映射到 catalog schema 并注册。
    /// </summary>
    /// <param name="tsdb">目标数据库实例。</param>
    /// <param name="statement">已解析的 CREATE MEASUREMENT 语句。</param>
    /// <returns>注册到 catalog 的 <see cref="MeasurementSchema"/>。</returns>
    /// <exception cref="ArgumentNullException">任何参数为 null。</exception>
    /// <exception cref="InvalidOperationException">同名 measurement 已存在。</exception>
    public static MeasurementSchema ExecuteCreateMeasurement(
        Tsdb tsdb,
        CreateMeasurementStatement statement)
    {
        ArgumentNullException.ThrowIfNull(tsdb);
        ArgumentNullException.ThrowIfNull(statement);

        var columns = new List<MeasurementColumn>(statement.Columns.Count);
        foreach (var col in statement.Columns)
            columns.Add(new MeasurementColumn(col.Name, MapRole(col.Kind), MapType(col.DataType)));

        var schema = MeasurementSchema.Create(statement.Name, columns);
        return tsdb.CreateMeasurement(schema);
    }

    private static MeasurementColumnRole MapRole(ColumnKind kind) => kind switch
    {
        ColumnKind.Tag => MeasurementColumnRole.Tag,
        ColumnKind.Field => MeasurementColumnRole.Field,
        _ => throw new NotSupportedException($"未知列角色 {kind}。"),
    };

    private static FieldType MapType(SqlDataType type) => type switch
    {
        SqlDataType.Float64 => FieldType.Float64,
        SqlDataType.Int64 => FieldType.Int64,
        SqlDataType.Boolean => FieldType.Boolean,
        SqlDataType.String => FieldType.String,
        _ => throw new NotSupportedException($"未知数据类型 {type}。"),
    };

    /// <summary>
    /// 执行 <c>INSERT INTO measurement (col, ...) VALUES (...) [, (...)]*</c> 语句。
    /// 校验规则：
    /// <list type="bullet">
    ///   <item>目标 measurement 必须已通过 CREATE MEASUREMENT 注册。</item>
    ///   <item>列列表中的每个名字必须是 schema 中已声明的列，或为保留伪列 <c>time</c>（时间戳，不区分大小写）。</item>
    ///   <item>同一 INSERT 列列表中不允许重复列名。</item>
    ///   <item>Tag 列必须传入字符串字面量；不允许 NULL；不允许保留字符。</item>
    ///   <item>Field 列值必须与列声明类型匹配（INT 字面量可隐式转换为 FLOAT）。</item>
    ///   <item>每行至少需要包含一个 Field 列值（与 <see cref="Point"/> 的约束一致）。</item>
    ///   <item><c>time</c> 列必须为非负整数字面量；缺省时使用当前 UTC 毫秒。</item>
    ///   <item>VALUES 字面量当前仅支持 NULL / Boolean / Integer / Float / String，不支持运算表达式。</item>
    /// </list>
    /// </summary>
    /// <param name="tsdb">目标数据库实例。</param>
    /// <param name="statement">已解析的 INSERT 语句。</param>
    /// <returns>包含写入行数的 <see cref="InsertExecutionResult"/>。</returns>
    /// <exception cref="ArgumentNullException">任何参数为 null。</exception>
    /// <exception cref="InvalidOperationException">measurement 不存在 / 未提供任何 Field / 类型不匹配等校验失败时抛出。</exception>
    public static InsertExecutionResult ExecuteInsert(Tsdb tsdb, InsertStatement statement)
    {
        ArgumentNullException.ThrowIfNull(tsdb);
        ArgumentNullException.ThrowIfNull(statement);

        var schema = tsdb.Measurements.TryGet(statement.Measurement)
            ?? throw new InvalidOperationException(
                $"Measurement '{statement.Measurement}' 不存在；请先执行 CREATE MEASUREMENT。");

        // 解析列绑定：(timeColumnIndex, columnBindings[])
        int timeColumnIndex = -1;
        var bindings = new ColumnBinding[statement.Columns.Count];
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < statement.Columns.Count; i++)
        {
            var name = statement.Columns[i];
            if (string.Equals(name, "time", StringComparison.OrdinalIgnoreCase))
            {
                if (timeColumnIndex >= 0)
                    throw new InvalidOperationException("INSERT 列列表中 'time' 出现多次。");
                timeColumnIndex = i;
                bindings[i] = ColumnBinding.Time;
                continue;
            }

            if (!seen.Add(name))
                throw new InvalidOperationException($"INSERT 列列表中列 '{name}' 重复。");

            var col = schema.TryGetColumn(name)
                ?? throw new InvalidOperationException(
                    $"Measurement '{schema.Name}' 没有列 '{name}'。");
            bindings[i] = ColumnBinding.Schema(col);
        }

        int written = 0;
        foreach (var row in statement.Rows)
        {
            // row 长度由 parser 保证与 columns 等长
            long timestamp = timeColumnIndex < 0
                ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                : ExtractTimestamp(row[timeColumnIndex]);

            Dictionary<string, string>? tags = null;
            Dictionary<string, FieldValue>? fields = null;

            for (int i = 0; i < bindings.Length; i++)
            {
                if (i == timeColumnIndex)
                    continue;

                var binding = bindings[i];
                var literal = AsLiteral(row[i], binding.Column!.Name);

                if (binding.Column!.Role == MeasurementColumnRole.Tag)
                {
                    if (literal.Kind == SqlLiteralKind.Null)
                        throw new InvalidOperationException(
                            $"Tag 列 '{binding.Column.Name}' 不允许为 NULL。");
                    if (literal.Kind != SqlLiteralKind.String)
                        throw new InvalidOperationException(
                            $"Tag 列 '{binding.Column.Name}' 必须是字符串字面量，实际为 {literal.Kind}。");
                    tags ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    tags[binding.Column.Name] = literal.StringValue!;
                }
                else
                {
                    if (literal.Kind == SqlLiteralKind.Null)
                        throw new InvalidOperationException(
                            $"Field 列 '{binding.Column.Name}' 不允许为 NULL。");
                    var value = ConvertField(literal, binding.Column);
                    fields ??= new Dictionary<string, FieldValue>(StringComparer.Ordinal);
                    fields[binding.Column.Name] = value;
                }
            }

            if (fields is null || fields.Count == 0)
                throw new InvalidOperationException(
                    $"INSERT 行至少需要包含一个 FIELD 列值（measurement '{schema.Name}'）。");

            var point = Point.Create(schema.Name, timestamp, tags, fields);
            tsdb.Write(point);
            written++;
        }

        return new InsertExecutionResult(schema.Name, written);
    }

    /// <summary>
    /// 执行 SELECT 语句，返回投影列名与行数据。
    /// </summary>
    /// <param name="tsdb">目标 Tsdb 实例。</param>
    /// <param name="statement">已解析的 SELECT 语句。</param>
    /// <returns>包含列名与行数据的 <see cref="SelectExecutionResult"/>。</returns>
    /// <exception cref="ArgumentNullException">任何参数为 null。</exception>
    /// <exception cref="InvalidOperationException">measurement 不存在 / WHERE 包含不支持的表达式 / 投影违规等。</exception>
    public static SelectExecutionResult ExecuteSelect(Tsdb tsdb, SelectStatement statement)
    {
        ArgumentNullException.ThrowIfNull(tsdb);
        ArgumentNullException.ThrowIfNull(statement);
        return SelectExecutor.Execute(tsdb, statement);
    }

    /// <summary>
    /// 执行 DELETE 语句：把 WHERE 中 tag 等值过滤 + 时间窗 落到 PR #20 的 Tombstone 体系。
    /// 对命中 tag 过滤的所有 series × schema 中所有 Field 列追加墓碑。
    /// </summary>
    /// <param name="tsdb">目标 Tsdb 实例。</param>
    /// <param name="statement">已解析的 DELETE 语句。</param>
    /// <returns>包含 measurement 名、命中 series 数、追加墓碑数的 <see cref="DeleteExecutionResult"/>。</returns>
    /// <exception cref="ArgumentNullException">任何参数为 null。</exception>
    /// <exception cref="InvalidOperationException">measurement 不存在 / WHERE 包含不支持的表达式。</exception>
    public static DeleteExecutionResult ExecuteDelete(Tsdb tsdb, DeleteStatement statement)
    {
        ArgumentNullException.ThrowIfNull(tsdb);
        ArgumentNullException.ThrowIfNull(statement);
        return DeleteExecutor.Execute(tsdb, statement);
    }

    private static LiteralExpression AsLiteral(SqlExpression expr, string columnName)
    {
        return expr switch
        {
            LiteralExpression lit => lit,
            _ => throw new InvalidOperationException(
                $"列 '{columnName}' 的 VALUES 必须是字面量，不支持表达式 ({expr.GetType().Name})。"),
        };
    }

    private static long ExtractTimestamp(SqlExpression expr)
    {
        var lit = AsLiteral(expr, "time");
        if (lit.Kind != SqlLiteralKind.Integer)
            throw new InvalidOperationException(
                $"'time' 列必须是非负整数字面量（Unix 毫秒），实际为 {lit.Kind}。");
        if (lit.IntegerValue < 0)
            throw new InvalidOperationException(
                $"'time' 列时间戳不能为负数，实际为 {lit.IntegerValue}。");
        return lit.IntegerValue;
    }

    private static FieldValue ConvertField(LiteralExpression literal, MeasurementColumn column)
    {
        switch (column.DataType)
        {
            case FieldType.Float64:
                return literal.Kind switch
                {
                    SqlLiteralKind.Float => FieldValue.FromDouble(literal.FloatValue),
                    SqlLiteralKind.Integer => FieldValue.FromDouble(literal.IntegerValue),
                    _ => throw TypeMismatch(column, literal.Kind),
                };
            case FieldType.Int64:
                if (literal.Kind != SqlLiteralKind.Integer)
                    throw TypeMismatch(column, literal.Kind);
                return FieldValue.FromLong(literal.IntegerValue);
            case FieldType.Boolean:
                if (literal.Kind != SqlLiteralKind.Boolean)
                    throw TypeMismatch(column, literal.Kind);
                return FieldValue.FromBool(literal.BooleanValue);
            case FieldType.String:
                if (literal.Kind != SqlLiteralKind.String)
                    throw TypeMismatch(column, literal.Kind);
                return FieldValue.FromString(literal.StringValue!);
            default:
                throw new NotSupportedException($"不支持的列类型 {column.DataType}。");
        }
    }

    private static InvalidOperationException TypeMismatch(MeasurementColumn column, SqlLiteralKind actual)
        => new($"Field 列 '{column.Name}' 期望 {column.DataType}，实际字面量类别为 {actual}。");

    /// <summary>INSERT 列绑定：要么是时间戳伪列，要么是 schema 中的某一列。</summary>
    private readonly struct ColumnBinding
    {
        public MeasurementColumn? Column { get; }
        public bool IsTime => Column is null;

        private ColumnBinding(MeasurementColumn? column) { Column = column; }

        public static ColumnBinding Time { get; } = new(null);
        public static ColumnBinding Schema(MeasurementColumn column) => new(column);
    }
}
