using SonnetDB.Storage.Format;

namespace SonnetDB.Model;

/// <summary>
/// 字段值的统一表示，支持 Float64 / Int64 / Boolean / String 四种类型，
/// 通过显式 <see cref="FieldType"/> 标签区分，零装箱。
/// </summary>
/// <remarks>
/// 设计要点：使用一个 <c>long _numeric</c> 字段以位模式（bit pattern）同时承载
/// Double（<see cref="BitConverter.DoubleToInt64Bits"/>）、Long 和 Bool（0/1），
/// 避免装箱到 <c>object</c>。String 单独保存在 <c>_string</c> 字段中。
/// </remarks>
public readonly struct FieldValue : IEquatable<FieldValue>
{
    /// <summary>字段的实际类型标签。</summary>
    public FieldType Type { get; }

    /// <summary>存储 Float64 / Int64 / Boolean 的数值位模式。</summary>
    private readonly long _numeric;

    /// <summary>仅当 <see cref="Type"/> 为 <see cref="FieldType.String"/> 时有值。</summary>
    private readonly string? _string;

    private FieldValue(FieldType type, long numeric, string? str)
    {
        Type = type;
        _numeric = numeric;
        _string = str;
    }

    // ── 工厂方法 ────────────────────────────────────────────────────────────

    /// <summary>从 64 位双精度浮点数创建字段值。</summary>
    public static FieldValue FromDouble(double value)
        => new(FieldType.Float64, BitConverter.DoubleToInt64Bits(value), null);

    /// <summary>从 64 位有符号整数创建字段值。</summary>
    public static FieldValue FromLong(long value)
        => new(FieldType.Int64, value, null);

    /// <summary>从布尔值创建字段值。</summary>
    public static FieldValue FromBool(bool value)
        => new(FieldType.Boolean, value ? 1L : 0L, null);

    /// <summary>从字符串创建字段值。</summary>
    /// <param name="value">非空字符串。</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> 为 null。</exception>
    public static FieldValue FromString(string value)
        => new(FieldType.String, 0L, value ?? throw new ArgumentNullException(nameof(value)));

    // ── 取值方法 ────────────────────────────────────────────────────────────

    /// <summary>以 double 形式返回字段值。</summary>
    /// <exception cref="InvalidOperationException">字段类型不是 Float64。</exception>
    public double AsDouble() => Type == FieldType.Float64
        ? BitConverter.Int64BitsToDouble(_numeric)
        : throw new InvalidOperationException($"Field is {Type}, not Float64.");

    /// <summary>以 long 形式返回字段值。</summary>
    /// <exception cref="InvalidOperationException">字段类型不是 Int64。</exception>
    public long AsLong() => Type == FieldType.Int64
        ? _numeric
        : throw new InvalidOperationException($"Field is {Type}, not Int64.");

    /// <summary>以 bool 形式返回字段值。</summary>
    /// <exception cref="InvalidOperationException">字段类型不是 Boolean。</exception>
    public bool AsBool() => Type == FieldType.Boolean
        ? _numeric != 0
        : throw new InvalidOperationException($"Field is {Type}, not Boolean.");

    /// <summary>以 string 形式返回字段值。</summary>
    /// <exception cref="InvalidOperationException">字段类型不是 String。</exception>
    public string AsString() => Type == FieldType.String
        ? _string!
        : throw new InvalidOperationException($"Field is {Type}, not String.");

    // ── 辅助方法 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 尝试以 double 形式获取数值。
    /// Float64 直接转换，Int64 / Boolean 自动转换，String 返回 false。
    /// </summary>
    /// <param name="value">转换后的 double 值；失败时为 0。</param>
    /// <returns>转换成功返回 true，否则返回 false。</returns>
    public bool TryGetNumeric(out double value)
    {
        switch (Type)
        {
            case FieldType.Float64:
                value = BitConverter.Int64BitsToDouble(_numeric);
                return true;
            case FieldType.Int64:
                value = (double)_numeric;
                return true;
            case FieldType.Boolean:
                value = _numeric != 0 ? 1.0 : 0.0;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    // ── 相等性 ──────────────────────────────────────────────────────────────

    /// <summary>按类型和值比较两个 <see cref="FieldValue"/> 是否相等。</summary>
    public bool Equals(FieldValue other)
    {
        if (Type != other.Type)
            return false;
        return Type == FieldType.String
            ? string.Equals(_string, other._string, StringComparison.Ordinal)
            : _numeric == other._numeric;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is FieldValue v && Equals(v);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Type, _numeric, _string);

    /// <inheritdoc/>
    public override string ToString() => Type switch
    {
        FieldType.Float64 => BitConverter.Int64BitsToDouble(_numeric).ToString("G"),
        FieldType.Int64 => _numeric.ToString(),
        FieldType.Boolean => _numeric != 0 ? "true" : "false",
        FieldType.String => _string!,
        _ => $"Unknown({Type})",
    };

    /// <summary>相等运算符。</summary>
    public static bool operator ==(FieldValue l, FieldValue r) => l.Equals(r);

    /// <summary>不等运算符。</summary>
    public static bool operator !=(FieldValue l, FieldValue r) => !l.Equals(r);
}
