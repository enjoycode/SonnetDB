using TSLite.Catalog;
using TSLite.Engine;
using TSLite.Sql.Ast;
using TSLite.Storage.Format;

namespace TSLite.Sql.Execution;

/// <summary>
/// 把 <see cref="SqlStatement"/> AST 应用到 <see cref="Tsdb"/> 实例的执行器。
/// 当前 Milestone 仅支持 <see cref="CreateMeasurementStatement"/>，其余语句留待后续 PR。
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
    {
        ArgumentNullException.ThrowIfNull(tsdb);
        ArgumentNullException.ThrowIfNull(sql);

        var statement = SqlParser.Parse(sql);
        return ExecuteStatement(tsdb, statement);
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
    {
        ArgumentNullException.ThrowIfNull(tsdb);
        ArgumentNullException.ThrowIfNull(statement);

        return statement switch
        {
            CreateMeasurementStatement create => ExecuteCreateMeasurement(tsdb, create),
            _ => throw new NotSupportedException(
                $"SQL 语句类型 '{statement.GetType().Name}' 尚未实现。"),
        };
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
}
