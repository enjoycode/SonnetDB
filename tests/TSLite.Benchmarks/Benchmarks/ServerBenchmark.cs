using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using TSLite.Benchmarks.Helpers;

namespace TSLite.Benchmarks.Benchmarks;

// ── 服务器基准专用内部 DTO ───────────────────────────────────────────────────

internal sealed class ServerSqlRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("sql")]
    public string Sql { get; set; } = string.Empty;
}

internal sealed class ServerBatchRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("statements")]
    public List<ServerSqlRequest> Statements { get; set; } = [];
}

// ── TSLite Server INSERT 基准 ────────────────────────────────────────────────

/// <summary>
/// TSLite.Server 模式写入 1,000,000 条（HTTP Batch API）性能基准。
/// 需要 tslite-server 容器运行在 http://localhost:5080（见 docker/docker-compose.yml）。
/// </summary>
[Config(typeof(ServerInsertConfig))]
[MemoryDiagnoser]
[BenchmarkCategory("Server")]
public class ServerInsertBenchmark
{
    private const int DataPointCount = 1_000_000;
    private const string ServerUrl = "http://localhost:5080";
    private const string AdminToken = "bench-admin-token";
    private const string DbName = "bench_server_insert";
    private const int BatchSize = 2_000;

    private BenchmarkDataPoint[] _dataPoints = [];
    private HttpClient? _http;
    private bool _serverAvailable;

    /// <summary>全局初始化：检查服务可用性，创建数据库与 Measurement。</summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _dataPoints = DataGenerator.Generate(DataPointCount);

        _http = new HttpClient { BaseAddress = new Uri(ServerUrl) };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminToken);
        _http.Timeout = TimeSpan.FromMinutes(10);

        try
        {
            var pong = await _http.GetAsync("/healthz").ConfigureAwait(false);
            if (!pong.IsSuccessStatusCode)
            {
                _serverAvailable = false;
                Console.Error.WriteLine("[SKIP] TSLite.Server 健康检查失败。");
                return;
            }

            // 创建数据库（幂等）
            await PostJsonAsync("/v1/db", $"{{\"name\":\"{DbName}\"}}").ConfigureAwait(false);

            // 创建 Measurement（幂等）
            await PostSqlAsync(DbName,
                "CREATE MEASUREMENT sensor_data (host TAG, value FIELD FLOAT)")
                .ConfigureAwait(false);

            _serverAvailable = true;
        }
        catch (Exception ex)
        {
            _serverAvailable = false;
            Console.Error.WriteLine(
                $"[SKIP] TSLite.Server 不可用（{ex.Message}）。" +
                "请先执行: docker compose -f tests/TSLite.Benchmarks/docker/docker-compose.yml up -d tslite-server");
        }
    }

    /// <summary>每次迭代前删除并重新创建数据库，确保每轮从空库开始。</summary>
    [IterationSetup]
    public void IterationSetup()
    {
        if (!_serverAvailable) return;

        _http!.DeleteAsync($"/v1/db/{DbName}").GetAwaiter().GetResult();
        PostJsonAsync("/v1/db", $"{{\"name\":\"{DbName}\"}}").GetAwaiter().GetResult();
        PostSqlAsync(DbName,
            "CREATE MEASUREMENT sensor_data (host TAG, value FIELD FLOAT)")
            .GetAwaiter().GetResult();
    }

    /// <summary>
    /// TSLite.Server 写入 100 万条（HTTP Batch API，每批 2000 条）。
    /// </summary>
    [Benchmark(Description = "TSLite Server 写入 100万条")]
    public async Task TSLiteServer_Insert_1M()
    {
        if (!_serverAvailable)
        {
            Console.Error.WriteLine("[SKIP] TSLite.Server 不可用");
            return;
        }

        for (int offset = 0; offset < _dataPoints.Length; offset += BatchSize)
        {
            int end = Math.Min(offset + BatchSize, _dataPoints.Length);
            var stmts = new List<ServerSqlRequest>(end - offset);
            for (int i = offset; i < end; i++)
            {
                var dp = _dataPoints[i];
                stmts.Add(new ServerSqlRequest
                {
                    Sql = string.Format(CultureInfo.InvariantCulture,
                        "INSERT INTO sensor_data(host, value, time) VALUES ('server001', {0}, {1})",
                        dp.Value, dp.Timestamp)
                });
            }

            var batch = new ServerBatchRequest { Statements = stmts };
            var json = JsonSerializer.Serialize(batch);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _http!.PostAsync(
                $"/v1/db/{DbName}/sql/batch", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
        }
    }

    /// <summary>全局清理。</summary>
    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_serverAvailable)
        {
            try
            {
                await _http!.DeleteAsync($"/v1/db/{DbName}").ConfigureAwait(false);
            }
            catch (Exception ex) { Console.Error.WriteLine($"[WARN] 清理失败（不影响结果）: {ex.Message}"); }
        }

        _http?.Dispose();
    }

    private async Task PostJsonAsync(string path, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http!.PostAsync(path, content).ConfigureAwait(false);
        // 200/201 均为正常（已存在或新建）；4xx 如 409 Conflict 也属幂等成功；
        // 仅 5xx 代表服务器内部故障，需要传播失败
        if ((int)resp.StatusCode >= 500)
            resp.EnsureSuccessStatusCode();
    }

    private async Task PostSqlAsync(string db, string sql)
    {
        var req = new ServerSqlRequest { Sql = sql };
        var json = JsonSerializer.Serialize(req);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http!.PostAsync(
            $"/v1/db/{Uri.EscapeDataString(db)}/sql", content).ConfigureAwait(false);
        // 幂等 DDL（如 CREATE MEASUREMENT）若已存在会返回 4xx sql_error；
        // 仅 5xx 代表服务器内部故障，需要传播失败
        if ((int)resp.StatusCode >= 500)
            resp.EnsureSuccessStatusCode();
    }

    private sealed class ServerInsertConfig : ManualConfig
    {
        public ServerInsertConfig()
        {
            AddJob(Job.Default
                .WithStrategy(RunStrategy.Monitoring)
                .WithWarmupCount(0)
                .WithIterationCount(3));
        }
    }
}

// ── TSLite Server QUERY 基准 ─────────────────────────────────────────────────

/// <summary>
/// TSLite.Server 模式范围查询性能基准：预先写入 100 万条，查询最后 10%。
/// 需要 tslite-server 容器运行在 http://localhost:5080。
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Server")]
public class ServerQueryBenchmark
{
    private const int DataPointCount = 1_000_000;
    private const string ServerUrl = "http://localhost:5080";
    private const string AdminToken = "bench-admin-token";
    private const string DbName = "bench_server_query";
    private const int BatchSize = 2_000;

    private long _queryFromMs;
    private long _queryToMs;
    private HttpClient? _http;
    private bool _serverAvailable;

    /// <summary>全局初始化：写入 100 万条测试数据。</summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        var points = DataGenerator.Generate(DataPointCount);
        _queryFromMs = DataGenerator.QueryFromMs(DataPointCount);
        _queryToMs = DataGenerator.QueryToMs(DataPointCount);

        _http = new HttpClient { BaseAddress = new Uri(ServerUrl) };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminToken);
        _http.Timeout = TimeSpan.FromMinutes(20);

        try
        {
            var pong = await _http.GetAsync("/healthz").ConfigureAwait(false);
            if (!pong.IsSuccessStatusCode)
            {
                _serverAvailable = false;
                Console.Error.WriteLine("[SKIP] TSLite.Server 不可用");
                return;
            }

            await PostJsonAsync("/v1/db", $"{{\"name\":\"{DbName}\"}}").ConfigureAwait(false);
            await PostSqlAsync(DbName,
                "CREATE MEASUREMENT sensor_data (host TAG, value FIELD FLOAT)")
                .ConfigureAwait(false);

            // 写入 100 万条数据
            for (int offset = 0; offset < points.Length; offset += BatchSize)
            {
                int end = Math.Min(offset + BatchSize, points.Length);
                var stmts = new List<ServerSqlRequest>(end - offset);
                for (int i = offset; i < end; i++)
                {
                    var dp = points[i];
                    stmts.Add(new ServerSqlRequest
                    {
                        Sql = string.Format(CultureInfo.InvariantCulture,
                            "INSERT INTO sensor_data(host, value, time) VALUES ('server001', {0}, {1})",
                            dp.Value, dp.Timestamp)
                    });
                }

                var batch = new ServerBatchRequest { Statements = stmts };
                var json = JsonSerializer.Serialize(batch);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync(
                    $"/v1/db/{DbName}/sql/batch", content).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
            }

            _serverAvailable = true;
        }
        catch (Exception ex)
        {
            _serverAvailable = false;
            Console.Error.WriteLine(
                $"[SKIP] TSLite.Server 不可用（{ex.Message}）。" +
                "请先执行: docker compose -f tests/TSLite.Benchmarks/docker/docker-compose.yml up -d tslite-server");
        }
    }

    /// <summary>TSLite.Server 范围查询（HTTP SQL，约 100,000 条）。</summary>
    [Benchmark(Description = "TSLite Server 范围查询")]
    public async Task<int> TSLiteServer_Query_Range()
    {
        if (!_serverAvailable)
        {
            Console.Error.WriteLine("[SKIP] TSLite.Server 不可用");
            return -1;
        }

        var fromStr = _queryFromMs.ToString(CultureInfo.InvariantCulture);
        var toStr = _queryToMs.ToString(CultureInfo.InvariantCulture);
        var sql = $"SELECT time, value FROM sensor_data " +
                  $"WHERE host = 'server001' AND time >= {fromStr} AND time < {toStr}";

        var req = new ServerSqlRequest { Sql = sql };
        var json = JsonSerializer.Serialize(req);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http!.PostAsync(
            $"/v1/db/{DbName}/sql", content).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        // 消费 ndjson 响应体（流式读取，统计行数）
        await using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        int rowCount = 0;
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (line.StartsWith("[", StringComparison.Ordinal))
                rowCount++;
        }

        return rowCount;
    }

    /// <summary>全局清理。</summary>
    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_serverAvailable)
        {
            try
            {
                await _http!.DeleteAsync($"/v1/db/{DbName}").ConfigureAwait(false);
            }
            catch (Exception ex) { Console.Error.WriteLine($"[WARN] 清理失败（不影响结果）: {ex.Message}"); }
        }

        _http?.Dispose();
    }

    private async Task PostJsonAsync(string path, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http!.PostAsync(path, content).ConfigureAwait(false);
        // 200/201 均为正常（已存在或新建）；4xx 如 409 Conflict 也属幂等成功；
        // 仅 5xx 代表服务器内部故障，需要传播失败
        if ((int)resp.StatusCode >= 500)
            resp.EnsureSuccessStatusCode();
    }

    private async Task PostSqlAsync(string db, string sql)
    {
        var req = new ServerSqlRequest { Sql = sql };
        var json = JsonSerializer.Serialize(req);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http!.PostAsync(
            $"/v1/db/{Uri.EscapeDataString(db)}/sql", content).ConfigureAwait(false);
        // 幂等 DDL（如 CREATE MEASUREMENT）若已存在会返回 4xx sql_error；
        // 仅 5xx 代表服务器内部故障，需要传播失败
        if ((int)resp.StatusCode >= 500)
            resp.EnsureSuccessStatusCode();
    }
}

// ── TSLite Server AGGREGATE 基准 ─────────────────────────────────────────────

/// <summary>
/// TSLite.Server 模式聚合查询性能基准：预先写入 100 万条，按 1 分钟桶聚合。
/// 需要 tslite-server 容器运行在 http://localhost:5080。
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Server")]
public class ServerAggregateBenchmark
{
    private const int DataPointCount = 1_000_000;
    private const string ServerUrl = "http://localhost:5080";
    private const string AdminToken = "bench-admin-token";
    private const string DbName = "bench_server_aggregate";
    private const int BatchSize = 2_000;

    private HttpClient? _http;
    private bool _serverAvailable;

    /// <summary>全局初始化：写入 100 万条测试数据。</summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        var points = DataGenerator.Generate(DataPointCount);

        _http = new HttpClient { BaseAddress = new Uri(ServerUrl) };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminToken);
        _http.Timeout = TimeSpan.FromMinutes(20);

        try
        {
            var pong = await _http.GetAsync("/healthz").ConfigureAwait(false);
            if (!pong.IsSuccessStatusCode)
            {
                _serverAvailable = false;
                Console.Error.WriteLine("[SKIP] TSLite.Server 不可用");
                return;
            }

            await PostJsonAsync("/v1/db", $"{{\"name\":\"{DbName}\"}}").ConfigureAwait(false);
            await PostSqlAsync(DbName,
                "CREATE MEASUREMENT sensor_data (host TAG, value FIELD FLOAT)")
                .ConfigureAwait(false);

            for (int offset = 0; offset < points.Length; offset += BatchSize)
            {
                int end = Math.Min(offset + BatchSize, points.Length);
                var stmts = new List<ServerSqlRequest>(end - offset);
                for (int i = offset; i < end; i++)
                {
                    var dp = points[i];
                    stmts.Add(new ServerSqlRequest
                    {
                        Sql = string.Format(CultureInfo.InvariantCulture,
                            "INSERT INTO sensor_data(host, value, time) VALUES ('server001', {0}, {1})",
                            dp.Value, dp.Timestamp)
                    });
                }

                var batch = new ServerBatchRequest { Statements = stmts };
                var json = JsonSerializer.Serialize(batch);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync(
                    $"/v1/db/{DbName}/sql/batch", content).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
            }

            _serverAvailable = true;
        }
        catch (Exception ex)
        {
            _serverAvailable = false;
            Console.Error.WriteLine(
                $"[SKIP] TSLite.Server 不可用（{ex.Message}）。" +
                "请先执行: docker compose -f tests/TSLite.Benchmarks/docker/docker-compose.yml up -d tslite-server");
        }
    }

    /// <summary>TSLite.Server 1 分钟桶聚合（HTTP SQL，全量 100 万条）。</summary>
    [Benchmark(Description = "TSLite Server 1分钟聚合")]
    public async Task<int> TSLiteServer_Aggregate_1Min()
    {
        if (!_serverAvailable)
        {
            Console.Error.WriteLine("[SKIP] TSLite.Server 不可用");
            return -1;
        }

        var startMs = DataGenerator.StartTimestampMs.ToString(CultureInfo.InvariantCulture);
        var endMs = DataGenerator.QueryToMs(DataPointCount).ToString(CultureInfo.InvariantCulture);
        var sql = $"SELECT avg(value) FROM sensor_data " +
                  $"WHERE time >= {startMs} AND time < {endMs} " +
                  $"GROUP BY time(1m)";

        var req = new ServerSqlRequest { Sql = sql };
        var json = JsonSerializer.Serialize(req);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http!.PostAsync(
            $"/v1/db/{DbName}/sql", content).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        int rowCount = 0;
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (line.StartsWith("[", StringComparison.Ordinal))
                rowCount++;
        }

        return rowCount;
    }

    /// <summary>全局清理。</summary>
    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_serverAvailable)
        {
            try
            {
                await _http!.DeleteAsync($"/v1/db/{DbName}").ConfigureAwait(false);
            }
            catch (Exception ex) { Console.Error.WriteLine($"[WARN] 清理失败（不影响结果）: {ex.Message}"); }
        }

        _http?.Dispose();
    }

    private async Task PostJsonAsync(string path, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http!.PostAsync(path, content).ConfigureAwait(false);
        // 200/201 均为正常（已存在或新建）；4xx 如 409 Conflict 也属幂等成功；
        // 仅 5xx 代表服务器内部故障，需要传播失败
        if ((int)resp.StatusCode >= 500)
            resp.EnsureSuccessStatusCode();
    }

    private async Task PostSqlAsync(string db, string sql)
    {
        var req = new ServerSqlRequest { Sql = sql };
        var json = JsonSerializer.Serialize(req);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http!.PostAsync(
            $"/v1/db/{Uri.EscapeDataString(db)}/sql", content).ConfigureAwait(false);
        // 幂等 DDL（如 CREATE MEASUREMENT）若已存在会返回 4xx sql_error；
        // 仅 5xx 代表服务器内部故障，需要传播失败
        if ((int)resp.StatusCode >= 500)
            resp.EnsureSuccessStatusCode();
    }
}
