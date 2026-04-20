using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TSLite.Data.Internal;

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
        // PR #44 将实现：分发到 POST /v1/db/{db}/measurements/{m}/{lp|json|bulk}。
        throw new NotSupportedException(
            "远程连接的 CommandType.TableDirect 批量入库快路径尚未实现，将在 PR #44 中加入。");
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
