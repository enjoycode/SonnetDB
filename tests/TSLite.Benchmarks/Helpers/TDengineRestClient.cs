using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Globalization;

namespace TSLite.Benchmarks.Helpers;

/// <summary>
/// 通过 TDengine RESTful API（端口 6041）执行 SQL 的轻量级客户端。
/// 无需安装 TDengine 原生客户端库，通过 HTTP 与 TDengine 交互。
/// </summary>
public sealed class TDengineRestClient : IDisposable
{
    private readonly HttpClient _http;

    /// <summary>
    /// 创建 TDengine REST 客户端。
    /// </summary>
    /// <param name="baseUrl">TDengine REST API 地址，例如 http://localhost:6041。</param>
    /// <param name="username">用户名，默认 root。</param>
    /// <param name="password">密码，默认 taosdata。</param>
    public TDengineRestClient(string baseUrl, string username = "root", string password = "taosdata")
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
    }

    /// <summary>
    /// 执行 SQL 语句并返回原始 JSON 字符串。
    /// </summary>
    /// <param name="sql">要执行的 SQL 语句。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>TDengine REST 响应的 JSON 字符串。</returns>
    /// <exception cref="InvalidOperationException">当 TDengine 返回错误码时抛出。</exception>
    public async Task<string> ExecuteAsync(string sql, CancellationToken ct = default)
    {
        using var content = new StringContent(sql, Encoding.UTF8, "text/plain");
        using var response = await _http.PostAsync("/rest/sql", content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 0)
        {
            var desc = doc.RootElement.TryGetProperty("desc", out var d) ? d.GetString() : json;
            throw new InvalidOperationException($"TDengine error: {desc}");
        }

        return json;
    }

    /// <summary>
    /// 执行批量 INSERT（将数据拆分为多个 SQL 语句，每批 <paramref name="batchSize"/> 行）。
    /// </summary>
    /// <param name="table">目标子表名，例如 bench_tsdb.sd_server001。</param>
    /// <param name="points">数据点数组。</param>
    /// <param name="batchSize">每批行数，默认 1000。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task BulkInsertAsync(
        string table,
        BenchmarkDataPoint[] points,
        int batchSize = 1000,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder(batchSize * 30);
        for (int offset = 0; offset < points.Length; offset += batchSize)
        {
            int end = Math.Min(offset + batchSize, points.Length);
            sb.Clear();
            sb.Append("INSERT INTO ").Append(table).Append(" VALUES ");
            for (int i = offset; i < end; i++)
            {
                if (i > offset) sb.Append(',');
                sb.Append('(').Append(points[i].Timestamp).Append(',')
                  .Append(points[i].Value.ToString("G17", CultureInfo.InvariantCulture)).Append(')');
            }

            await ExecuteAsync(sb.ToString(), ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();
}
