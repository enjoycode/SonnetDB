using SonnetDB.Model;
using SonnetDB.Query.Functions;
using SonnetDB.Query.Functions.Window;
using Xunit;

namespace SonnetDB.Core.Tests.Query.Functions;

/// <summary>
/// PR #53 — Tier 3 窗口函数 evaluator 的纯算法单元测试（不经过 SQL 路径）。
/// </summary>
public sealed class WindowFunctionTests
{
    private static FieldValue?[] D(params double?[] values)
    {
        var result = new FieldValue?[values.Length];
        for (int i = 0; i < values.Length; i++)
            result[i] = values[i] is { } v ? FieldValue.FromDouble(v) : null;
        return result;
    }

    private static long[] T(params long[] timestamps) => timestamps;

    // ── difference / delta / increase ────────────────────────────────────

    [Fact]
    public void DifferenceEvaluator_OutputsCurrentMinusPrevious_FirstIsNull()
    {
        var ev = new DifferenceEvaluator("x", scale: 1.0, nonNegative: false);
        var result = ev.Compute(T(0, 100, 200, 300), D(10, 13, 11, 20));
        Assert.Null(result[0]);
        Assert.Equal(3.0, (double)result[1]!);
        Assert.Equal(-2.0, (double)result[2]!);
        Assert.Equal(9.0, (double)result[3]!);
    }

    [Fact]
    public void DifferenceEvaluator_NonNegative_NegativeDeltasReturnNull()
    {
        var ev = new DifferenceEvaluator("x", scale: 1.0, nonNegative: true);
        var result = ev.Compute(T(0, 100, 200), D(5, 3, 9));
        Assert.Null(result[0]);
        Assert.Null(result[1]); // 5→3 是负差
        Assert.Equal(6.0, (double)result[2]!);
    }

    // ── derivative / rate ────────────────────────────────────────────────

    [Fact]
    public void DerivativeEvaluator_DividesByDtInSecondsByDefault()
    {
        // 1000ms / 1000ms = 1s 间隔
        var ev = new DerivativeEvaluator("x", unitMs: 1000, nonNegative: false);
        var result = ev.Compute(T(0, 1000, 3000), D(10, 20, 50));
        Assert.Null(result[0]);
        Assert.Equal(10.0, (double)result[1]!);   // (20-10)/1s = 10
        Assert.Equal(15.0, (double)result[2]!);   // (50-20)/2s = 15
    }

    [Fact]
    public void DerivativeEvaluator_NonNegative_DropsCounterReset()
    {
        var ev = new DerivativeEvaluator("x", unitMs: 1000, nonNegative: true);
        var result = ev.Compute(T(0, 1000, 2000), D(100, 50, 80));
        Assert.Null(result[0]);
        Assert.Null(result[1]); // 100→50 reset
        Assert.Equal(30.0, (double)result[2]!);
    }

    // ── cumulative_sum / integral ────────────────────────────────────────

    [Fact]
    public void CumulativeSumEvaluator_RunsRunningTotal()
    {
        var ev = new CumulativeSumEvaluator("x");
        var result = ev.Compute(T(0, 1, 2, 3, 4), D(1, 2, 3, 4, 5));
        Assert.Equal(1.0, (double)result[0]!);
        Assert.Equal(3.0, (double)result[1]!);
        Assert.Equal(6.0, (double)result[2]!);
        Assert.Equal(10.0, (double)result[3]!);
        Assert.Equal(15.0, (double)result[4]!);
    }

    [Fact]
    public void IntegralEvaluator_TrapezoidalRule_OnConstantValue()
    {
        // f=5 on [0, 1000ms] → 面积 = 5 * 1s = 5
        var ev = new IntegralEvaluator("x", unitMs: 1000);
        var result = ev.Compute(T(0, 1000), D(5, 5));
        Assert.Equal(0.0, (double)result[0]!);
        Assert.Equal(5.0, (double)result[1]!, precision: 6);
    }

    [Fact]
    public void IntegralEvaluator_TrapezoidalRule_OnLinearRamp()
    {
        // f(t) = t/1000 on [0, 1000, 2000] → 累计面积 0, 0.5, 2.0
        var ev = new IntegralEvaluator("x", unitMs: 1000);
        var result = ev.Compute(T(0, 1000, 2000), D(0, 1, 2));
        Assert.Equal(0.0, (double)result[0]!);
        Assert.Equal(0.5, (double)result[1]!, precision: 6);
        Assert.Equal(2.0, (double)result[2]!, precision: 6);
    }

    // ── moving_average / ewma ────────────────────────────────────────────

    [Fact]
    public void MovingAverageEvaluator_NPointsWindow_FirstNMinus1AreNull()
    {
        var ev = new MovingAverageEvaluator("x", windowSize: 3);
        var result = ev.Compute(T(0, 1, 2, 3, 4), D(1, 2, 3, 4, 5));
        Assert.Null(result[0]);
        Assert.Null(result[1]);
        Assert.Equal(2.0, (double)result[2]!); // (1+2+3)/3
        Assert.Equal(3.0, (double)result[3]!); // (2+3+4)/3
        Assert.Equal(4.0, (double)result[4]!); // (3+4+5)/3
    }

    [Fact]
    public void EwmaEvaluator_AlphaOne_IsIdentity()
    {
        var ev = new EwmaEvaluator("x", alpha: 1.0);
        var result = ev.Compute(T(0, 1, 2), D(10, 20, 30));
        Assert.Equal(10.0, (double)result[0]!);
        Assert.Equal(20.0, (double)result[1]!);
        Assert.Equal(30.0, (double)result[2]!);
    }

    [Fact]
    public void EwmaEvaluator_RecursiveSmoothing()
    {
        var ev = new EwmaEvaluator("x", alpha: 0.5);
        // s0 = 10
        // s1 = 0.5*20 + 0.5*10 = 15
        // s2 = 0.5*30 + 0.5*15 = 22.5
        var result = ev.Compute(T(0, 1, 2), D(10, 20, 30));
        Assert.Equal(10.0, (double)result[0]!);
        Assert.Equal(15.0, (double)result[1]!, precision: 6);
        Assert.Equal(22.5, (double)result[2]!, precision: 6);
    }

    // ── fill / locf / interpolate ────────────────────────────────────────

    [Fact]
    public void FillEvaluator_ReplacesNullsWithConstant()
    {
        var ev = new FillEvaluator("x", fill: -1.0);
        var result = ev.Compute(T(0, 1, 2, 3), D(10, null, null, 20));
        Assert.Equal(10.0, (double)result[0]!);
        Assert.Equal(-1.0, (double)result[1]!);
        Assert.Equal(-1.0, (double)result[2]!);
        Assert.Equal(20.0, (double)result[3]!);
    }

    [Fact]
    public void LocfEvaluator_CarriesForward()
    {
        var ev = new LocfEvaluator("x");
        var result = ev.Compute(T(0, 1, 2, 3, 4), D(null, 5, null, null, 8));
        Assert.Null(result[0]);
        Assert.Equal(5.0, (double)result[1]!);
        Assert.Equal(5.0, (double)result[2]!);
        Assert.Equal(5.0, (double)result[3]!);
        Assert.Equal(8.0, (double)result[4]!);
    }

    [Fact]
    public void InterpolateEvaluator_LinearBetweenAnchors()
    {
        var ev = new InterpolateEvaluator("x");
        // 时间戳 0,10,20,30,40 值 10, null, null, null, 50 → 中间 20,30,40
        var result = ev.Compute(T(0, 10, 20, 30, 40), D(10, null, null, null, 50));
        Assert.Equal(10.0, (double)result[0]!);
        Assert.Equal(20.0, (double)result[1]!, precision: 6);
        Assert.Equal(30.0, (double)result[2]!, precision: 6);
        Assert.Equal(40.0, (double)result[3]!, precision: 6);
        Assert.Equal(50.0, (double)result[4]!);
    }

    [Fact]
    public void InterpolateEvaluator_LeadingTrailingNullsStayNull()
    {
        var ev = new InterpolateEvaluator("x");
        var result = ev.Compute(T(0, 10, 20, 30), D(null, 5, 7, null));
        Assert.Null(result[0]);
        Assert.Equal(5.0, (double)result[1]!);
        Assert.Equal(7.0, (double)result[2]!);
        Assert.Null(result[3]);
    }

    // ── state_changes / state_duration ───────────────────────────────────

    [Fact]
    public void StateChangesEvaluator_CountsTransitions()
    {
        var ev = new StateChangesEvaluator("x");
        var result = ev.Compute(T(0, 1, 2, 3, 4), D(1, 1, 2, 2, 3));
        Assert.Equal(0L, result[0]);
        Assert.Equal(0L, result[1]);
        Assert.Equal(1L, result[2]);
        Assert.Equal(1L, result[3]);
        Assert.Equal(2L, result[4]);
    }

    [Fact]
    public void StateDurationEvaluator_ResetsOnTransition()
    {
        var ev = new StateDurationEvaluator("x");
        // ts: 0,100,200,500,1000 values: A,A,A,B,B
        var result = ev.Compute(
            T(0, 100, 200, 500, 1000),
            D(1, 1, 1, 2, 2));
        Assert.Equal(0L, result[0]);
        Assert.Equal(100L, result[1]);
        Assert.Equal(200L, result[2]);
        Assert.Equal(0L, result[3]);   // 状态变化
        Assert.Equal(500L, result[4]);
    }

    // ── HoltWinters ──────────────────────────────────────────────────────

    [Fact]
    public void HoltWintersEvaluator_OnLinearTrend_TracksClosely()
    {
        var ev = new HoltWintersEvaluator("x", alpha: 0.8, beta: 0.3);
        var values = D(1, 2, 3, 4, 5, 6, 7, 8);
        var result = ev.Compute(T(0, 1, 2, 3, 4, 5, 6, 7), values);
        // 最后几个点应非常接近真实值 ±1
        Assert.InRange((double)result[7]!, 7.0, 9.5);
    }

    // ── FunctionRegistry 注册校验 ─────────────────────────────────────────

    [Theory]
    [InlineData("difference")]
    [InlineData("delta")]
    [InlineData("derivative")]
    [InlineData("non_negative_derivative")]
    [InlineData("rate")]
    [InlineData("irate")]
    [InlineData("increase")]
    [InlineData("cumulative_sum")]
    [InlineData("integral")]
    [InlineData("moving_average")]
    [InlineData("ewma")]
    [InlineData("holt_winters")]
    [InlineData("fill")]
    [InlineData("locf")]
    [InlineData("interpolate")]
    [InlineData("state_changes")]
    [InlineData("state_duration")]
    public void FunctionRegistry_RegistersWindowFunction(string name)
    {
        Assert.Equal(FunctionKind.Window, FunctionRegistry.GetFunctionKind(name));
        Assert.True(FunctionRegistry.TryGetWindow(name, out var fn));
        Assert.Equal(name, fn!.Name);
    }
}
