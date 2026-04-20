using System.Data;
using System.Data.Common;

namespace TSLite.Ado;

/// <summary>
/// TSLite ADO.NET 参数。仅支持基础标量类型；用于 <see cref="TsdbCommand"/> 中 <c>@name</c> / <c>:name</c>
/// 占位符的字面量替换（替换前会做 SQL 转义，避免注入）。
/// </summary>
/// <remarks>
/// 支持的运行时类型：<see cref="string"/> / <see cref="bool"/> / <see cref="byte"/> / <see cref="short"/> /
/// <see cref="int"/> / <see cref="long"/> / <see cref="float"/> / <see cref="double"/> / <see cref="decimal"/> /
/// <see cref="DateTime"/>（按 UTC Unix 毫秒）/ <see cref="DateTimeOffset"/>（按 Unix 毫秒）/
/// <see cref="DBNull"/> 或 null。其他类型抛出 <see cref="NotSupportedException"/>。
/// </remarks>
public sealed class TsdbParameter : DbParameter
{
    /// <summary>构造一个空参数。</summary>
    public TsdbParameter() { }

    /// <summary>构造命名参数并赋值。</summary>
    public TsdbParameter(string parameterName, object? value)
    {
        ParameterName = parameterName;
        Value = value;
    }

    /// <inheritdoc />
    public override DbType DbType { get; set; } = DbType.Object;

    /// <inheritdoc />
    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

    /// <inheritdoc />
    public override bool IsNullable { get; set; } = true;

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ParameterName { get; set; } = string.Empty;

    /// <inheritdoc />
    public override int Size { get; set; }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string SourceColumn { get; set; } = string.Empty;

    /// <inheritdoc />
    public override bool SourceColumnNullMapping { get; set; }

    /// <inheritdoc />
    public override object? Value { get; set; }

    /// <inheritdoc />
    public override void ResetDbType() => DbType = DbType.Object;
}
