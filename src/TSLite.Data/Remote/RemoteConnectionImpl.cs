using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TSLite.Data.Embedded;
using TSLite.Data.Internal;
using TSLite.Ingest;

namespace TSLite.Data.Remote;

/// <summary>
/// 远程连接实现：通过 HTTP 调用 <c>TSLite.Server</c>。
/// </summary>
internal sealed class RemoteConnectionImpl : IConnectionImpl
{
    private readonly TsdbConnectionStringBuilder _builder;
    private HttpClient? _http;
    private string _baseUrl = string.Empty;
    private string _database = string.Empty;
    private ConnectionState _state = ConnectionState.Closed;

    public RemoteConnectionImpl(TsdbConnectionStringBuilder builder)
    {
        _builder = builder;
    }

    public string DataSource => _baseUrl;

    public string Database => _database;

    public string ServerVersion => _http is null ? "unknown" : "TSLite.Server";

    public ConnectionState State => _state;

    public void Open()
    {
        if (_state == ConnectionState.Open) return;

        var (baseUrl, dbFromUrl) = ParseEndpoint(_builder.DataSource);
        _baseUrl = baseUrl;
        _database = !string.IsNullOrWhiteSpace(_builder.Database) ? _builder.Database! : dbFromUrl;

        if (string.IsNullOrWhiteSpace(_database))
            throw new InvalidOperationException(
                "远程连接缺少数据库名：请在 Data Source URL 路径中提供（如 tslite+http://host/db），或显式设置 'Database='。");

        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(_builder.Timeout),
        };
        if (!string.IsNullOrWhiteSpace(_builder.Token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _builder.Token);

        _state = ConnectionState.Open;
    }

    public void Close()
    {
        if (_state == ConnectionState.Closed) return;
        _state = ConnectionState.Closed;
        _http?.Dispose();
        _http = null;
    }

    public void Dispose() => Close();

    public IExecutionResult Execute(string sql, TsdbParameterCollection parameters, CommandBehavior behavior)
    {
        if (_http is null || _state != ConnectionState.Open)
            throw new InvalidOperationException("连接未打开。");

        var url = $"v1/db/{Uri.EscapeDataString(_database)}/sql";
        var body = new SqlRequestBody { Sql = sql };
        var json = JsonSerializer.Serialize(body, RemoteJsonContext.Default.SqlRequestBody);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        // 使用 ResponseHeadersRead 以支持流式读取 ndjson 响应体
        var response = _http.Send(request, HttpCompletionOption.ResponseHeadersRead);
        try
        {
            if (!response.IsSuccessStatusCode)
                throw BuildHttpError(response);

            var stream = response.Content.ReadAsStream();
            // RemoteExecutionResult 拥有 stream 与 response 的所有权
            return RemoteExecutionResult.Create(response, stream);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static TsdbServerException BuildHttpError(HttpResponseMessage response)
    {
        string body = string.Empty;
        try
        {
            body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
        catch { /* ignore */ }

        string error = "http_error";
        string message = response.ReasonPhrase ?? response.StatusCode.ToString();
        if (!string.IsNullOrEmpty(body))
        {
            try
            {
                var err = JsonSerializer.Deserialize(body, RemoteJsonContext.Default.ServerErrorBody);
                if (err is not null && !string.IsNullOrEmpty(err.Error))
                {
                    error = err.Error;
                    message = err.Message;
                }
            }
            catch { /* 非 JSON 响应保留 raw body */ message = body; }
        }
        return new TsdbServerException(error, message, response.StatusCode);
    }

    public IExecutionResult ExecuteBulk(string commandText, TsdbParameterCollection parameters)
    {
        if (_http is null || _state != ConnectionState.Open)
            throw new InvalidOperationException("连接未打开。");
        ArgumentNullException.ThrowIfNull(commandText);

        // 1) 嗅探协议格式 + 切首行 measurement 前缀
        var format = BulkPayloadDetector.DetectWithPrefix(commandText, out var measurementFromPrefix, out var payload);

        // 2) measurement 优先级：参数 > 首行前缀 > 从 payload 内提取（JSON `m` / BulkValues `INSERT INTO <name>`）
        var measurement = TryGetParam(parameters, "measurement") ?? measurementFromPrefix;
        if (string.IsNullOrWhiteSpace(measurement))
        {
            measurement = format switch
            {
                BulkPayloadFormat.Json => TryPeekJsonMeasurement(payload.Span),
                BulkPayloadFormat.BulkValues => SafePeekBulkMeasurement(payload),
                _ => null,
            };
        }
        if (string.IsNullOrWhiteSpace(measurement))
            throw new InvalidOperationException(
                "远程批量入库必须指定 measurement：可在 payload 首行作为前缀（如 `cpu\\n...`）、" +
                "通过 cmd.Parameters[\"measurement\"] 提供，或在 JSON 中给出 `m` 字段、在 INSERT 中给出 `INTO <name>`。");

        // 3) 端点后缀
        string suffix = format switch
        {
            BulkPayloadFormat.LineProtocol => "lp",
            BulkPayloadFormat.Json => "json",
            BulkPayloadFormat.BulkValues => "bulk",
            _ => throw new InvalidOperationException($"未知协议格式 {format}。"),
        };

        // 4) query string：onerror / flush
        var url = new StringBuilder();
        url.Append("v1/db/").Append(Uri.EscapeDataString(_database))
           .Append("/measurements/").Append(Uri.EscapeDataString(measurement))
           .Append('/').Append(suffix);
        var qs = new List<string>();
        var onerror = TryGetParam(parameters, "onerror");
        if (!string.IsNullOrEmpty(onerror))
            qs.Add("onerror=" + Uri.EscapeDataString(onerror));
        var flush = TryGetParam(parameters, "flush");
        if (!string.IsNullOrEmpty(flush))
            qs.Add("flush=" + Uri.EscapeDataString(flush));
        if (qs.Count > 0)
            url.Append('?').Append(string.Join('&', qs));

        // 5) 构造请求体（payload 已通过 DetectWithPrefix 切掉首行）
        string contentType = format == BulkPayloadFormat.Json
            ? "application/json"
            : "text/plain";
        using var request = new HttpRequestMessage(HttpMethod.Post, url.ToString())
        {
            Content = new StringContent(payload.ToString(), Encoding.UTF8, contentType),
        };

        using var response = _http.Send(request, HttpCompletionOption.ResponseContentRead);
        if (!response.IsSuccessStatusCode)
            throw BuildHttpError(response);

        // 6) 解析响应 JSON
        var stream = response.Content.ReadAsStream();
        var body = JsonSerializer.Deserialize(stream, RemoteJsonContext.Default.BulkIngestResponseBody)
            ?? throw new TsdbServerException("bulk_ingest_error", "服务端响应体为空。", response.StatusCode);
        return MaterializedExecutionResult.NonQuery((int)body.WrittenRows);
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
    /// 极简扫描 JSON 文本，提取顶层 <c>"m"</c> 字段。仅在远程客户端用于决定 endpoint 路径段，
    /// 真正的 JSON 解析仍由服务端 <see cref="JsonPointsReader"/> 完成。
    /// </summary>
    private static string? TryPeekJsonMeasurement(ReadOnlySpan<char> json)
    {
        // 寻找 "m" : "<value>" — 仅匹配第一处。容错有限，主要服务于按规范构造的 payload。
        int i = 0;
        while (i < json.Length)
        {
            // 找下一个 "
            int q1 = IndexOf(json, '"', i);
            if (q1 < 0) return null;
            int q2 = IndexOf(json, '"', q1 + 1);
            if (q2 < 0) return null;
            var key = json.Slice(q1 + 1, q2 - q1 - 1);
            i = q2 + 1;
            if (!key.Equals("m", StringComparison.Ordinal)) continue;
            // 跳过空白与冒号
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t' || json[i] == '\r' || json[i] == '\n')) i++;
            if (i >= json.Length || json[i] != ':') continue;
            i++;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t' || json[i] == '\r' || json[i] == '\n')) i++;
            if (i >= json.Length || json[i] != '"') return null;
            int v1 = i + 1;
            int v2 = IndexOf(json, '"', v1);
            if (v2 < 0) return null;
            return new string(json.Slice(v1, v2 - v1));
        }
        return null;
    }

    private static int IndexOf(ReadOnlySpan<char> s, char c, int start)
    {
        for (int i = start; i < s.Length; i++)
            if (s[i] == c) return i;
        return -1;
    }

    private static string? SafePeekBulkMeasurement(ReadOnlyMemory<char> payload)
    {
        try { return SchemaBoundBulkValuesReader.PeekMeasurementName(payload.ToString()); }
        catch (BulkIngestException) { return null; }
    }

    /// <summary>
    /// 解析连接字符串中的 <c>Data Source</c>，返回 (baseUrl, databaseFromPath)。
    /// 支持 <c>tslite+http://host:port/dbname</c> / <c>http://host:port/dbname</c>。
    /// </summary>
    internal static (string BaseUrl, string DatabaseFromPath) ParseEndpoint(string dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource))
            throw new InvalidOperationException("远程连接缺少 'Data Source'。");

        var ds = dataSource.Trim();
        if (ds.StartsWith("tslite+http://", StringComparison.OrdinalIgnoreCase))
            ds = "http://" + ds["tslite+http://".Length..];
        else if (ds.StartsWith("tslite+https://", StringComparison.OrdinalIgnoreCase))
            ds = "https://" + ds["tslite+https://".Length..];

        if (!Uri.TryCreate(ds, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"远程 Data Source 不是合法 URL: {dataSource}");
        if (uri.Scheme != "http" && uri.Scheme != "https")
            throw new InvalidOperationException($"不支持的远程 scheme: {uri.Scheme}");

        var baseUrl = $"{uri.Scheme}://{uri.Authority}/";
        var path = uri.AbsolutePath.Trim('/');
        return (baseUrl, path);
    }
}

/// <summary>
/// 服务端返回非 2xx 响应时抛出的异常。
/// </summary>
public sealed class TsdbServerException : Exception
{
    /// <summary>构造一条服务端错误。</summary>
    public TsdbServerException(string error, string message, HttpStatusCode statusCode)
        : base($"[{(int)statusCode} {statusCode}] {error}: {message}")
    {
        Error = error;
        ServerMessage = message;
        StatusCode = statusCode;
    }

    /// <summary>服务端给出的错误标识，例如 <c>unauthorized</c> / <c>forbidden</c> / <c>db_not_found</c> / <c>sql_error</c>。</summary>
    public string Error { get; }

    /// <summary>服务端给出的人类可读消息。</summary>
    public string ServerMessage { get; }

    /// <summary>HTTP 状态码。</summary>
    public HttpStatusCode StatusCode { get; }
}
