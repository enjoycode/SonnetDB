using TSLite.Catalog;
using TSLite.Query;
using TSLite.Sql.Ast;

namespace TSLite.Sql.Execution;

/// <summary>
/// <c>WHERE</c> 子句的 v1 分解结果：
/// <list type="bullet">
///   <item><description>仅支持顶层由 <c>AND</c> 连接的合取式（不允许 <c>OR</c> / <c>NOT</c>）。</description></item>
///   <item><description>每个叶子谓词必须是
///     <c>tag_col = '字符串字面量'</c> 或 <c>time</c> 与整数字面量的比较（<c>= / != / &lt; / &lt;= / &gt; / &gt;=</c>）。</description></item>
///   <item><description>不支持 field 列上的过滤；不支持表达式作为右值。</description></item>
/// </list>
/// </summary>
/// <param name="TagFilter">tag 列等值过滤集合（已合并去重；同 tag 列重复声明且取值不一致时抛错）。</param>
/// <param name="TimeRange">从 <c>time</c> 比较推导出的闭区间时间窗。</param>
internal readonly record struct WhereClause(
    IReadOnlyDictionary<string, string> TagFilter,
    TimeRange TimeRange);

internal static class WhereClauseDecomposer
{
    /// <summary>
    /// 把可选的 WHERE 表达式分解为 <see cref="WhereClause"/>。<paramref name="where"/> 为 null 时
    /// 返回空 tag 过滤 + 全时间窗。
    /// </summary>
    /// <param name="where">WHERE 表达式 AST，可为 null。</param>
    /// <param name="schema">用于校验 tag 列名是否存在 / 是否真的是 Tag 列。</param>
    /// <exception cref="InvalidOperationException">表达式不在 v1 支持的形态内时抛出。</exception>
    public static WhereClause Decompose(SqlExpression? where, MeasurementSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var tagFilter = new Dictionary<string, string>(StringComparer.Ordinal);
        long fromInclusive = long.MinValue;
        long toInclusive = long.MaxValue;

        if (where is not null)
        {
            foreach (var leaf in FlattenAnd(where))
                ApplyLeaf(leaf, schema, tagFilter, ref fromInclusive, ref toInclusive);
        }

        if (fromInclusive > toInclusive)
            throw new InvalidOperationException(
                $"WHERE 子句的时间窗为空：[from={fromInclusive}, to={toInclusive}]。");

        return new WhereClause(tagFilter, new TimeRange(fromInclusive, toInclusive));
    }

    private static IEnumerable<SqlExpression> FlattenAnd(SqlExpression expr)
    {
        if (expr is BinaryExpression { Operator: SqlBinaryOperator.And } andExpr)
        {
            foreach (var l in FlattenAnd(andExpr.Left)) yield return l;
            foreach (var r in FlattenAnd(andExpr.Right)) yield return r;
        }
        else
        {
            yield return expr;
        }
    }

    private static void ApplyLeaf(
        SqlExpression leaf,
        MeasurementSchema schema,
        Dictionary<string, string> tagFilter,
        ref long fromInclusive,
        ref long toInclusive)
    {
        if (leaf is not BinaryExpression bin || !IsComparisonOperator(bin.Operator))
            throw NotSupported(leaf, "WHERE 仅支持 tag = 'literal' 与 time 比较，且通过 AND 连接。");

        // 规范化：把字面量放到右侧。
        var (left, right, op) = NormalizeComparison(bin);

        // time vs literal
        if (left is IdentifierExpression { Name: var leftName } &&
            string.Equals(leftName, "time", StringComparison.OrdinalIgnoreCase))
        {
            ApplyTimeComparison(op, right, ref fromInclusive, ref toInclusive);
            return;
        }

        // tag_col = 'literal'
        if (op == SqlBinaryOperator.Equal &&
            left is IdentifierExpression { Name: var tagName } &&
            right is LiteralExpression { Kind: SqlLiteralKind.String, StringValue: var tagVal })
        {
            var col = schema.TryGetColumn(tagName)
                ?? throw new InvalidOperationException(
                    $"WHERE 中引用了未知列 '{tagName}'。");
            if (col.Role != MeasurementColumnRole.Tag)
                throw new InvalidOperationException(
                    $"WHERE 中只支持 tag 列等值过滤；'{tagName}' 是 {col.Role} 列。");

            if (tagFilter.TryGetValue(tagName, out var existing))
            {
                if (!string.Equals(existing, tagVal, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"WHERE 中 tag '{tagName}' 被同时约束为 '{existing}' 和 '{tagVal}'，结果集为空。");
            }
            else
            {
                tagFilter[tagName] = tagVal!;
            }
            return;
        }

        throw NotSupported(leaf, "WHERE 谓词不在 v1 支持范围内。");
    }

    private static void ApplyTimeComparison(
        SqlBinaryOperator op,
        SqlExpression right,
        ref long fromInclusive,
        ref long toInclusive)
    {
        if (right is not LiteralExpression { Kind: SqlLiteralKind.Integer, IntegerValue: var ts })
            throw new InvalidOperationException(
                "WHERE 中 'time' 比较的右值必须是整数字面量（Unix 毫秒）。");

        switch (op)
        {
            case SqlBinaryOperator.Equal:
                if (ts > fromInclusive) fromInclusive = ts;
                if (ts < toInclusive) toInclusive = ts;
                break;
            case SqlBinaryOperator.GreaterThanOrEqual:
                if (ts > fromInclusive) fromInclusive = ts;
                break;
            case SqlBinaryOperator.GreaterThan:
                if (ts == long.MaxValue)
                    throw new InvalidOperationException("'time > long.MaxValue' 永远为假。");
                if (ts + 1 > fromInclusive) fromInclusive = ts + 1;
                break;
            case SqlBinaryOperator.LessThanOrEqual:
                if (ts < toInclusive) toInclusive = ts;
                break;
            case SqlBinaryOperator.LessThan:
                if (ts == long.MinValue)
                    throw new InvalidOperationException("'time < long.MinValue' 永远为假。");
                if (ts - 1 < toInclusive) toInclusive = ts - 1;
                break;
            case SqlBinaryOperator.NotEqual:
                throw new InvalidOperationException(
                    "WHERE 中暂不支持 'time != X'（v1）。");
            default:
                throw new InvalidOperationException(
                    $"不支持的 time 比较运算符 {op}。");
        }
    }

    private static (SqlExpression Left, SqlExpression Right, SqlBinaryOperator Op) NormalizeComparison(
        BinaryExpression bin)
    {
        // 若左侧为字面量、右侧为标识符，则交换并翻转运算符
        if (bin.Left is LiteralExpression && bin.Right is IdentifierExpression)
            return (bin.Right, bin.Left, FlipComparison(bin.Operator));
        return (bin.Left, bin.Right, bin.Operator);
    }

    private static SqlBinaryOperator FlipComparison(SqlBinaryOperator op) => op switch
    {
        SqlBinaryOperator.Equal => SqlBinaryOperator.Equal,
        SqlBinaryOperator.NotEqual => SqlBinaryOperator.NotEqual,
        SqlBinaryOperator.LessThan => SqlBinaryOperator.GreaterThan,
        SqlBinaryOperator.LessThanOrEqual => SqlBinaryOperator.GreaterThanOrEqual,
        SqlBinaryOperator.GreaterThan => SqlBinaryOperator.LessThan,
        SqlBinaryOperator.GreaterThanOrEqual => SqlBinaryOperator.LessThanOrEqual,
        _ => op,
    };

    private static bool IsComparisonOperator(SqlBinaryOperator op) => op
        is SqlBinaryOperator.Equal
        or SqlBinaryOperator.NotEqual
        or SqlBinaryOperator.LessThan
        or SqlBinaryOperator.LessThanOrEqual
        or SqlBinaryOperator.GreaterThan
        or SqlBinaryOperator.GreaterThanOrEqual;

    private static InvalidOperationException NotSupported(SqlExpression expr, string detail)
        => new($"{detail} 表达式：{expr}。");
}
