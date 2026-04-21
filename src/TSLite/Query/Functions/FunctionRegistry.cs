using System.Diagnostics.CodeAnalysis;
using TSLite.Catalog;
using TSLite.Sql.Ast;
using TSLite.Storage.Format;

namespace TSLite.Query.Functions;

/// <summary>
/// 内置函数注册表；当前仅承载聚合函数。
/// </summary>
public static class FunctionRegistry
{
    private static readonly IAggregateFunction[] _aggregateFunctionList = CreateAggregateFunctionList();

    private static readonly IReadOnlyDictionary<string, IAggregateFunction> _aggregateFunctions =
        CreateAggregateFunctionsByName(_aggregateFunctionList);

    private static readonly IReadOnlyDictionary<Aggregator, IAggregateFunction> _aggregateFunctionsByLegacy =
        CreateAggregateFunctionsByLegacy(_aggregateFunctionList);

    /// <summary>返回所有已注册内置聚合函数。</summary>
    public static IReadOnlyCollection<IAggregateFunction> AggregateFunctions => _aggregateFunctionList;

    /// <summary>按函数名查找聚合函数（大小写不敏感）。</summary>
    /// <param name="name">SQL 函数名。</param>
    /// <param name="function">命中时返回的内置聚合函数；未命中时为 <c>null</c>。</param>
    /// <returns>是否命中。</returns>
    public static bool TryGetAggregate(string name, [MaybeNullWhen(false)] out IAggregateFunction function)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _aggregateFunctions.TryGetValue(name, out function);
    }

    /// <summary>按 legacy 聚合枚举查找内置聚合函数。</summary>
    public static IAggregateFunction GetAggregate(Aggregator aggregator)
        => _aggregateFunctionsByLegacy.TryGetValue(aggregator, out var function)
            ? function
            : throw new InvalidOperationException($"未找到与 {aggregator} 对应的内置聚合函数。");

    private static IAggregateFunction[] CreateAggregateFunctionList() =>
    [
        new BuiltInAggregateFunction("count", Aggregator.Count, allowsStarArgument: true),
        new BuiltInAggregateFunction("sum", Aggregator.Sum),
        new BuiltInAggregateFunction("min", Aggregator.Min),
        new BuiltInAggregateFunction("max", Aggregator.Max),
        new BuiltInAggregateFunction("avg", Aggregator.Avg),
        new BuiltInAggregateFunction("first", Aggregator.First),
        new BuiltInAggregateFunction("last", Aggregator.Last),
    ];

    private static IReadOnlyDictionary<string, IAggregateFunction> CreateAggregateFunctionsByName(
        IAggregateFunction[] functions)
    {
        var dict = new Dictionary<string, IAggregateFunction>(functions.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var function in functions)
            dict.Add(function.Name, function);
        return dict;
    }

    private static IReadOnlyDictionary<Aggregator, IAggregateFunction> CreateAggregateFunctionsByLegacy(
        IAggregateFunction[] functions)
    {
        var dict = new Dictionary<Aggregator, IAggregateFunction>(functions.Length);
        foreach (var function in functions)
        {
            if (function.LegacyAggregator is { } aggregator)
                dict.Add(aggregator, function);
        }

        return dict;
    }

    private sealed class BuiltInAggregateFunction : IAggregateFunction
    {
        private readonly bool _allowsStarArgument;

        public BuiltInAggregateFunction(string name, Aggregator legacyAggregator, bool allowsStarArgument = false)
        {
            Name = name;
            LegacyAggregator = legacyAggregator;
            _allowsStarArgument = allowsStarArgument;
        }

        public string Name { get; }

        public Aggregator? LegacyAggregator { get; }

        public string? ResolveFieldName(FunctionCallExpression call, MeasurementSchema schema)
        {
            ArgumentNullException.ThrowIfNull(call);
            ArgumentNullException.ThrowIfNull(schema);

            if (call.IsStar)
            {
                if (!_allowsStarArgument)
                    throw new InvalidOperationException(
                        $"仅 count(*) 允许 '*' 作为参数，{call.Name}(*) 非法。");
                return null;
            }

            if (call.Arguments.Count != 1 || call.Arguments[0] is not IdentifierExpression id)
                throw new InvalidOperationException(
                    $"{call.Name}(...) 必须接收一个列名作为参数。");

            var col = schema.TryGetColumn(id.Name)
                ?? throw new InvalidOperationException(
                    $"聚合函数 {call.Name}({id.Name}) 引用了未知列。");
            if (col.Role != MeasurementColumnRole.Field)
                throw new InvalidOperationException(
                    $"聚合函数 {call.Name}({id.Name}) 只能作用于 FIELD 列。");
            if (LegacyAggregator != Aggregator.Count && col.DataType == FieldType.String)
                throw new InvalidOperationException(
                    $"聚合函数 {call.Name} 不支持 String 类型字段 '{id.Name}'。");
            return col.Name;
        }
    }
}
