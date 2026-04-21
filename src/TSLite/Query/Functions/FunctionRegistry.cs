using System.Diagnostics.CodeAnalysis;
using TSLite.Catalog;
using TSLite.Query.Functions.Aggregates;
using TSLite.Query.Functions.Control;
using TSLite.Query.Functions.Window;
using TSLite.Sql.Ast;
using TSLite.Storage.Format;

namespace TSLite.Query.Functions;

/// <summary>
/// 内置函数注册表；当前承载聚合函数、标量函数与窗口函数。
/// </summary>
public static class FunctionRegistry
{
    private static readonly IAggregateFunction[] _aggregateFunctionList = CreateAggregateFunctionList();
    private static readonly IScalarFunction[] _scalarFunctionList = CreateScalarFunctionList();
    private static readonly IWindowFunction[] _windowFunctionList = CreateWindowFunctionList();

    private static readonly IReadOnlyDictionary<string, IAggregateFunction> _aggregateFunctions =
        CreateFunctionsByName(_aggregateFunctionList);

    private static readonly IReadOnlyDictionary<string, IScalarFunction> _scalarFunctions =
        CreateFunctionsByName(_scalarFunctionList);

    private static readonly IReadOnlyDictionary<string, IWindowFunction> _windowFunctions =
        CreateFunctionsByName(_windowFunctionList);

    private static readonly IReadOnlyDictionary<Aggregator, IAggregateFunction> _aggregateFunctionsByLegacy =
        CreateAggregateFunctionsByLegacy(_aggregateFunctionList);

    /// <summary>返回所有已注册内置聚合函数。</summary>
    public static IReadOnlyCollection<IAggregateFunction> AggregateFunctions => _aggregateFunctionList;

    /// <summary>返回所有已注册内置标量函数。</summary>
    public static IReadOnlyCollection<IScalarFunction> ScalarFunctions => _scalarFunctionList;

    /// <summary>返回所有已注册内置窗口函数。</summary>
    public static IReadOnlyCollection<IWindowFunction> WindowFunctions => _windowFunctionList;

    /// <summary>按函数名查找聚合函数（大小写不敏感）。</summary>
    public static bool TryGetAggregate(string name, [MaybeNullWhen(false)] out IAggregateFunction function)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _aggregateFunctions.TryGetValue(name, out function);
    }

    /// <summary>按函数名查找标量函数（大小写不敏感）。</summary>
    public static bool TryGetScalar(string name, [MaybeNullWhen(false)] out IScalarFunction function)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _scalarFunctions.TryGetValue(name, out function);
    }

    /// <summary>按函数名查找窗口函数（大小写不敏感）。</summary>
    public static bool TryGetWindow(string name, [MaybeNullWhen(false)] out IWindowFunction function)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _windowFunctions.TryGetValue(name, out function);
    }

    /// <summary>判断函数名属于哪一类内置函数。</summary>
    public static FunctionKind GetFunctionKind(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_aggregateFunctions.ContainsKey(name)) return FunctionKind.Aggregate;
        if (_scalarFunctions.ContainsKey(name)) return FunctionKind.Scalar;
        if (_windowFunctions.ContainsKey(name)) return FunctionKind.Window;
        return FunctionKind.Unknown;
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
        // Tier 2 — 扩展聚合（PR #52）
        new StddevFunction(),
        new VarianceFunction(),
        new SpreadFunction(),
        new ModeFunction(),
        new FixedPercentileFunction("median", 0.5),
        new PercentileFunction(),
        new FixedPercentileFunction("p50", 0.50),
        new FixedPercentileFunction("p90", 0.90),
        new FixedPercentileFunction("p95", 0.95),
        new FixedPercentileFunction("p99", 0.99),
        new TDigestAggFunction(),
        new DistinctCountFunction(),
        new HistogramFunction(),
        // Tier 4 — PID 控制律（PR #54）
        new PidAggregateFunction(),
        new PidEstimateFunction(),
    ];

    private static IScalarFunction[] CreateScalarFunctionList() =>
    [
        new BuiltInScalarFunction("abs", 1, 1, static args => Math.Abs(RequireDouble(args[0], "abs"))),
        new BuiltInScalarFunction("round", 1, 2, EvaluateRound),
        new BuiltInScalarFunction("sqrt", 1, 1, static args => Math.Sqrt(RequireDouble(args[0], "sqrt"))),
        new BuiltInScalarFunction("log", 1, 2, EvaluateLog),
        new BuiltInScalarFunction("coalesce", 1, int.MaxValue, EvaluateCoalesce),
    ];

    private static IWindowFunction[] CreateWindowFunctionList() =>
    [
        // Tier 3 — 差分类（PR #53）
        new DifferenceFunction(),
        new DeltaFunction(),
        new IncreaseFunction(),
        new DerivativeFunction(),
        new NonNegativeDerivativeFunction(),
        new RateFunction(),
        new IrateFunction(),
        // Tier 3 — 累计 / 积分
        new CumulativeSumFunction(),
        new IntegralFunction(),
        // Tier 3 — 平滑 / 预测
        new MovingAverageFunction(),
        new EwmaFunction(),
        new HoltWintersFunction(),
        // Tier 3 — 缺失值处理
        new FillFunction(),
        new LocfFunction(),
        new InterpolateFunction(),
        // Tier 3 — 状态分析
        new StateChangesFunction(),
        new StateDurationFunction(),
        // Tier 4 — PID 行级控制律（PR #54）
        new PidSeriesFunction(),
        // Tier 4 — 异常 / 变点检测（PR #55）
        new AnomalyFunction(),
        new ChangepointFunction(),
    ];

    private static IReadOnlyDictionary<string, TFunction> CreateFunctionsByName<TFunction>(TFunction[] functions)
        where TFunction : class, ISqlFunction
    {
        var dict = new Dictionary<string, TFunction>(functions.Length, StringComparer.OrdinalIgnoreCase);
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

    private static object? EvaluateRound(IReadOnlyList<object?> args)
    {
        double value = RequireDouble(args[0], "round");
        if (args.Count == 1)
            return Math.Round(value);

        int digits = checked((int)RequireDouble(args[1], "round"));
        return Math.Round(value, digits);
    }

    private static object? EvaluateLog(IReadOnlyList<object?> args)
    {
        double value = RequireDouble(args[0], "log");
        if (args.Count == 1)
            return Math.Log(value);

        double newBase = RequireDouble(args[1], "log");
        return Math.Log(value, newBase);
    }

    private static object? EvaluateCoalesce(IReadOnlyList<object?> args)
    {
        foreach (var arg in args)
        {
            if (arg is not null)
                return arg;
        }

        return null;
    }

    private static double RequireDouble(object? value, string functionName)
    {
        return value switch
        {
            byte b => b,
            sbyte sb => sb,
            short s => s,
            ushort us => us,
            int i => i,
            uint ui => ui,
            long l => l,
            ulong ul => ul,
            float f => f,
            double d => d,
            decimal m => (double)m,
            null => throw new InvalidOperationException($"函数 {functionName} 不接受 NULL 参数。"),
            _ => throw new InvalidOperationException($"函数 {functionName} 需要数值参数。"),
        };
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

    private sealed class BuiltInScalarFunction : IScalarFunction
    {
        private readonly Func<IReadOnlyList<object?>, object?> _evaluator;

        public BuiltInScalarFunction(string name, int minArgumentCount, int maxArgumentCount,
            Func<IReadOnlyList<object?>, object?> evaluator)
        {
            Name = name;
            MinArgumentCount = minArgumentCount;
            MaxArgumentCount = maxArgumentCount;
            _evaluator = evaluator;
        }

        public string Name { get; }

        public int MinArgumentCount { get; }

        public int MaxArgumentCount { get; }

        public object? Evaluate(IReadOnlyList<object?> args)
        {
            ArgumentNullException.ThrowIfNull(args);
            if (args.Count < MinArgumentCount || args.Count > MaxArgumentCount)
            {
                string expected = MinArgumentCount == MaxArgumentCount
                    ? MinArgumentCount.ToString()
                    : $"{MinArgumentCount}~{MaxArgumentCount}";
                throw new InvalidOperationException(
                    $"函数 {Name} 需要 {expected} 个参数，实际为 {args.Count}。");
            }

            return _evaluator(args);
        }
    }
}
