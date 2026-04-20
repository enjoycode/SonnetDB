using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using TSLite.Sql.Execution;

namespace TSLite.Ado;

/// <summary>
/// 基于 <see cref="SelectExecutionResult"/> 内存物化结果的 ADO.NET 数据读取器。
/// </summary>
/// <remarks>
/// <para>
/// 当前实现把 <c>SELECT</c> 结果完整保留在内存中（语义上等同 v1 执行器），不做行级懒加载；
/// 对于非 SELECT 语句，也用一个空结果集附带 <see cref="RecordsAffected"/>。
/// </para>
/// <para>
/// 支持的 <see cref="GetFieldType"/> 推断：以列上首个非 null 行的运行时类型为准；全空时回落到 <see cref="object"/>。
/// </para>
/// </remarks>
public sealed class TsdbDataReader : DbDataReader
{
    private readonly SelectExecutionResult _result;
    private readonly int _recordsAffected;
    private readonly CommandBehavior _behavior;
    private readonly TsdbConnection? _connection;
    private readonly ColumnTypeKind[] _columnTypes;
    private int _rowIndex = -1;
    private bool _closed;

    internal TsdbDataReader(
        SelectExecutionResult result,
        int recordsAffected,
        CommandBehavior behavior,
        TsdbConnection? connection)
    {
        _result = result;
        _recordsAffected = recordsAffected;
        _behavior = behavior;
        _connection = connection;
        _columnTypes = InferColumnTypes(result);
    }

    private static ColumnTypeKind[] InferColumnTypes(SelectExecutionResult result)
    {
        var types = new ColumnTypeKind[result.Columns.Count];
        for (int c = 0; c < result.Columns.Count; c++)
        {
            ColumnTypeKind kind = ColumnTypeKind.Object;
            for (int r = 0; r < result.Rows.Count; r++)
            {
                var v = result.Rows[r][c];
                if (v is null) continue;
                kind = ClassifyValue(v);
                break;
            }
            types[c] = kind;
        }
        return types;
    }

    private static ColumnTypeKind ClassifyValue(object value) => value switch
    {
        bool => ColumnTypeKind.Boolean,
        byte => ColumnTypeKind.Byte,
        sbyte => ColumnTypeKind.SByte,
        short => ColumnTypeKind.Int16,
        ushort => ColumnTypeKind.UInt16,
        int => ColumnTypeKind.Int32,
        uint => ColumnTypeKind.UInt32,
        long => ColumnTypeKind.Int64,
        ulong => ColumnTypeKind.UInt64,
        float => ColumnTypeKind.Single,
        double => ColumnTypeKind.Double,
        decimal => ColumnTypeKind.Decimal,
        string => ColumnTypeKind.String,
        DateTime => ColumnTypeKind.DateTime,
        DateTimeOffset => ColumnTypeKind.DateTimeOffset,
        TimeSpan => ColumnTypeKind.TimeSpan,
        Guid => ColumnTypeKind.Guid,
        byte[] => ColumnTypeKind.Bytes,
        _ => ColumnTypeKind.Object,
    };

    /// <summary>
    /// 列运行时类型枚举。改为枚举可以让 <see cref="GetFieldType"/> 用 <c>switch</c>
    /// 返回 <c>typeof(...)</c> 常量，从而满足 IL 分析器对
    /// <see cref="DynamicallyAccessedMembersAttribute"/> 的静态可证明性要求。
    /// </summary>
    private enum ColumnTypeKind : byte
    {
        Object = 0,
        Boolean,
        Byte,
        SByte,
        Int16,
        UInt16,
        Int32,
        UInt32,
        Int64,
        UInt64,
        Single,
        Double,
        Decimal,
        String,
        DateTime,
        DateTimeOffset,
        TimeSpan,
        Guid,
        Bytes,
    }

    /// <inheritdoc />
    public override object this[int ordinal] => GetValue(ordinal);

    /// <inheritdoc />
    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <inheritdoc />
    public override int Depth => 0;

    /// <inheritdoc />
    public override int FieldCount => _result.Columns.Count;

    /// <inheritdoc />
    public override bool HasRows => _result.Rows.Count > 0;

    /// <inheritdoc />
    public override bool IsClosed => _closed;

    /// <inheritdoc />
    public override int RecordsAffected => _recordsAffected;

    /// <inheritdoc />
    public override bool GetBoolean(int ordinal) => Convert.ToBoolean(GetValue(ordinal), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override byte GetByte(int ordinal) => Convert.ToByte(GetValue(ordinal), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        => throw new NotSupportedException("TSLite 不支持二进制列。");

    /// <inheritdoc />
    public override char GetChar(int ordinal) => Convert.ToChar(GetValue(ordinal), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        => throw new NotSupportedException("TSLite 不支持字符流读取。");

    /// <inheritdoc />
    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

    /// <inheritdoc />
    public override DateTime GetDateTime(int ordinal)
    {
        var v = GetValue(ordinal);
        return v switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.UtcDateTime,
            long ms => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime,
            _ => Convert.ToDateTime(v, CultureInfo.InvariantCulture),
        };
    }

    /// <inheritdoc />
    public override decimal GetDecimal(int ordinal) => Convert.ToDecimal(GetValue(ordinal), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override double GetDouble(int ordinal) => Convert.ToDouble(GetValue(ordinal), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override IEnumerator GetEnumerator() => new DbEnumerator(this);

    /// <inheritdoc />
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    public override Type GetFieldType(int ordinal)
    {
        ValidateOrdinal(ordinal);
        return _columnTypes[ordinal] switch
        {
            ColumnTypeKind.Boolean => typeof(bool),
            ColumnTypeKind.Byte => typeof(byte),
            ColumnTypeKind.SByte => typeof(sbyte),
            ColumnTypeKind.Int16 => typeof(short),
            ColumnTypeKind.UInt16 => typeof(ushort),
            ColumnTypeKind.Int32 => typeof(int),
            ColumnTypeKind.UInt32 => typeof(uint),
            ColumnTypeKind.Int64 => typeof(long),
            ColumnTypeKind.UInt64 => typeof(ulong),
            ColumnTypeKind.Single => typeof(float),
            ColumnTypeKind.Double => typeof(double),
            ColumnTypeKind.Decimal => typeof(decimal),
            ColumnTypeKind.String => typeof(string),
            ColumnTypeKind.DateTime => typeof(DateTime),
            ColumnTypeKind.DateTimeOffset => typeof(DateTimeOffset),
            ColumnTypeKind.TimeSpan => typeof(TimeSpan),
            ColumnTypeKind.Guid => typeof(Guid),
            ColumnTypeKind.Bytes => typeof(byte[]),
            _ => typeof(object),
        };
    }

    /// <inheritdoc />
    public override float GetFloat(int ordinal) => Convert.ToSingle(GetValue(ordinal), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);

    /// <inheritdoc />
    public override short GetInt16(int ordinal) => Convert.ToInt16(GetValue(ordinal), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override int GetInt32(int ordinal) => Convert.ToInt32(GetValue(ordinal), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override long GetInt64(int ordinal) => Convert.ToInt64(GetValue(ordinal), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override string GetName(int ordinal)
    {
        ValidateOrdinal(ordinal);
        return _result.Columns[ordinal];
    }

    /// <inheritdoc />
    public override int GetOrdinal(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        for (int i = 0; i < _result.Columns.Count; i++)
            if (string.Equals(_result.Columns[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        throw new IndexOutOfRangeException($"未找到列 '{name}'。");
    }

    /// <inheritdoc />
    public override string GetString(int ordinal)
    {
        var v = GetValue(ordinal);
        return v switch
        {
            string s => s,
            null => throw new InvalidCastException($"列 {ordinal} 的值为 NULL。"),
            _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    /// <inheritdoc />
    public override object GetValue(int ordinal)
    {
        EnsureOnRow();
        ValidateOrdinal(ordinal);
        return _result.Rows[_rowIndex][ordinal] ?? DBNull.Value;
    }

    /// <inheritdoc />
    public override int GetValues(object[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        EnsureOnRow();
        int n = Math.Min(values.Length, _result.Columns.Count);
        for (int i = 0; i < n; i++)
            values[i] = _result.Rows[_rowIndex][i] ?? DBNull.Value;
        return n;
    }

    /// <inheritdoc />
    public override bool IsDBNull(int ordinal)
    {
        EnsureOnRow();
        ValidateOrdinal(ordinal);
        return _result.Rows[_rowIndex][ordinal] is null;
    }

    /// <inheritdoc />
    public override bool NextResult() => false;

    /// <inheritdoc />
    public override bool Read()
    {
        if (_closed) return false;
        if (_rowIndex + 1 >= _result.Rows.Count)
            return false;
        _rowIndex++;
        return true;
    }

    /// <inheritdoc />
    public override void Close()
    {
        if (_closed) return;
        _closed = true;
        if ((_behavior & CommandBehavior.CloseConnection) != 0)
            _connection?.Close();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing) Close();
        base.Dispose(disposing);
    }

    private void EnsureOnRow()
    {
        if (_closed) throw new InvalidOperationException("Reader 已关闭。");
        if (_rowIndex < 0) throw new InvalidOperationException("尚未调用 Read()。");
        if (_rowIndex >= _result.Rows.Count) throw new InvalidOperationException("已超出末行。");
    }

    private void ValidateOrdinal(int ordinal)
    {
        if (ordinal < 0 || ordinal >= _result.Columns.Count)
            throw new IndexOutOfRangeException($"列序号 {ordinal} 越界（列数 {_result.Columns.Count}）。");
    }
}
