using BenchmarkDotNet.Attributes;
using InfluxDB.Client;
using InfluxDB.Client.Writes;
using Microsoft.Data.Sqlite;
using System.Globalization;
using TSLite.Benchmarks.Helpers;

namespace TSLite.Benchmarks.Benchmarks;

/// <summary>
/// 聚合查询性能对比：TSLite（内存 LINQ 占位）、SQLite、InfluxDB、TDengine。
/// 预先写入 1,000,000 条数据，然后反复执行按 1 分钟桶的 AVG/MIN/MAX/COUNT 聚合。
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Aggregate")]
public class AggregateBenchmark
{
    // ── 配置 ──────────────────────────────────────────────────────────────
    private const int DataPointCount = 1_000_000;
    private const string InfluxUrl = "http://localhost:8086";
    private const string InfluxToken = "my-super-secret-auth-token";
    private const string InfluxOrg = "tslite";
    private const string InfluxBucket = "benchmarks";
    private const string TDengineUrl = "http://localhost:6041";
    private const string TDengineDb = "bench_aggregate";
    private const string TDengineTable = "sd_server001";
    private const string TDengineSubTable = TDengineDb + "." + TDengineTable;

    // ── 共享数据 ──────────────────────────────────────────────────────────
    private BenchmarkDataPoint[] _dataPoints = [];

    // ── SQLite ─────────────────────────────────────────────────────────────
    private string _sqliteDbPath = string.Empty;

    // ── InfluxDB ───────────────────────────────────────────────────────────
    private InfluxDBClient? _influxClient;
    private bool _influxAvailable;

    // ── TDengine ──────────────────────────────────────────────────────────
    private TDengineRestClient? _tdengineClient;
    private bool _tdengineAvailable;

    // ── TSLite（内存占位） ─────────────────────────────────────────────────
    private BenchmarkDataPoint[] _tsLiteStore = [];

    // ─────────────────────────────────────────────────────────────────────
    // GlobalSetup：写入 100 万条测试数据
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>全局初始化：向各数据库写入 100 万条测试数据，供后续反复聚合。</summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _dataPoints = DataGenerator.Generate(DataPointCount);
        _tsLiteStore = _dataPoints;

        // ── SQLite ─────────────────────────────────────────────────────
        _sqliteDbPath = Path.Combine(Path.GetTempPath(),
            $"tslite_bench_aggregate_{Guid.NewGuid():N}.db");
        using var conn = OpenSqlite(_sqliteDbPath);
        SqliteExecute(conn,
            "CREATE TABLE IF NOT EXISTS sensor_data " +
            "(ts INTEGER NOT NULL, host TEXT NOT NULL, value REAL NOT NULL)");
        SqliteExecute(conn, "CREATE INDEX IF NOT EXISTS idx_ts ON sensor_data (ts)");
        SqliteBulkInsert(conn, _dataPoints);

        // ── InfluxDB ────────────────────────────────────────────────────
        try
        {
            _influxClient = new InfluxDBClient(InfluxUrl, InfluxToken);
            await _influxClient.PingAsync().ConfigureAwait(false);
            await WriteInfluxDataAsync(_dataPoints).ConfigureAwait(false);
            _influxAvailable = true;
        }
        catch
        {
            _influxAvailable = false;
            Console.Error.WriteLine(
                "[SKIP] InfluxDB 不可用。请先执行: docker compose -f tests/TSLite.Benchmarks/docker/docker-compose.yml up -d influxdb");
        }

        // ── TDengine ────────────────────────────────────────────────────
        try
        {
            _tdengineClient = new TDengineRestClient(TDengineUrl);
            await _tdengineClient.ExecuteAsync(
                $"CREATE DATABASE IF NOT EXISTS {TDengineDb} PRECISION 'ms'").ConfigureAwait(false);
            await _tdengineClient.ExecuteAsync(
                $"CREATE STABLE IF NOT EXISTS {TDengineDb}.sensor_data " +
                "(ts TIMESTAMP, value DOUBLE) TAGS (host BINARY(64))").ConfigureAwait(false);
            await _tdengineClient.ExecuteAsync(
                $"CREATE TABLE IF NOT EXISTS {TDengineSubTable} " +
                $"USING {TDengineDb}.sensor_data TAGS ('server001')").ConfigureAwait(false);
            await _tdengineClient.BulkInsertAsync(TDengineSubTable, _dataPoints).ConfigureAwait(false);
            _tdengineAvailable = true;
        }
        catch
        {
            _tdengineAvailable = false;
            Console.Error.WriteLine(
                "[SKIP] TDengine 不可用。请先执行: docker compose -f tests/TSLite.Benchmarks/docker/docker-compose.yml up -d tdengine");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Benchmark 方法
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// TSLite 1 分钟桶聚合（内存 LINQ 占位）。
    /// 对 100 万个数据点按每分钟分组，计算 AVG/MIN/MAX/COUNT。
    /// 结果约含 16,667 个桶（1M 秒 / 60 ≈ 16667 分钟）。
    /// </summary>
    [Benchmark(Baseline = true, Description = "TSLite 1分钟聚合（内存占位）")]
    public List<(long BucketTs, double Avg, double Min, double Max, int Count)> TSLite_Aggregate_1Min()
    {
        const long bucketMs = 60_000L;
        var result = new List<(long, double, double, double, int)>();

        long? curBucket = null;
        double sum = 0, min = double.MaxValue, max = double.MinValue;
        int count = 0;

        foreach (var dp in _tsLiteStore)
        {
            long bucket = dp.Timestamp / bucketMs * bucketMs;
            if (bucket != curBucket)
            {
                if (curBucket.HasValue)
                    result.Add((curBucket.Value, sum / count, min, max, count));

                curBucket = bucket;
                sum = dp.Value;
                min = dp.Value;
                max = dp.Value;
                count = 1;
            }
            else
            {
                sum += dp.Value;
                if (dp.Value < min) min = dp.Value;
                if (dp.Value > max) max = dp.Value;
                count++;
            }
        }

        if (curBucket.HasValue)
            result.Add((curBucket.Value, sum / count, min, max, count));

        return result;
    }

    /// <summary>SQLite 1 分钟桶聚合（GROUP BY，全量 100 万条，约 16,667 个桶）。</summary>
    [Benchmark(Description = "SQLite 1分钟聚合")]
    public List<(long Bucket, double Avg, double Min, double Max, long Count)> SQLite_Aggregate_1Min()
    {
        using var conn = OpenSqlite(_sqliteDbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT ts / 60000 * 60000 AS bucket, " +
            "       AVG(value)         AS avg_val, " +
            "       MIN(value)         AS min_val, " +
            "       MAX(value)         AS max_val, " +
            "       COUNT(*)           AS cnt " +
            "FROM sensor_data " +
            "GROUP BY bucket " +
            "ORDER BY bucket";

        var result = new List<(long, double, double, double, long)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add((reader.GetInt64(0), reader.GetDouble(1),
                        reader.GetDouble(2), reader.GetDouble(3), reader.GetInt64(4)));

        return result;
    }

    /// <summary>InfluxDB 1 分钟桶聚合（Flux aggregateWindow，全量 100 万条）。</summary>
    [Benchmark(Description = "InfluxDB 1分钟聚合")]
    public async Task<int> InfluxDB_Aggregate_1Min()
    {
        if (!_influxAvailable)
        {
            Console.Error.WriteLine("[SKIP] InfluxDB 不可用");
            return -1;
        }

        var startRfc3339 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var stopRfc3339 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddSeconds(DataPointCount + 1).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        var flux = $"""
            from(bucket: "{InfluxBucket}")
              |> range(start: {startRfc3339}, stop: {stopRfc3339})
              |> filter(fn: (r) => r["_measurement"] == "sensor_data")
              |> aggregateWindow(every: 1m, fn: mean, createEmpty: false)
            """;

        var tables = await _influxClient!.GetQueryApi()
            .QueryAsync(flux, InfluxOrg).ConfigureAwait(false);

        return tables.Sum(t => t.Records.Count);
    }

    /// <summary>TDengine 1 分钟桶聚合（INTERVAL(1m)，全量 100 万条）。</summary>
    [Benchmark(Description = "TDengine 1分钟聚合")]
    public async Task<string> TDengine_Aggregate_1Min()
    {
        if (!_tdengineAvailable)
        {
            Console.Error.WriteLine("[SKIP] TDengine 不可用");
            return string.Empty;
        }

        return await _tdengineClient!.ExecuteAsync(
            $"SELECT _wstart, AVG(value), MIN(value), MAX(value), COUNT(*) " +
            $"FROM {TDengineSubTable} INTERVAL(1m)").ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────────────
    // GlobalCleanup
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>全局清理：删除测试数据库。</summary>
    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (File.Exists(_sqliteDbPath))
            File.Delete(_sqliteDbPath);

        if (_influxAvailable)
        {
            try
            {
                _influxClient!.GetDeleteApi().Delete(
                    new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(DataPointCount + 1),
                    string.Empty, InfluxBucket, InfluxOrg).GetAwaiter().GetResult();
            }
            catch { /* 清理失败不影响结果 */ }

            _influxClient!.Dispose();
        }

        if (_tdengineAvailable)
        {
            try
            {
                await _tdengineClient!.ExecuteAsync($"DROP DATABASE IF EXISTS {TDengineDb}")
                    .ConfigureAwait(false);
            }
            catch { /* 清理失败不影响结果 */ }

            _tdengineClient!.Dispose();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 辅助方法
    // ─────────────────────────────────────────────────────────────────────

    private static SqliteConnection OpenSqlite(string path)
    {
        var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        SqliteExecute(conn, "PRAGMA synchronous = OFF");
        SqliteExecute(conn, "PRAGMA journal_mode = WAL");
        return conn;
    }

    private static void SqliteExecute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void SqliteBulkInsert(SqliteConnection conn, BenchmarkDataPoint[] points)
    {
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO sensor_data (ts, host, value) VALUES (@ts, @host, @val)";
        var tsParam = cmd.Parameters.AddWithValue("@ts", 0L);
        var hostParam = cmd.Parameters.AddWithValue("@host", string.Empty);
        var valParam = cmd.Parameters.AddWithValue("@val", 0.0);
        cmd.Prepare();

        foreach (var dp in points)
        {
            tsParam.Value = dp.Timestamp;
            hostParam.Value = dp.Host;
            valParam.Value = dp.Value;
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private async Task WriteInfluxDataAsync(BenchmarkDataPoint[] points)
    {
        const int batchSize = 10_000;
        var writeApi = _influxClient!.GetWriteApiAsync();

        for (int offset = 0; offset < points.Length; offset += batchSize)
        {
            int end = Math.Min(offset + batchSize, points.Length);
            var batch = new PointData[end - offset];
            for (int i = 0; i < batch.Length; i++)
            {
                var dp = points[offset + i];
                batch[i] = PointData.Measurement("sensor_data")
                    .Tag("host", dp.Host)
                    .Field("value", dp.Value)
                    .Timestamp(dp.Timestamp, InfluxDB.Client.Api.Domain.WritePrecision.Ms);
            }

            await writeApi.WritePointsAsync(batch, InfluxBucket, InfluxOrg).ConfigureAwait(false);
        }
    }
}
