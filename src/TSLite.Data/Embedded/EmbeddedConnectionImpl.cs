using System.Data;
using System.Data.Common;
using TSLite.Data.Internal;
using TSLite.Engine;
using TSLite.Ingest;
using TSLite.Sql;
using TSLite.Sql.Ast;
using TSLite.Sql.Execution;

namespace TSLite.Data.Embedded;

/// <summary>
/// 嵌入式连接实现：直接打开本地目录上的 <see cref="Tsdb"/>，并在进程内共享。
/// </summary>
internal sealed class EmbeddedConnectionImpl : IConnectionImpl
{
    private readonly TsdbConnectionStringBuilder _builder;
    private Tsdb? _tsdb;
    private ConnectionState _state = ConnectionState.Closed;

    public EmbeddedConnectionImpl(TsdbConnectionStringBuilder builder)
    {
        _builder = builder;
    }

    public string DataSource => NormalizeDataSource(_builder.DataSource);

    public string Database => DataSource;

    public string ServerVersion => typeof(Tsdb).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    public ConnectionState State => _state;

    internal Tsdb? Tsdb => _tsdb;

    public void Open()
    {
        if (_state == ConnectionState.Open) return;
        var path = NormalizeDataSource(_builder.DataSource);
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("ConnectionString 缺少 'Data Source'。");

        _tsdb = SharedTsdbRegistry.Acquire(new TsdbOptions { RootDirectory = path });
        _state = ConnectionState.Open;
    }

    public void Close()
    {
        if (_state == ConnectionState.Closed) return;
        var t = _tsdb;
        _tsdb = null;
        _state = ConnectionState.Closed;
        if (t != null)
            SharedTsdbRegistry.Release(t);
    }

    public void Dispose() => Close();

    public IExecutionResult Execute(string sql, TsdbParameterCollection parameters, CommandBehavior behavior)
    {
        if (_tsdb is null || _state != ConnectionState.Open)
            throw new InvalidOperationException("连接未打开。");

        var statement = SqlParser.Parse(sql);
        return statement switch
        {
            InsertStatement ins => MaterializedExecutionResult.NonQuery(SqlExecutor.ExecuteInsert(_tsdb, ins).RowsInserted),
            DeleteStatement del => MaterializedExecutionResult.NonQuery(SqlExecutor.ExecuteDelete(_tsdb, del).TombstonesAdded),
            CreateMeasurementStatement create => ExecuteCreate(_tsdb, create),
            SelectStatement sel => MaterializedExecutionResult.FromSelect(SqlExecutor.ExecuteSelect(_tsdb, sel)),
            _ => throw new NotSupportedException(
                $"语句类型 '{statement.GetType().Name}' 暂不支持。"),
        };
    }

    private static IExecutionResult ExecuteCreate(Tsdb tsdb, CreateMeasurementStatement create)
    {
        SqlExecutor.ExecuteCreateMeasurement(tsdb, create);
        return MaterializedExecutionResult.NonQuery(0);
    }

    public IExecutionResult ExecuteBulk(string commandText, TsdbParameterCollection parameters)
    {
        if (_tsdb is null || _state != ConnectionState.Open)
            throw new InvalidOperationException("连接未打开。");
        ArgumentNullException.ThrowIfNull(commandText);

        // 参数：measurement / onerror / flush
        string? measurementOverride = TryGetParam(parameters, "measurement");
        bool flushOnComplete = string.Equals(TryGetParam(parameters, "flush"), "true", StringComparison.OrdinalIgnoreCase);
        var errorPolicy = string.Equals(TryGetParam(parameters, "onerror"), "skip", StringComparison.OrdinalIgnoreCase)
            ? BulkErrorPolicy.Skip
            : BulkErrorPolicy.FailFast;

        // 1) 嗅探格式 + 切首行 measurement 前缀
        var format = BulkPayloadDetector.DetectWithPrefix(commandText, out var measurementFromPrefix, out var payload);
        var measurement = measurementOverride ?? measurementFromPrefix;

        // 2) 构造 reader
        IPointReader reader = format switch
        {
            BulkPayloadFormat.LineProtocol => new LineProtocolReader(payload, measurementOverride: measurement),
            BulkPayloadFormat.Json => new JsonPointsReader(payload, measurementOverride: measurement),
            BulkPayloadFormat.BulkValues => CreateBulkValuesReader(_tsdb, payload.ToString(), measurement),
            _ => throw new BulkIngestException($"未知协议格式 {format}。"),
        };

        try
        {
            var result = BulkIngestor.Ingest(_tsdb, reader, errorPolicy, flushOnComplete);
            return MaterializedExecutionResult.NonQuery(result.Written);
        }
        finally
        {
            (reader as IDisposable)?.Dispose();
        }
    }

    private static BulkValuesReader CreateBulkValuesReader(Tsdb tsdb, string sql, string? measurementOverride)
    {
        // 列角色 resolver：先按需求解析的 measurement 的 schema 决定（measurement 由 reader 自身解析后回填）。
        // 这里采用闭包：第一次访问列时按 sql 的 measurement 解析 schema；override 优先。
        Catalog.MeasurementSchema? cachedSchema = null;
        var resolver = (string col) =>
        {
            if (string.Equals(col, "time", StringComparison.OrdinalIgnoreCase))
                return BulkValuesColumnRole.Time;

            if (cachedSchema is null)
            {
                // BulkValuesReader 在 ctor 内已解析 measurement，但此 resolver 在 ctor 内被调用，
                // 故我们按 measurementOverride（若有）或 sql 中显式 measurement 名查找。
                var name = measurementOverride ?? PeekMeasurementName(sql);
                cachedSchema = tsdb.Measurements.TryGet(name)
                    ?? throw new BulkIngestException($"Bulk INSERT: measurement '{name}' 不存在；请先 CREATE MEASUREMENT。");
            }

            var column = cachedSchema.TryGetColumn(col)
                ?? throw new BulkIngestException($"Bulk INSERT: measurement '{cachedSchema.Name}' 没有列 '{col}'。");
            return column.Role == Catalog.MeasurementColumnRole.Tag
                ? BulkValuesColumnRole.Tag
                : BulkValuesColumnRole.Field;
        };

        return new BulkValuesReader(sql, resolver, measurementOverride);
    }

    private static string PeekMeasurementName(string sql)
    {
        // 轻量 peek：跳过 INSERT INTO，读一个标识符（必要时支持 "..." / `...`）。
        var span = sql.AsSpan();
        int i = 0;
        SkipWhitespace(span, ref i);
        ConsumeKeyword(span, ref i, "INSERT");
        SkipWhitespace(span, ref i);
        ConsumeKeyword(span, ref i, "INTO");
        SkipWhitespace(span, ref i);
        if (i >= span.Length) throw new BulkIngestException("Bulk INSERT: 期望 measurement 名。");
        char c = span[i];
        if (c == '"' || c == '`')
        {
            int s = ++i;
            while (i < span.Length && span[i] != c) i++;
            return new string(span[s..i]);
        }
        int start = i;
        while (i < span.Length && (char.IsLetterOrDigit(span[i]) || span[i] == '_')) i++;
        if (i == start) throw new BulkIngestException("Bulk INSERT: 无法读取 measurement 名。");
        return new string(span[start..i]);
    }

    private static void SkipWhitespace(ReadOnlySpan<char> span, ref int i)
    {
        while (i < span.Length && (span[i] == ' ' || span[i] == '\t' || span[i] == '\r' || span[i] == '\n')) i++;
    }

    private static void ConsumeKeyword(ReadOnlySpan<char> span, ref int i, string kw)
    {
        if (i + kw.Length > span.Length)
            throw new BulkIngestException($"Bulk INSERT: 期望关键字 '{kw}'。");
        for (int k = 0; k < kw.Length; k++)
        {
            if (char.ToUpperInvariant(span[i + k]) != kw[k])
                throw new BulkIngestException($"Bulk INSERT: 期望关键字 '{kw}'。");
        }
        i += kw.Length;
    }

    private static string? TryGetParam(TsdbParameterCollection parameters, string name)
    {
        for (int i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            if (string.Equals(p.ParameterName?.TrimStart('@', ':'), name, StringComparison.OrdinalIgnoreCase))
                return p.Value?.ToString();
        }
        return null;
    }

    /// <summary>
    /// 兼容 <c>tslite://path</c> 形式：去掉 scheme 前缀，得到真实文件系统路径。
    /// </summary>
    private static string NormalizeDataSource(string ds)
    {
        if (string.IsNullOrWhiteSpace(ds)) return ds;
        const string prefix = "tslite://";
        if (ds.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return ds[prefix.Length..];
        return ds;
    }
}
