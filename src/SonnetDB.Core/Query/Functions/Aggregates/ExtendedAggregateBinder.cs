using SonnetDB.Catalog;
using SonnetDB.Sql.Ast;
using SonnetDB.Storage.Format;

namespace SonnetDB.Query.Functions.Aggregates;

/// <summary>
/// 扩展聚合函数共享的参数解析与列校验工具。
/// </summary>
internal static class ExtendedAggregateBinder
{
    /// <summary>
    /// 校验 <c>fn(field)</c> 形式（单参数、字段必须为数值列），返回字段名。
    /// </summary>
    public static string ResolveSingleNumericField(
        FunctionCallExpression call, MeasurementSchema schema, string functionName)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(schema);

        if (call.IsStar)
            throw new InvalidOperationException($"{functionName}(*) 非法。");
        if (call.Arguments.Count != 1)
            throw new InvalidOperationException(
                $"{functionName}(...) 需要 1 个参数（字段名），实际 {call.Arguments.Count}。");
        if (call.Arguments[0] is not IdentifierExpression id)
            throw new InvalidOperationException(
                $"{functionName}(...) 第一个参数必须是字段名。");

        var col = schema.TryGetColumn(id.Name)
            ?? throw new InvalidOperationException($"{functionName}({id.Name}) 引用了未知列。");
        if (col.Role != MeasurementColumnRole.Field)
            throw new InvalidOperationException(
                $"{functionName} 只能作用于 FIELD 列（'{id.Name}' 是 {col.Role}）。");
        if (col.DataType == FieldType.String)
            throw new InvalidOperationException(
                $"{functionName} 不支持 String 字段 '{id.Name}'。");
        return col.Name;
    }

    /// <summary>
    /// 校验 <c>fn(field, numeric_literal)</c> 形式，返回 (字段名, 第二参数数值)。
    /// </summary>
    public static (string FieldName, double NumericArgument) ResolveFieldAndNumeric(
        FunctionCallExpression call, MeasurementSchema schema, string functionName)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(schema);

        if (call.IsStar)
            throw new InvalidOperationException($"{functionName}(*) 非法。");
        if (call.Arguments.Count != 2)
            throw new InvalidOperationException(
                $"{functionName}(...) 需要 2 个参数（字段名，数值常量），实际 {call.Arguments.Count}。");

        if (call.Arguments[0] is not IdentifierExpression id)
            throw new InvalidOperationException(
                $"{functionName}(...) 第一个参数必须是字段名。");
        var col = schema.TryGetColumn(id.Name)
            ?? throw new InvalidOperationException($"{functionName}({id.Name}, ...) 引用了未知列。");
        if (col.Role != MeasurementColumnRole.Field)
            throw new InvalidOperationException(
                $"{functionName} 只能作用于 FIELD 列（'{id.Name}' 是 {col.Role}）。");
        if (col.DataType == FieldType.String)
            throw new InvalidOperationException(
                $"{functionName} 不支持 String 字段 '{id.Name}'。");

        double numeric = call.Arguments[1] switch
        {
            LiteralExpression { Kind: SqlLiteralKind.Integer } lit => lit.IntegerValue,
            LiteralExpression { Kind: SqlLiteralKind.Float } lit => lit.FloatValue,
            _ => throw new InvalidOperationException(
                $"{functionName}(...) 第二个参数必须是数值常量。"),
        };
        return (col.Name, numeric);
    }
}
