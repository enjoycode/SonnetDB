using TSLite.Catalog;
using TSLite.Sql.Ast;

namespace TSLite.Query.Functions.Aggregates;

/// <summary>
/// 扩展聚合函数公共基类：固定 <see cref="IAggregateFunction.LegacyAggregator"/> 为 <c>null</c>，
/// 派生类只需实现累加器创建。
/// </summary>
internal abstract class ExtendedAggregateFunction : IAggregateFunction
{
    public abstract string Name { get; }

    public Aggregator? LegacyAggregator => null;

    public abstract string? ResolveFieldName(FunctionCallExpression call, MeasurementSchema schema);

    public abstract IAggregateAccumulator CreateAccumulator(
        FunctionCallExpression call, MeasurementSchema schema);

    IAggregateAccumulator? IAggregateFunction.CreateAccumulator(
        FunctionCallExpression call, MeasurementSchema schema) => CreateAccumulator(call, schema);
}

// ── stddev / variance ────────────────────────────────────────────────────

internal sealed class StddevFunction : ExtendedAggregateFunction
{
    public override string Name => "stddev";

    public override string? ResolveFieldName(FunctionCallExpression call, MeasurementSchema schema)
        => ExtendedAggregateBinder.ResolveSingleNumericField(call, schema, Name);

    public override IAggregateAccumulator CreateAccumulator(
        FunctionCallExpression call, MeasurementSchema schema) => new StddevAccumulator();
}

internal sealed class VarianceFunction : ExtendedAggregateFunction
{
    public override string Name => "variance";

    public override string? ResolveFieldName(FunctionCallExpression call, MeasurementSchema schema)
        => ExtendedAggregateBinder.ResolveSingleNumericField(call, schema, Name);

    public override IAggregateAccumulator CreateAccumulator(
        FunctionCallExpression call, MeasurementSchema schema) => new VarianceAccumulator();
}

internal sealed class StddevAccumulator : IAggregateAccumulator
{
    private readonly WelfordAccumulator _state = new();

    public long Count => _state.Count;

    public void Add(double value) => _state.Add(value);

    public void Merge(IAggregateAccumulator other)
    {
        if (other is not StddevAccumulator s)
            throw new ArgumentException($"Cannot merge {other.GetType().Name} into StddevAccumulator.", nameof(other));
        _state.Merge(s._state);
    }

    public object? Finalize() => Count >= 2 ? _state.SampleStdDev : null;
}

internal sealed class VarianceAccumulator : IAggregateAccumulator
{
    private readonly WelfordAccumulator _state = new();

    public long Count => _state.Count;

    public void Add(double value) => _state.Add(value);

    public void Merge(IAggregateAccumulator other)
    {
        if (other is not VarianceAccumulator v)
            throw new ArgumentException($"Cannot merge {other.GetType().Name} into VarianceAccumulator.", nameof(other));
        _state.Merge(v._state);
    }

    public object? Finalize() => Count >= 2 ? _state.SampleVariance : null;
}

// ── spread ───────────────────────────────────────────────────────────────

internal sealed class SpreadFunction : ExtendedAggregateFunction
{
    public override string Name => "spread";

    public override string? ResolveFieldName(FunctionCallExpression call, MeasurementSchema schema)
        => ExtendedAggregateBinder.ResolveSingleNumericField(call, schema, Name);

    public override IAggregateAccumulator CreateAccumulator(
        FunctionCallExpression call, MeasurementSchema schema) => new SpreadAccumulator();
}

internal sealed class SpreadAccumulator : IAggregateAccumulator
{
    private double _min = double.PositiveInfinity;
    private double _max = double.NegativeInfinity;

    public long Count { get; private set; }

    public void Add(double value)
    {
        if (double.IsNaN(value)) return;
        Count++;
        if (value < _min) _min = value;
        if (value > _max) _max = value;
    }

    public void Merge(IAggregateAccumulator other)
    {
        if (other is not SpreadAccumulator s)
            throw new ArgumentException($"Cannot merge {other.GetType().Name} into SpreadAccumulator.", nameof(other));
        if (s.Count == 0) return;
        Count += s.Count;
        if (s._min < _min) _min = s._min;
        if (s._max > _max) _max = s._max;
    }

    public object? Finalize() => Count == 0 ? null : (object)(_max - _min);
}

// ── mode ─────────────────────────────────────────────────────────────────

internal sealed class ModeFunction : ExtendedAggregateFunction
{
    public override string Name => "mode";

    public override string? ResolveFieldName(FunctionCallExpression call, MeasurementSchema schema)
        => ExtendedAggregateBinder.ResolveSingleNumericField(call, schema, Name);

    public override IAggregateAccumulator CreateAccumulator(
        FunctionCallExpression call, MeasurementSchema schema) => new ModeAccumulator();
}

internal sealed class ModeAccumulator : IAggregateAccumulator
{
    private readonly Dictionary<double, long> _counts = new();

    public long Count { get; private set; }

    public void Add(double value)
    {
        if (double.IsNaN(value)) return;
        Count++;
        ref long slot = ref System.Runtime.InteropServices.CollectionsMarshal
            .GetValueRefOrAddDefault(_counts, value, out _);
        slot++;
    }

    public void Merge(IAggregateAccumulator other)
    {
        if (other is not ModeAccumulator m)
            throw new ArgumentException($"Cannot merge {other.GetType().Name} into ModeAccumulator.", nameof(other));
        foreach (var (k, v) in m._counts)
        {
            ref long slot = ref System.Runtime.InteropServices.CollectionsMarshal
                .GetValueRefOrAddDefault(_counts, k, out _);
            slot += v;
        }
        Count += m.Count;
    }

    public object? Finalize()
    {
        if (Count == 0) return null;
        long bestCount = 0;
        double bestValue = 0;
        foreach (var (k, v) in _counts)
        {
            if (v > bestCount || (v == bestCount && k < bestValue))
            {
                bestCount = v;
                bestValue = k;
            }
        }
        return bestValue;
    }
}

// ── percentile / median / pXX ────────────────────────────────────────────

internal sealed class PercentileFunction : ExtendedAggregateFunction
{
    public override string Name => "percentile";

    public override string? ResolveFieldName(FunctionCallExpression call, MeasurementSchema schema)
    {
        var (field, _) = ExtendedAggregateBinder.ResolveFieldAndNumeric(call, schema, Name);
        return field;
    }

    public override IAggregateAccumulator CreateAccumulator(
        FunctionCallExpression call, MeasurementSchema schema)
    {
        var (_, q) = ExtendedAggregateBinder.ResolveFieldAndNumeric(call, schema, Name);
        if (q <= 0 || q > 100)
            throw new InvalidOperationException(
                $"percentile(...) 第二个参数 q 必须落在 (0, 100] 区间，实际 {q}。");
        return new PercentileAccumulator(q / 100.0);
    }
}

internal sealed class FixedPercentileFunction : ExtendedAggregateFunction
{
    private readonly double _q;
    public override string Name { get; }

    public FixedPercentileFunction(string name, double q)
    {
        Name = name;
        _q = q;
    }

    public override string? ResolveFieldName(FunctionCallExpression call, MeasurementSchema schema)
        => ExtendedAggregateBinder.ResolveSingleNumericField(call, schema, Name);

    public override IAggregateAccumulator CreateAccumulator(
        FunctionCallExpression call, MeasurementSchema schema) => new PercentileAccumulator(_q);
}

internal sealed class PercentileAccumulator : IAggregateAccumulator
{
    private readonly double _q;
    private readonly TDigest _digest = new();

    public PercentileAccumulator(double q)
    {
        if (q <= 0 || q > 1)
            throw new ArgumentOutOfRangeException(nameof(q));
        _q = q;
    }

    public long Count => _digest.Count;

    public void Add(double value) => _digest.Add(value);

    public void Merge(IAggregateAccumulator other)
    {
        if (other is not PercentileAccumulator p)
            throw new ArgumentException($"Cannot merge {other.GetType().Name} into PercentileAccumulator.", nameof(other));
        _digest.Merge(p._digest);
    }

    public object? Finalize() => Count == 0 ? null : (object)_digest.Quantile(_q);
}

// ── tdigest_agg ──────────────────────────────────────────────────────────

internal sealed class TDigestAggFunction : ExtendedAggregateFunction
{
    public override string Name => "tdigest_agg";

    public override string? ResolveFieldName(FunctionCallExpression call, MeasurementSchema schema)
        => ExtendedAggregateBinder.ResolveSingleNumericField(call, schema, Name);

    public override IAggregateAccumulator CreateAccumulator(
        FunctionCallExpression call, MeasurementSchema schema) => new TDigestAggAccumulator();
}

internal sealed class TDigestAggAccumulator : IAggregateAccumulator
{
    private readonly TDigest _digest = new();

    public long Count => _digest.Count;

    public void Add(double value) => _digest.Add(value);

    public void Merge(IAggregateAccumulator other)
    {
        if (other is not TDigestAggAccumulator t)
            throw new ArgumentException($"Cannot merge {other.GetType().Name} into TDigestAggAccumulator.", nameof(other));
        _digest.Merge(t._digest);
    }

    public object? Finalize() => Count == 0 ? null : (object)_digest.ToJson();
}

// ── distinct_count ───────────────────────────────────────────────────────

internal sealed class DistinctCountFunction : ExtendedAggregateFunction
{
    public override string Name => "distinct_count";

    public override string? ResolveFieldName(FunctionCallExpression call, MeasurementSchema schema)
        => ExtendedAggregateBinder.ResolveSingleNumericField(call, schema, Name);

    public override IAggregateAccumulator CreateAccumulator(
        FunctionCallExpression call, MeasurementSchema schema) => new DistinctCountAccumulator();
}

internal sealed class DistinctCountAccumulator : IAggregateAccumulator
{
    private readonly HyperLogLog _hll = new();

    public long Count { get; private set; }

    public void Add(double value)
    {
        if (double.IsNaN(value)) return;
        Count++;
        _hll.Add(value);
    }

    public void Merge(IAggregateAccumulator other)
    {
        if (other is not DistinctCountAccumulator d)
            throw new ArgumentException($"Cannot merge {other.GetType().Name} into DistinctCountAccumulator.", nameof(other));
        _hll.Merge(d._hll);
        Count += d.Count;
    }

    public object? Finalize() => Count == 0 ? (object)0L : _hll.Estimate();
}

// ── histogram ────────────────────────────────────────────────────────────

internal sealed class HistogramFunction : ExtendedAggregateFunction
{
    public override string Name => "histogram";

    public override string? ResolveFieldName(FunctionCallExpression call, MeasurementSchema schema)
    {
        var (field, _) = ExtendedAggregateBinder.ResolveFieldAndNumeric(call, schema, Name);
        return field;
    }

    public override IAggregateAccumulator CreateAccumulator(
        FunctionCallExpression call, MeasurementSchema schema)
    {
        var (_, binWidth) = ExtendedAggregateBinder.ResolveFieldAndNumeric(call, schema, Name);
        if (binWidth <= 0 || double.IsNaN(binWidth) || double.IsInfinity(binWidth))
            throw new InvalidOperationException(
                $"histogram(...) 第二个参数 bin_width 必须为正有限数，实际 {binWidth}。");
        return new HistogramAccumulator(binWidth);
    }
}

internal sealed class HistogramAccumulator : IAggregateAccumulator
{
    private readonly double _binWidth;
    private readonly Dictionary<long, long> _bins = new();

    public HistogramAccumulator(double binWidth) => _binWidth = binWidth;

    public long Count { get; private set; }

    public void Add(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;
        Count++;
        long binIndex = (long)Math.Floor(value / _binWidth);
        ref long slot = ref System.Runtime.InteropServices.CollectionsMarshal
            .GetValueRefOrAddDefault(_bins, binIndex, out _);
        slot++;
    }

    public void Merge(IAggregateAccumulator other)
    {
        if (other is not HistogramAccumulator h)
            throw new ArgumentException($"Cannot merge {other.GetType().Name} into HistogramAccumulator.", nameof(other));
        if (h._binWidth != _binWidth)
            throw new ArgumentException("Cannot merge histograms with different bin widths.", nameof(other));
        foreach (var (k, v) in h._bins)
        {
            ref long slot = ref System.Runtime.InteropServices.CollectionsMarshal
                .GetValueRefOrAddDefault(_bins, k, out _);
            slot += v;
        }
        Count += h.Count;
    }

    public object? Finalize()
    {
        if (Count == 0) return null;
        var sb = new System.Text.StringBuilder();
        sb.Append('{');
        bool first = true;
        foreach (var (k, v) in _bins.OrderBy(static kv => kv.Key))
        {
            if (!first) sb.Append(',');
            first = false;
            double lo = k * _binWidth;
            double hi = lo + _binWidth;
            sb.Append("\"[")
              .Append(lo.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
              .Append(',')
              .Append(hi.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
              .Append(")\":")
              .Append(v);
        }
        sb.Append('}');
        return sb.ToString();
    }
}
