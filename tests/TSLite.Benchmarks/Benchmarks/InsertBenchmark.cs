using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using InfluxDB.Client;
using InfluxDB.Client.Writes;
using Microsoft.Data.Sqlite;
using TSLite.Benchmarks.Helpers;
using TSLite.Engine;
using TSLite.Engine.Compaction;
using TSLite.Engine.Retention;
using TSLite.Memory;
using TSLite.Model;

namespace TSLite.Benchmarks.Benchmarks;

/// <summary>
/// 1,000,000 条数据批量写入性能对比：TSLite、SQLite、InfluxDB、TDengine。
/// 每次迭代均先清空数据，再执行完整的 100 万条写入操作。
/// </summary>
[Config(typeof(InsertConfig))]
[MemoryDiagnoser]
[BenchmarkCategory("Insert")]
public class InsertBenchmark
{
    // ── 配置 ──────────────────────────────────────────────────────────────
    private const int DataPointCount = 1_000_000;
    private const string InfluxUrl = "http://localhost:8086";
    private const string InfluxToken = "my-super-secret-auth-token";
    private const string InfluxOrg = "tslite";
    private const string InfluxBucket = "benchmarks";
    private const string TDengineUrl = "http://localhost:6041";
    private const string TDengineDb = "bench_insert";
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

    // ── TSLite ─────────────────────────────────────────────────────────────
    private string _tsLiteRootDir = string.Empty;
    private Tsdb? _tsLiteDb;
    private Point[] _tsLitePoints = [];
    private static readonly IReadOnlyDictionary<string, string> TsLiteTags =
        new Dictionary<string, string> { ["host"] = "server001" };

    // ─────────────────────────────────────────────────────────────────────
    // GlobalSetup：生成数据 + 建立数据库 Schema
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>全局初始化：生成测试数据并创建各数据库的 Schema。</summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _dataPoints = DataGenerator.Generate(DataPointCount);

        // ── SQLite ─────────────────────────────────────────────────────
        _sqliteDbPath = Path.Combine(Path.GetTempPath(), $"tslite_bench_insert_{Guid.NewGuid():N}.db");
        using var conn = OpenSqlite(_sqliteDbPath);
        SqliteExecute(conn, "CREATE TABLE IF NOT EXISTS sensor_data " +
                            "(ts INTEGER NOT NULL, host TEXT NOT NULL, value REAL NOT NULL)");

        // ── InfluxDB ────────────────────────────────────────────────────
        try
        {
            _influxClient = new InfluxDBClient(InfluxUrl, InfluxToken);
            _influxAvailable = await _influxClient.PingAsync().ConfigureAwait(false);
            if (_influxAvailable)
                await EnsureInfluxBucketAsync().ConfigureAwait(false);
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
            await _tdengineClient.ExecuteAsync($"CREATE DATABASE IF NOT EXISTS {TDengineDb} PRECISION 'ms'")
                .ConfigureAwait(false);
            await _tdengineClient.ExecuteAsync(
                $"CREATE STABLE IF NOT EXISTS {TDengineDb}.sensor_data " +
                "(ts TIMESTAMP, `value` DOUBLE) TAGS (`host` BINARY(64))").ConfigureAwait(false);
            await _tdengineClient.ExecuteAsync(
                $"CREATE TABLE IF NOT EXISTS {TDengineSubTable} " +
                $"USING {TDengineDb}.sensor_data TAGS ('server001')").ConfigureAwait(false);
            _tdengineAvailable = true;
        }
        catch
        {
            _tdengineAvailable = false;
            Console.Error.WriteLine(
                "[SKIP] TDengine 不可用。请先执行: docker compose -f tests/TSLite.Benchmarks/docker/docker-compose.yml up -d tdengine");
        }

        // ── TSLite：预构建 Point 数组（共享 tags，避免每次迭代重新分配） ────
        _tsLitePoints = new Point[DataPointCount];
        for (int i = 0; i < DataPointCount; i++)
        {
            var dp = _dataPoints[i];
            _tsLitePoints[i] = Point.Create(
                "sensor_data",
                dp.Timestamp,
                TsLiteTags,
                new Dictionary<string, FieldValue> { ["value"] = FieldValue.FromDouble(dp.Value) });
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // IterationSetup：清空上一轮写入的数据
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>每次迭代前清空各数据库中的数据，确保每轮测量均从零开始。</summary>
    [IterationSetup]
    public void IterationSetup()
    {
        // SQLite
        using var conn = OpenSqlite(_sqliteDbPath);
        SqliteExecute(conn, "DELETE FROM sensor_data");

        // InfluxDB
        if (_influxAvailable)
        {
            _influxClient!.GetDeleteApi().Delete(
                    new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(DataPointCount + 1),
                    string.Empty, InfluxBucket, InfluxOrg)
                .GetAwaiter().GetResult();
        }

        // TDengine
        if (_tdengineAvailable)
        {
            _tdengineClient!.ExecuteAsync($"DELETE FROM {TDengineSubTable}").GetAwaiter().GetResult();
        }

        // TSLite：关闭旧实例、清空目录、重新打开
        _tsLiteDb?.Dispose();
        _tsLiteDb = null;
        if (!string.IsNullOrEmpty(_tsLiteRootDir) && Directory.Exists(_tsLiteRootDir))
            Directory.Delete(_tsLiteRootDir, recursive: true);
        _tsLiteRootDir = Path.Combine(Path.GetTempPath(), $"tslite_bench_insert_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tsLiteRootDir);
        _tsLiteDb = Tsdb.Open(new TsdbOptions
        {
            RootDirectory = _tsLiteRootDir,
            FlushPolicy = new MemTableFlushPolicy
            {
                MaxBytes = long.MaxValue,
                MaxPoints = int.MaxValue,
                MaxAge = TimeSpan.MaxValue
            },
            BackgroundFlush = new BackgroundFlushOptions { Enabled = false },
            Compaction = new CompactionPolicy { Enabled = false },
            Retention = new RetentionPolicy { Enabled = false }
        });
    }

    // ─────────────────────────────────────────────────────────────────────
    // Benchmark 方法
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// TSLite 写入 100 万条（真实引擎：MemTable + WAL + Flush）。
    /// 每次迭代均从空的 Tsdb 实例写入，最后调用 FlushNow 将数据落盘。
    /// </summary>
    [Benchmark(Baseline = true, Description = "TSLite 写入 100万条")]
    public void TSLite_Insert_1M()
    {
        _tsLiteDb!.WriteMany(_tsLitePoints);
        _tsLiteDb!.FlushNow();
    }

    /// <summary>SQLite 写入 100 万条（文件模式，WAL 日志，事务批量提交）。</summary>
    [Benchmark(Description = "SQLite 写入 100万条")]
    public void SQLite_Insert_1M()
    {
        using var conn = OpenSqlite(_sqliteDbPath);
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO sensor_data (ts, host, value) VALUES (@ts, @host, @val)";
        var tsParam = cmd.Parameters.AddWithValue("@ts", 0L);
        var hostParam = cmd.Parameters.AddWithValue("@host", string.Empty);
        var valParam = cmd.Parameters.AddWithValue("@val", 0.0);
        cmd.Prepare();

        foreach (var dp in _dataPoints)
        {
            tsParam.Value = dp.Timestamp;
            hostParam.Value = dp.Host;
            valParam.Value = dp.Value;
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <summary>InfluxDB 写入 100 万条（Line Protocol，10k 批次）。</summary>
    [Benchmark(Description = "InfluxDB 写入 100万条")]
    public async Task InfluxDB_Insert_1M()
    {
        if (!_influxAvailable)
        {
            Console.Error.WriteLine("[SKIP] InfluxDB 不可用");
            return;
        }

        const int batchSize = 10_000;
        var writeApi = _influxClient!.GetWriteApiAsync();

        for (int offset = 0; offset < _dataPoints.Length; offset += batchSize)
        {
            int end = Math.Min(offset + batchSize, _dataPoints.Length);
            var batch = new PointData[end - offset];
            for (int i = 0; i < batch.Length; i++)
            {
                var dp = _dataPoints[offset + i];
                batch[i] = PointData.Measurement("sensor_data")
                    .Tag("host", dp.Host)
                    .Field("value", dp.Value)
                    .Timestamp(dp.Timestamp, InfluxDB.Client.Api.Domain.WritePrecision.Ms);
            }

            await writeApi.WritePointsAsync(batch, InfluxBucket, InfluxOrg).ConfigureAwait(false);
        }
    }

    /// <summary>TDengine 写入 100 万条（REST API，1k 批次）。</summary>
    [Benchmark(Description = "TDengine 写入 100万条")]
    public async Task TDengine_Insert_1M()
    {
        if (!_tdengineAvailable)
        {
            Console.Error.WriteLine("[SKIP] TDengine 不可用");
            return;
        }

        await _tdengineClient!.BulkInsertAsync(TDengineSubTable, _dataPoints).ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────────────
    // GlobalCleanup
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>全局清理：删除测试数据库文件及外部数据库中的 Schema。</summary>
    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        // SQLite
        SqliteConnection.ClearAllPools();
        if (File.Exists(_sqliteDbPath))
            File.Delete(_sqliteDbPath);
        // InfluxDB：仅删除数据，保留 bucket，避免后续基准进程因 bucket 不存在而失败。
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

        // TDengine
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

        // TSLite
        _tsLiteDb?.Dispose();
        _tsLiteDb = null;
        if (!string.IsNullOrEmpty(_tsLiteRootDir) && Directory.Exists(_tsLiteRootDir))
            Directory.Delete(_tsLiteRootDir, recursive: true);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 辅助方法
    // ─────────────────────────────────────────────────────────────────────

    private async Task EnsureInfluxBucketAsync()
    {
        var bucketsApi = _influxClient!.GetBucketsApi();
        var existing = await bucketsApi.FindBucketByNameAsync(InfluxBucket).ConfigureAwait(false);
        if (existing is not null) return;
        var orgs = await _influxClient.GetOrganizationsApi()
            .FindOrganizationsAsync(org: InfluxOrg).ConfigureAwait(false);
        if (orgs is null || orgs.Count == 0)
            throw new InvalidOperationException($"InfluxDB org '{InfluxOrg}' not found");
        await bucketsApi.CreateBucketAsync(InfluxBucket, orgs[0].Id).ConfigureAwait(false);
    }

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

    // ─────────────────────────────────────────────────────────────────────
    // BenchmarkDotNet 配置
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 插入基准采用监控模式（RunStrategy.Monitoring）：
    /// 每轮直接测量一次完整 100 万条写入，避免吞吐量受到多轮迭代干扰。
    /// </summary>
    private sealed class InsertConfig : ManualConfig
    {
        public InsertConfig()
        {
            AddJob(Job.Default
                .WithStrategy(RunStrategy.Monitoring)
                .WithWarmupCount(0)
                .WithIterationCount(3));
        }
    }
}
