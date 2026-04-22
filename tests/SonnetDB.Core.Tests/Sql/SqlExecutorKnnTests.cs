using SonnetDB.Engine;
using SonnetDB.Query;
using SonnetDB.Sql;
using SonnetDB.Sql.Execution;
using Xunit;

namespace SonnetDB.Core.Tests.Sql;

/// <summary>
/// PR #60：knn(measurement, column, query_vector, k[, metric]) 表值函数端到端测试。
/// </summary>
public sealed class SqlExecutorKnnTests : IDisposable
{
    private readonly string _root;

    public SqlExecutorKnnTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sndb-knn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private Tsdb OpenDb()
    {
        var db = Tsdb.Open(new TsdbOptions { RootDirectory = _root });
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT docs (source TAG, embedding FIELD VECTOR(3))");
        return db;
    }

    private static SelectExecutionResult Select(Tsdb db, string sql)
        => Assert.IsType<SelectExecutionResult>(SqlExecutor.Execute(db, sql));

    // ── 基础功能 ─────────────────────────────────────────────────────────────

    [Fact]
    public void Knn_ReturnsTopKByDistance_Cosine()
    {
        using var db = OpenDb();
        // 插入 4 条向量，query 为 [1,0,0]
        // cosine distance:
        //   [1,0,0] → 0.0（最近）
        //   [1,1,0] → 1 - 1/√2 ≈ 0.293
        //   [0,1,0] → 1.0
        //   [-1,0,0] → 2.0（最远）
        SqlExecutor.Execute(db, "INSERT INTO docs (source, embedding, time) VALUES " +
            "('a', [1, 0, 0], 1000), " +
            "('b', [1, 1, 0], 2000), " +
            "('c', [0, 1, 0], 3000), " +
            "('d', [-1, 0, 0], 4000)");

        var r = Select(db, "SELECT * FROM knn(docs, embedding, [1, 0, 0], 2)");

        Assert.Equal(new[] { "time", "distance", "source", "embedding" }, r.Columns.ToArray());
        Assert.Equal(2, r.Rows.Count);

        // 第 1 行：[1,0,0]（distance ≈ 0）
        Assert.Equal(1000L, (long)r.Rows[0][0]!);
        Assert.Equal(0.0, (double)r.Rows[0][1]!, 6);
        Assert.Equal("a", r.Rows[0][2]);

        // 第 2 行：[1,1,0]（distance ≈ 0.293）
        Assert.Equal(2000L, (long)r.Rows[1][0]!);
        Assert.True((double)r.Rows[1][1]! > 0.0);
        Assert.Equal("b", r.Rows[1][2]);
    }

    [Fact]
    public void Knn_L2Metric_ReturnsCorrectOrder()
    {
        using var db = OpenDb();
        SqlExecutor.Execute(db, "INSERT INTO docs (source, embedding, time) VALUES " +
            "('a', [0, 0, 0], 1000), " +
            "('b', [3, 4, 0], 2000), " +
            "('c', [1, 0, 0], 3000)");

        var r = Select(db, "SELECT * FROM knn(docs, embedding, [0, 0, 0], 3, 'l2')");

        Assert.Equal(3, r.Rows.Count);
        // 最近：[0,0,0] → dist=0；然后 [1,0,0] → dist=1；最远 [3,4,0] → dist=5
        Assert.Equal(0.0, (double)r.Rows[0][1]!, 6);
        Assert.Equal(1.0, (double)r.Rows[1][1]!, 6);
        Assert.Equal(5.0, (double)r.Rows[2][1]!, 6);
    }

    [Fact]
    public void Knn_InnerProductMetric_ReturnsNegativeDotOrder()
    {
        using var db = OpenDb();
        SqlExecutor.Execute(db, "INSERT INTO docs (source, embedding, time) VALUES " +
            "('a', [2, 0, 0], 1000), " +
            "('b', [0, 3, 0], 2000), " +
            "('c', [-1, 0, 0], 3000)");

        // query = [1, 0, 0]，dots = [2, 0, -1]，负内积 = [-2, 0, 1]
        var r = Select(db, "SELECT * FROM knn(docs, embedding, [1, 0, 0], 3, 'inner_product')");

        Assert.Equal(3, r.Rows.Count);
        // 最近（负内积最小 = 内积最大）：[2,0,0] → neg_dot=-2
        Assert.Equal(-2.0, (double)r.Rows[0][1]!, 6);
        // 中间：[0,3,0] → neg_dot=0
        Assert.Equal(0.0, (double)r.Rows[1][1]!, 6);
        // 最远：[-1,0,0] → neg_dot=1
        Assert.Equal(1.0, (double)r.Rows[2][1]!, 6);
    }

    [Fact]
    public void Knn_KLargerThanData_ReturnsAllData()
    {
        using var db = OpenDb();
        SqlExecutor.Execute(db, "INSERT INTO docs (source, embedding, time) VALUES " +
            "('a', [1, 0, 0], 1000), " +
            "('b', [0, 1, 0], 2000)");

        var r = Select(db, "SELECT * FROM knn(docs, embedding, [1, 0, 0], 100)");

        // k=100 > 2条数据，应返回全部 2 条
        Assert.Equal(2, r.Rows.Count);
    }

    [Fact]
    public void Knn_WithTagFilter_OnlyMatchesSeries()
    {
        using var db = OpenDb();
        SqlExecutor.Execute(db, "INSERT INTO docs (source, embedding, time) VALUES " +
            "('a', [1, 0, 0], 1000), " +
            "('b', [0, 1, 0], 2000)");

        var r = Select(db, "SELECT * FROM knn(docs, embedding, [0, 1, 0], 10) WHERE source = 'a'");

        // 仅 source='a' 的一条数据
        Assert.Single(r.Rows);
        Assert.Equal("a", r.Rows[0][2]);
    }

    [Fact]
    public void Knn_WithTimeFilter_RespectsTimeRange()
    {
        using var db = OpenDb();
        SqlExecutor.Execute(db, "INSERT INTO docs (source, embedding, time) VALUES " +
            "('a', [0, 0, 0], 1000), " +
            "('a', [1, 0, 0], 2000), " +
            "('a', [1, 1, 0], 3000)");

        // 仅查询 time >= 2000
        var r = Select(db,
            "SELECT * FROM knn(docs, embedding, [0, 0, 0], 10) WHERE time >= 2000");

        Assert.Equal(2, r.Rows.Count);
        Assert.All(r.Rows, row => Assert.True((long)row[0]! >= 2000L));
    }

    [Fact]
    public void Knn_NoMatchedSeries_ReturnsEmpty()
    {
        using var db = OpenDb();
        SqlExecutor.Execute(db, "INSERT INTO docs (source, embedding, time) VALUES " +
            "('a', [1, 0, 0], 1000)");

        var r = Select(db, "SELECT * FROM knn(docs, embedding, [1, 0, 0], 5) WHERE source = 'missing'");

        Assert.Empty(r.Rows);
    }

    [Fact]
    public void Knn_OutputContainsAllFieldColumns()
    {
        // 多字段 measurement
        using var db = Tsdb.Open(new TsdbOptions { RootDirectory = _root });
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT items (category TAG, vec FIELD VECTOR(2), score FIELD FLOAT)");
        SqlExecutor.Execute(db,
            "INSERT INTO items (category, vec, score, time) VALUES ('x', [1, 0], 3.14, 1000)");

        var r = Select(db, "SELECT * FROM knn(items, vec, [1, 0], 1)");

        Assert.Equal(new[] { "time", "distance", "category", "vec", "score" }, r.Columns.ToArray());
        Assert.Single(r.Rows);
        Assert.Equal("x", r.Rows[0][2]);
        // score 字段应被回填
        Assert.Equal(3.14, (double)r.Rows[0][4]!, 6);
    }

    // ── 错误场景 ─────────────────────────────────────────────────────────────

    [Fact]
    public void Knn_WrongColumnType_Throws()
    {
        using var db = Tsdb.Open(new TsdbOptions { RootDirectory = _root });
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT t (v FIELD FLOAT)");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Select(db, "SELECT * FROM knn(t, v, [1.0], 1)"));
        Assert.Contains("VECTOR", ex.Message);
    }

    [Fact]
    public void Knn_DimMismatch_Throws()
    {
        using var db = OpenDb();
        // embedding 是 VECTOR(3)，但 query 是 [1, 0]（2维）
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Select(db, "SELECT * FROM knn(docs, embedding, [1, 0], 1)"));
        Assert.Contains("维度", ex.Message);
    }

    [Fact]
    public void Knn_KZero_Throws()
    {
        using var db = OpenDb();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Select(db, "SELECT * FROM knn(docs, embedding, [1, 0, 0], 0)"));
        Assert.Contains("正整数", ex.Message);
    }

    [Fact]
    public void Knn_UnknownMetric_Throws()
    {
        using var db = OpenDb();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Select(db, "SELECT * FROM knn(docs, embedding, [1, 0, 0], 1, 'manhattan')"));
        Assert.Contains("manhattan", ex.Message);
    }

    [Fact]
    public void Knn_MeasurementNotExists_Throws()
    {
        using var db = Tsdb.Open(new TsdbOptions { RootDirectory = _root });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Select(db, "SELECT * FROM knn(absent, col, [1.0], 1)"));
        Assert.Contains("absent", ex.Message);
    }

    // ── 度量别名 ─────────────────────────────────────────────────────────────

    [Fact]
    public void Knn_MetricAliases_Resolve()
    {
        using var db = OpenDb();
        SqlExecutor.Execute(db, "INSERT INTO docs (source, embedding, time) VALUES ('a', [1, 0, 0], 1)");

        // 应不抛出
        Select(db, "SELECT * FROM knn(docs, embedding, [1, 0, 0], 1, 'cosine_distance')");
        Select(db, "SELECT * FROM knn(docs, embedding, [1, 0, 0], 1, 'euclidean')");
        Select(db, "SELECT * FROM knn(docs, embedding, [1, 0, 0], 1, 'dot')");
        Select(db, "SELECT * FROM knn(docs, embedding, [1, 0, 0], 1, 'ip')");
    }
}
