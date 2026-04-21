using SonnetDB.Catalog;
using SonnetDB.Model;
using SonnetDB.Sql.Ast;

namespace SonnetDB.Query.Functions.Window;

// ────────────────────────────────────────────────────────────────────────────
// 累计 / 积分类窗口函数：cumulative_sum / integral
// ────────────────────────────────────────────────────────────────────────────

/// <summary><c>cumulative_sum(field)</c>：从首行起的累计和；缺失值保留前一累计值。</summary>
internal sealed class CumulativeSumFunction : IWindowFunction
{
    public string Name => "cumulative_sum";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 1, 1);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        return new CumulativeSumEvaluator(col.Name);
    }
}

internal sealed class CumulativeSumEvaluator : IWindowEvaluator
{
    public CumulativeSumEvaluator(string fieldName) => FieldName = fieldName;

    public string FieldName { get; }

    public object?[] Compute(long[] timestamps, FieldValue?[] values)
    {
        var output = new object?[timestamps.Length];
        double sum = 0;
        bool seen = false;
        for (int i = 0; i < timestamps.Length; i++)
        {
            if (WindowFunctionBinder.TryToDouble(values[i], out var v))
            {
                sum += v;
                seen = true;
            }
            output[i] = seen ? sum : null;
        }
        return output;
    }
}

/// <summary>
/// <c>integral(field [, unit])</c>：基于梯形法的累计积分，单位默认 1 秒。
/// 缺失值跳过、不参与积分（视为采样间断）。
/// </summary>
internal sealed class IntegralFunction : IWindowFunction
{
    public string Name => "integral";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 1, 2);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        long unitMs = WindowFunctionBinder.ResolveUnitMillisecondsArgument(call, 1, Name);
        return new IntegralEvaluator(col.Name, unitMs);
    }
}

internal sealed class IntegralEvaluator : IWindowEvaluator
{
    private readonly long _unitMs;

    public IntegralEvaluator(string fieldName, long unitMs)
    {
        FieldName = fieldName;
        _unitMs = unitMs;
    }

    public string FieldName { get; }

    public object?[] Compute(long[] timestamps, FieldValue?[] values)
    {
        var output = new object?[timestamps.Length];
        double area = 0;
        double? prev = null;
        long prevTs = 0;
        for (int i = 0; i < timestamps.Length; i++)
        {
            if (!WindowFunctionBinder.TryToDouble(values[i], out var cur))
            {
                output[i] = prev is null ? null : (object)area;
                continue;
            }

            if (prev is { } p)
            {
                long dtMs = timestamps[i] - prevTs;
                if (dtMs > 0)
                    area += 0.5 * (p + cur) * dtMs / _unitMs;
            }

            output[i] = area;
            prev = cur;
            prevTs = timestamps[i];
        }
        return output;
    }
}
