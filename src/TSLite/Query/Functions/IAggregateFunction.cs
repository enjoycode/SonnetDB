using TSLite.Catalog;
using TSLite.Sql.Ast;

namespace TSLite.Query.Functions;

/// <summary>
/// 聚合函数的最小抽象，用于注册表解析与后续扩展。
/// <para>本接口当前只承担「命名 + SQL 调用语法校验 + legacy 快路径桥接」三件事；
/// 真正可合并的聚合状态抽象（<c>Add</c> / <c>Merge</c> / <c>Finalize</c>）将随
/// Milestone 12 PR #52（Tier 2 扩展聚合）一起引入。</para>
/// </summary>
public interface IAggregateFunction : ISqlFunction
{
    /// <summary>规范函数名（小写）。</summary>
    /// <summary>
    /// 桥接到现有高性能执行路径的 legacy 聚合枚举。
    /// <para>过渡期字段：内置 7 个聚合（count/sum/min/max/avg/first/last）通过该值复用
    /// <c>QueryEngine</c> 快路径；PR #52 起新增的扩展聚合将返回 <c>null</c>，由独立算子执行。</para>
    /// </summary>
    Aggregator? LegacyAggregator { get; }

    /// <summary>
    /// 校验 SQL 调用并解析目标字段名。
    /// <para>返回 <c>null</c> 表示允许 <c>*</c> 形式（仅用于 <c>count(*)</c>）。</para>
    /// </summary>
    /// <param name="call">SQL 中的函数调用 AST 节点。</param>
    /// <param name="schema">目标 measurement 的 schema。</param>
    /// <returns>目标字段名；<c>count(*)</c> 形式返回 <c>null</c>。</returns>
    /// <exception cref="InvalidOperationException">参数个数、列存在性或类型不满足函数约束时抛出。</exception>
    string? ResolveFieldName(FunctionCallExpression call, MeasurementSchema schema);
}
