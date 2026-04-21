using TSLite.Engine;
using TSLite.Sql;
using TSLite.Sql.Execution;
using Xunit;

namespace TSLite.Tests.Sql;

/// <summary>
/// PR #54 — PID 内置函数 SQL 端到端集成测试。
/// 覆盖 <c>pid_series</c> 行级窗口形态、<c>pid</c> 聚合形态、参数校验，以及
/// <c>INSERT … SELECT pid_series(...)</c> 控制回写场景。
/// </summary>
public sealed class SqlExecutorPidFunctionTests : IDisposable
{
    private readonly string _root;

    public SqlExecutorPidFunctionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tslite-pid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private TsdbOptions Options() => new() { RootDirectory = _root };

    private static Tsdb OpenWithSchema(TsdbOptions options)
    {
        var db = Tsdb.Open(options);
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT reactor (device TAG, temperature FIELD FLOAT)");
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT actuator (device TAG, valve FIELD FLOAT)");
        return db;
    }

    private static SelectExecutionResult Select(Tsdb db, string sql)
        => Assert.IsType<SelectExecutionResult>(SqlExecutor.Execute(db, sql));

    // ── pid_series 行级 ─────────────────────────────────────────────────

    [Fact]
    public void Select_PidSeries_ProducesPerRowControlSignal()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO reactor (time, device, temperature) VALUES " +
            "(0, 'r1', 0), (1000, 'r1', 4), (2000, 'r1', 7)");

        var r = Select(db,
            "SELECT pid_series(temperature, 10, 1, 1, 1) FROM reactor");

        Assert.Equal(3, r.Rows.Count);
        // 与 PidControllerTests.PidSeriesEvaluator_OutputsControlSignalPerRow 同步：
        Assert.Equal(10.0, (double)r.Rows[0][0]!, precision: 9);
        Assert.Equal(8.0, (double)r.Rows[1][0]!, precision: 9);
        Assert.Equal(9.0, (double)r.Rows[2][0]!, precision: 9);
    }

    [Fact]
    public void Select_PidSeries_MixesWithTimeAndField()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO reactor (time, device, temperature) VALUES " +
            "(0, 'r1', 0), (1000, 'r1', 4)");

        var r = Select(db,
            "SELECT time, temperature, pid_series(temperature, 10, 1, 0, 0) AS u FROM reactor");
        Assert.Equal(["time", "temperature", "u"], r.Columns);
        // ki=0, kd=0 → u = Kp*e
        Assert.Equal(10.0, (double)r.Rows[0][2]!, precision: 9);
        Assert.Equal(6.0, (double)r.Rows[1][2]!, precision: 9);
    }

    // ── pid 聚合形态 ────────────────────────────────────────────────────

    [Fact]
    public void Select_PidAggregate_ReturnsLastControlSignal()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO reactor (time, device, temperature) VALUES " +
            "(0, 'r1', 0), (1000, 'r1', 4), (2000, 'r1', 7)");

        var r = Select(db, "SELECT pid(temperature, 10, 1, 1, 1) FROM reactor");
        Assert.Single(r.Rows);
        Assert.Equal(9.0, (double)r.Rows[0][0]!, precision: 9);
    }

    [Fact]
    public void Select_PidAggregate_GroupedByTimeBucket()
    {
        using var db = OpenWithSchema(Options());
        // 桶 1（[0,1000)）：行 ts=0，pv=0 → u=10
        // 桶 2（[1000,2000)）：行 ts=1000 pv=4，新桶 → 桶内首行只算 P → u=Kp*(10-4)=6
        SqlExecutor.Execute(db,
            "INSERT INTO reactor (time, device, temperature) VALUES " +
            "(0, 'r1', 0), (1000, 'r1', 4)");

        var r = Select(db,
            "SELECT pid(temperature, 10, 1, 1, 1) FROM reactor GROUP BY time(1s)");

        Assert.Equal(2, r.Rows.Count);
        Assert.Equal(10.0, (double)r.Rows[0][0]!, precision: 9);
        Assert.Equal(6.0, (double)r.Rows[1][0]!, precision: 9);
    }

    // ── pid_series 结果可被重新 INSERT 回写作为控制量（模拟控制回路） ───────────

    [Fact]
    public void PidSeries_OutputsCanBePersistedAsActuatorCommands()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO reactor (time, device, temperature) VALUES " +
            "(0, 'r1', 0), (1000, 'r1', 4), (2000, 'r1', 7)");

        // 在嵌入式场景下，应用代码先查询 pid_series 结果、再 INSERT 到 actuator。
        var r = Select(db,
            "SELECT time, pid_series(temperature, 10, 1, 1, 1) FROM reactor");
        Assert.Equal(3, r.Rows.Count);

        var values = new System.Text.StringBuilder();
        for (int i = 0; i < r.Rows.Count; i++)
        {
            if (i > 0) values.Append(", ");
            values.Append('(').Append(r.Rows[i][0]).Append(", 'r1', ")
                  .Append(((double)r.Rows[i][1]!).ToString(System.Globalization.CultureInfo.InvariantCulture))
                  .Append(')');
        }
        SqlExecutor.Execute(db, $"INSERT INTO actuator (time, device, valve) VALUES {values}");

        var actuator = Select(db, "SELECT time, valve FROM actuator");
        Assert.Equal(3, actuator.Rows.Count);
        Assert.Equal(10.0, (double)actuator.Rows[0][1]!, precision: 9);
        Assert.Equal(8.0, (double)actuator.Rows[1][1]!, precision: 9);
        Assert.Equal(9.0, (double)actuator.Rows[2][1]!, precision: 9);
    }

    // ── 参数校验 ────────────────────────────────────────────────────────

    [Fact]
    public void Select_PidSeries_RejectsWrongArgumentCount()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO reactor (time, device, temperature) VALUES (0, 'r1', 0)");

        Assert.Throws<InvalidOperationException>(() =>
            Select(db, "SELECT pid_series(temperature, 10, 1, 1) FROM reactor"));
    }

    [Fact]
    public void Select_Pid_RejectsWrongArgumentCount()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO reactor (time, device, temperature) VALUES (0, 'r1', 0)");

        Assert.Throws<InvalidOperationException>(() =>
            Select(db, "SELECT pid(temperature, 10, 1) FROM reactor"));
    }

    [Fact]
    public void Select_Pid_RejectsTagColumn()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO reactor (time, device, temperature) VALUES (0, 'r1', 0)");

        Assert.Throws<InvalidOperationException>(() =>
            Select(db, "SELECT pid(device, 10, 1, 1, 1) FROM reactor"));
    }

    [Fact]
    public void Select_PidSeries_AcceptsNegativeGains()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO reactor (time, device, temperature) VALUES (0, 'r1', 0), (1000, 'r1', 5)");

        // kp=-1：第一行 u = -1*10 = -10；第二行 e=5 u = -1*5 = -5（ki=0, kd=0）
        var r = Select(db, "SELECT pid_series(temperature, 10, -1, 0, 0) FROM reactor");
        Assert.Equal(-10.0, (double)r.Rows[0][0]!, precision: 9);
        Assert.Equal(-5.0, (double)r.Rows[1][0]!, precision: 9);
    }
}
