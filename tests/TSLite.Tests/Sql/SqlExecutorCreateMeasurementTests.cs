using TSLite.Catalog;
using TSLite.Engine;
using TSLite.Sql.Ast;
using TSLite.Sql.Execution;
using TSLite.Storage.Format;
using Xunit;

namespace TSLite.Tests.Sql;

public class SqlExecutorCreateMeasurementTests : IDisposable
{
    private readonly string _root;

    public SqlExecutorCreateMeasurementTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tslite-exec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private TsdbOptions Options() => new() { RootDirectory = _root };

    [Fact]
    public void Execute_CreateMeasurement_RegistersInCatalog()
    {
        using var db = Tsdb.Open(Options());

        var result = SqlExecutor.Execute(db,
            "CREATE MEASUREMENT cpu (host TAG, region TAG, usage FIELD FLOAT, count FIELD INT)");

        var schema = Assert.IsType<MeasurementSchema>(result);
        Assert.Equal("cpu", schema.Name);
        Assert.Equal(4, schema.Columns.Count);
        Assert.Equal(MeasurementColumnRole.Tag, schema.Columns[0].Role);
        Assert.Equal(FieldType.String, schema.Columns[0].DataType);
        Assert.Equal(MeasurementColumnRole.Field, schema.Columns[2].Role);
        Assert.Equal(FieldType.Float64, schema.Columns[2].DataType);
        Assert.Equal(FieldType.Int64, schema.Columns[3].DataType);

        Assert.True(db.Measurements.Contains("cpu"));
    }

    [Fact]
    public void Execute_CreateMeasurement_PersistsAcrossReopen()
    {
        using (var db = Tsdb.Open(Options()))
        {
            SqlExecutor.Execute(db,
                "CREATE MEASUREMENT mem (host TAG, used FIELD INT, ok FIELD BOOL)");
        }

        using var db2 = Tsdb.Open(Options());
        var schema = db2.Measurements.TryGet("mem");
        Assert.NotNull(schema);
        Assert.Equal(3, schema!.Columns.Count);
        Assert.Equal(FieldType.Boolean, schema.Columns[2].DataType);
    }

    [Fact]
    public void Execute_CreateMeasurement_DuplicateThrows()
    {
        using var db = Tsdb.Open(Options());
        SqlExecutor.Execute(db, "CREATE MEASUREMENT m (a FIELD FLOAT)");
        Assert.Throws<InvalidOperationException>(() =>
            SqlExecutor.Execute(db, "CREATE MEASUREMENT m (b FIELD INT)"));
    }

    [Fact]
    public void Execute_UnsupportedStatement_ThrowsNotSupported()
    {
        using var db = Tsdb.Open(Options());
        // DELETE 已能解析但执行尚未实现（PR #26）。
        Assert.Throws<NotSupportedException>(() =>
            SqlExecutor.Execute(db, "DELETE FROM cpu WHERE time = 0"));
    }

    [Fact]
    public void CreateMeasurement_DirectApi_PersistsImmediately()
    {
        var schemaFile = TsdbPaths.MeasurementSchemaPath(_root);
        using (var db = Tsdb.Open(Options()))
        {
            db.CreateMeasurement(MeasurementSchema.Create("disk", new[]
            {
                new MeasurementColumn("dev", MeasurementColumnRole.Tag, FieldType.String),
                new MeasurementColumn("free", MeasurementColumnRole.Field, FieldType.Float64),
            }));
            // Persisted right away (before Dispose)
            Assert.True(File.Exists(schemaFile));
        }

        var loaded = MeasurementSchemaCodec.Load(schemaFile);
        Assert.Single(loaded);
        Assert.Equal("disk", loaded[0].Name);
    }
}
