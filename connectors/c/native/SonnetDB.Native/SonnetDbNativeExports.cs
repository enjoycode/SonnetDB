using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using SonnetDB.Catalog;
using SonnetDB.Engine;
using SonnetDB.Model;
using SonnetDB.Sql.Execution;

namespace SonnetDB.Native;

internal enum NativeValueType
{
    Null = 0,
    Int64 = 1,
    Float64 = 2,
    Boolean = 3,
    Text = 4,
}

internal sealed class NativeConnection : IDisposable
{
    private Tsdb? _tsdb;

    public NativeConnection(string dataSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSource);
        _tsdb = Tsdb.Open(new TsdbOptions { RootDirectory = NormalizeDataSource(dataSource) });
    }

    public NativeResult Execute(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        var tsdb = _tsdb ?? throw new ObjectDisposedException(nameof(NativeConnection));
        return NativeResult.From(SqlExecutor.Execute(tsdb, sql));
    }

    public void Flush()
    {
        var tsdb = _tsdb ?? throw new ObjectDisposedException(nameof(NativeConnection));
        tsdb.FlushNow();
    }

    public void Dispose()
    {
        var tsdb = _tsdb;
        _tsdb = null;
        tsdb?.Dispose();
    }

    private static string NormalizeDataSource(string dataSource)
    {
        const string prefix = "sonnetdb://";
        return dataSource.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? dataSource[prefix.Length..]
            : dataSource;
    }
}

internal sealed class NativeResult : IDisposable
{
    private readonly IReadOnlyList<string> _columns;
    private readonly IReadOnlyList<IReadOnlyList<object?>> _rows;
    private readonly Dictionary<int, IntPtr> _columnNamePointers = new();
    private readonly Dictionary<int, IntPtr> _valueTextPointers = new();
    private int _rowIndex = -1;
    private bool _disposed;

    private NativeResult(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        int recordsAffected)
    {
        _columns = columns;
        _rows = rows;
        RecordsAffected = recordsAffected;
    }

    public int RecordsAffected { get; }

    public int ColumnCount
    {
        get
        {
            ThrowIfDisposed();
            return _columns.Count;
        }
    }

    public static NativeResult From(object? result)
        => result switch
        {
            SelectExecutionResult select => new NativeResult(select.Columns, select.Rows, -1),
            InsertExecutionResult insert => NonQuery(insert.RowsInserted),
            DeleteExecutionResult delete => NonQuery(delete.TombstonesAdded),
            MeasurementSchema => NonQuery(0),
            int affected => NonQuery(affected),
            long affected => NonQuery(checked((int)affected)),
            null => NonQuery(0),
            _ => throw new NotSupportedException(
                $"SQL result type '{result.GetType().Name}' is not supported by the native C ABI."),
        };

    public IntPtr GetColumnName(int ordinal)
    {
        ThrowIfDisposed();
        ValidateColumnOrdinal(ordinal);

        if (_columnNamePointers.TryGetValue(ordinal, out var ptr))
            return ptr;

        ptr = Marshal.StringToCoTaskMemUTF8(_columns[ordinal]);
        _columnNamePointers.Add(ordinal, ptr);
        return ptr;
    }

    public int MoveNext()
    {
        ThrowIfDisposed();
        ReleaseValueTextPointers();

        if (_rowIndex + 1 >= _rows.Count)
            return 0;

        _rowIndex++;
        return 1;
    }

    public NativeValueType GetValueType(int ordinal)
    {
        var value = GetCurrentValue(ordinal);
        return value switch
        {
            null => NativeValueType.Null,
            byte or sbyte or short or ushort or int or uint or long => NativeValueType.Int64,
            ulong => NativeValueType.Int64,
            float or double or decimal => NativeValueType.Float64,
            bool => NativeValueType.Boolean,
            _ => NativeValueType.Text,
        };
    }

    public long GetInt64(int ordinal)
    {
        var value = GetCurrentValue(ordinal);
        return value switch
        {
            byte v => v,
            sbyte v => v,
            short v => v,
            ushort v => v,
            int v => v,
            uint v => v,
            long v => v,
            ulong v when v <= long.MaxValue => (long)v,
            _ => throw new InvalidOperationException($"Column {ordinal} is not an int64 value."),
        };
    }

    public double GetDouble(int ordinal)
    {
        var value = GetCurrentValue(ordinal);
        return value switch
        {
            byte v => v,
            sbyte v => v,
            short v => v,
            ushort v => v,
            int v => v,
            uint v => v,
            long v => v,
            ulong v => v,
            float v => v,
            double v => v,
            decimal v => (double)v,
            _ => throw new InvalidOperationException($"Column {ordinal} is not a double value."),
        };
    }

    public int GetBoolean(int ordinal)
    {
        var value = GetCurrentValue(ordinal);
        return value switch
        {
            bool v => v ? 1 : 0,
            _ => throw new InvalidOperationException($"Column {ordinal} is not a boolean value."),
        };
    }

    public IntPtr GetText(int ordinal)
    {
        var value = GetCurrentValue(ordinal);
        if (value is null)
            return IntPtr.Zero;

        if (_valueTextPointers.TryGetValue(ordinal, out var ptr))
            return ptr;

        ptr = Marshal.StringToCoTaskMemUTF8(FormatText(value));
        _valueTextPointers.Add(ordinal, ptr);
        return ptr;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        ReleaseValueTextPointers();
        foreach (var ptr in _columnNamePointers.Values)
            Marshal.FreeCoTaskMem(ptr);
        _columnNamePointers.Clear();
        _disposed = true;
    }

    private static NativeResult NonQuery(int recordsAffected)
        => new(Array.Empty<string>(), Array.Empty<IReadOnlyList<object?>>(), recordsAffected);

    private object? GetCurrentValue(int ordinal)
    {
        ThrowIfDisposed();
        ValidateColumnOrdinal(ordinal);
        if (_rowIndex < 0 || _rowIndex >= _rows.Count)
            throw new InvalidOperationException("Result is not positioned on a row.");
        return _rows[_rowIndex][ordinal];
    }

    private void ValidateColumnOrdinal(int ordinal)
    {
        if ((uint)ordinal >= (uint)_columns.Count)
            throw new ArgumentOutOfRangeException(nameof(ordinal), ordinal, "Column ordinal is out of range.");
    }

    private void ReleaseValueTextPointers()
    {
        foreach (var ptr in _valueTextPointers.Values)
            Marshal.FreeCoTaskMem(ptr);
        _valueTextPointers.Clear();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static string FormatText(object value)
        => value switch
        {
            string s => s,
            bool b => b ? "true" : "false",
            double d => d.ToString("G17", CultureInfo.InvariantCulture),
            float f => f.ToString("G9", CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            byte or sbyte or short or ushort or int or uint or long or ulong
                => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            GeoPoint geo => string.Create(
                CultureInfo.InvariantCulture,
                $"POINT({geo.Lat:G17},{geo.Lon:G17})"),
            float[] vector => FormatVector(vector),
            _ => value.ToString() ?? string.Empty,
        };

    private static string FormatVector(float[] vector)
    {
        var sb = new StringBuilder();
        sb.Append('[');
        for (int i = 0; i < vector.Length; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(vector[i].ToString("G9", CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }
}

internal static class SonnetDbNativeExports
{
    [ThreadStatic]
    private static string? s_lastError;

    [UnmanagedCallersOnly(EntryPoint = "sonnetdb_open", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static IntPtr Open(IntPtr dataSource)
    {
        try
        {
            ClearError();
            var path = ReadUtf8(dataSource, nameof(dataSource));
            var connection = new NativeConnection(path);
            return GCHandle.ToIntPtr(GCHandle.Alloc(connection));
        }
        catch (Exception ex)
        {
            SetError(ex);
            return IntPtr.Zero;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "sonnetdb_close", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void Close(IntPtr connection)
    {
        try
        {
            ClearError();
            if (connection == IntPtr.Zero)
                return;

            var handle = GCHandle.FromIntPtr(connection);
            if (handle.Target is IDisposable disposable)
                disposable.Dispose();
            handle.Free();
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "sonnetdb_execute", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static IntPtr Execute(IntPtr connection, IntPtr sql)
    {
        try
        {
            ClearError();
            var nativeConnection = GetTarget<NativeConnection>(connection, nameof(connection));
            var text = ReadUtf8(sql, nameof(sql));
            var result = nativeConnection.Execute(text);
            return GCHandle.ToIntPtr(GCHandle.Alloc(result));
        }
        catch (Exception ex)
        {
            SetError(ex);
            return IntPtr.Zero;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "sonnetdb_result_free", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void ResultFree(IntPtr result)
    {
        try
        {
            ClearError();
            if (result == IntPtr.Zero)
                return;

            var handle = GCHandle.FromIntPtr(result);
            if (handle.Target is IDisposable disposable)
                disposable.Dispose();
            handle.Free();
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "sonnetdb_result_records_affected", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int ResultRecordsAffected(IntPtr result)
        => Invoke(result, static r => r.RecordsAffected, -1);

    [UnmanagedCallersOnly(EntryPoint = "sonnetdb_result_column_count", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int ResultColumnCount(IntPtr result)
        => Invoke(result, static r => r.ColumnCount, -1);

    [UnmanagedCallersOnly(EntryPoint = "sonnetdb_result_column_name", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static IntPtr ResultColumnName(IntPtr result, int ordinal)
        => Invoke(result, r => r.GetColumnName(ordinal), IntPtr.Zero);

    [UnmanagedCallersOnly(EntryPoint = "sonnetdb_result_next", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int ResultNext(IntPtr result)
        => Invoke(result, static r => r.MoveNext(), -1);

    [UnmanagedCallersOnly(EntryPoint = "sonnetdb_result_value_type", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int ResultValueType(IntPtr result, int ordinal)
        => Invoke(result, r => (int)r.GetValueType(ordinal), -1);

    [UnmanagedCallersOnly(EntryPoint = "sonnetdb_result_value_int64", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static long ResultValueInt64(IntPtr result, int ordinal)
        => Invoke(result, r => r.GetInt64(ordinal), 0L);

    [UnmanagedCallersOnly(EntryPoint = "sonnetdb_result_value_double", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static double ResultValueDouble(IntPtr result, int ordinal)
        => Invoke(result, r => r.GetDouble(ordinal), 0d);

    [UnmanagedCallersOnly(EntryPoint = "sonnetdb_result_value_bool", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int ResultValueBool(IntPtr result, int ordinal)
        => Invoke(result, r => r.GetBoolean(ordinal), -1);

    [UnmanagedCallersOnly(EntryPoint = "sonnetdb_result_value_text", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static IntPtr ResultValueText(IntPtr result, int ordinal)
        => Invoke(result, r => r.GetText(ordinal), IntPtr.Zero);

    [UnmanagedCallersOnly(EntryPoint = "sonnetdb_flush", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int Flush(IntPtr connection)
    {
        try
        {
            ClearError();
            GetTarget<NativeConnection>(connection, nameof(connection)).Flush();
            return 0;
        }
        catch (Exception ex)
        {
            SetError(ex);
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "sonnetdb_version", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int Version(IntPtr buffer, int bufferLength)
    {
        try
        {
            ClearError();
            var version = typeof(Tsdb).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(Tsdb).Assembly.GetName().Version?.ToString()
                ?? "0.0.0";
            return CopyUtf8(version, buffer, bufferLength);
        }
        catch (Exception ex)
        {
            SetError(ex);
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "sonnetdb_last_error", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int LastError(IntPtr buffer, int bufferLength)
        => CopyUtf8(s_lastError ?? string.Empty, buffer, bufferLength);

    private static TReturn Invoke<TReturn>(IntPtr result, Func<NativeResult, TReturn> action, TReturn errorValue)
    {
        try
        {
            ClearError();
            var nativeResult = GetTarget<NativeResult>(result, nameof(result));
            return action(nativeResult);
        }
        catch (Exception ex)
        {
            SetError(ex);
            return errorValue;
        }
    }

    private static T GetTarget<T>(IntPtr handle, string parameterName)
        where T : class
    {
        if (handle == IntPtr.Zero)
            throw new ArgumentNullException(parameterName);

        var gcHandle = GCHandle.FromIntPtr(handle);
        return gcHandle.Target as T
            ?? throw new InvalidOperationException($"Native handle '{parameterName}' has an unexpected target type.");
    }

    private static string ReadUtf8(IntPtr pointer, string parameterName)
    {
        if (pointer == IntPtr.Zero)
            throw new ArgumentNullException(parameterName);

        return Marshal.PtrToStringUTF8(pointer)
            ?? throw new ArgumentException("UTF-8 string pointer is invalid.", parameterName);
    }

    private static int CopyUtf8(string value, IntPtr buffer, int bufferLength)
    {
        if (buffer == IntPtr.Zero || bufferLength <= 0)
            return Encoding.UTF8.GetByteCount(value);

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        int copyLength = Math.Min(bytes.Length, bufferLength - 1);
        if (copyLength > 0)
            Marshal.Copy(bytes, 0, buffer, copyLength);
        Marshal.WriteByte(buffer, copyLength, 0);
        return bytes.Length;
    }

    private static void ClearError()
    {
        s_lastError = null;
    }

    private static void SetError(Exception exception)
    {
        s_lastError = exception.Message;
    }
}
