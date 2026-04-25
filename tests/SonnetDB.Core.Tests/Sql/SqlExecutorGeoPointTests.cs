using SonnetDB.Catalog;
using SonnetDB.Engine;
using SonnetDB.Model;
using SonnetDB.Query;
using SonnetDB.Sql.Execution;
using SonnetDB.Storage.Format;
using Xunit;

namespace SonnetDB.Core.Tests.Sql;

/// <summary>
/// Milestone 15 PR #70：GEOPOINT 类型、POINT 字面量与 lat/lon 标量函数测试。
/// </summary>
public sealed class SqlExecutorGeoPointTests : IDisposable
{
    private readonly string _root;

    public SqlExecutorGeoPointTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sndb-geo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private TsdbOptions Options() => new()
    {
        RootDirectory = _root,
        SegmentWriterOptions = new SonnetDB.Storage.Segments.SegmentWriterOptions { FsyncOnCommit = false },
    };

    [Fact]
    public void CreateMeasurement_WithGeoPointColumn_RegistersType()
    {
        using var db = Tsdb.Open(Options());

        var schema = Assert.IsType<MeasurementSchema>(SqlExecutor.Execute(db,
            "CREATE MEASUREMENT vehicle (device TAG, position FIELD GEOPOINT)"));

        var col = schema.TryGetColumn("position")!;
        Assert.Equal(FieldType.GeoPoint, col.DataType);
        Assert.Null(col.VectorDimension);
    }

    [Fact]
    public void Insert_PointLiteral_RoundTripsThroughEngine()
    {
        using var db = Tsdb.Open(Options());
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT vehicle (device TAG, position FIELD GEOPOINT)");
        SqlExecutor.Execute(db,
            "INSERT INTO vehicle (time, device, position) VALUES (1000, 'car-1', POINT(39.9042, 116.4074))");

        var seriesId = SeriesId.Compute(new SeriesKey("vehicle",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["device"] = "car-1" }));
        var points = db.Query.Execute(new PointQuery(seriesId, "position",
            new TimeRange(0, long.MaxValue))).ToList();

        var point = Assert.Single(points);
        Assert.Equal(FieldType.GeoPoint, point.Value.Type);
        Assert.Equal(new GeoPoint(39.9042, 116.4074), point.Value.AsGeoPoint());
    }

    [Fact]
    public void Select_LatLon_ReturnsCoordinates()
    {
        using var db = Tsdb.Open(Options());
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT vehicle (device TAG, position FIELD GEOPOINT)");
        SqlExecutor.Execute(db,
            "INSERT INTO vehicle (time, device, position) VALUES (1000, 'car-1', POINT(31.2304, 121.4737))");

        var result = Assert.IsType<SelectExecutionResult>(SqlExecutor.Execute(db,
            "SELECT lat(position), lon(position) FROM vehicle"));

        var row = Assert.Single(result.Rows);
        Assert.Equal(31.2304, Convert.ToDouble(row[0]), 6);
        Assert.Equal(121.4737, Convert.ToDouble(row[1]), 6);
    }

    [Fact]
    public void FlushAndReopen_GeoPointSegment_RoundTrips()
    {
        using (var db = Tsdb.Open(Options()))
        {
            SqlExecutor.Execute(db,
                "CREATE MEASUREMENT vehicle (device TAG, position FIELD GEOPOINT)");
            SqlExecutor.Execute(db,
                "INSERT INTO vehicle (time, device, position) VALUES " +
                "(1000, 'car-1', POINT(39.9042, 116.4074)), " +
                "(2000, 'car-1', POINT(31.2304, 121.4737))");
            db.FlushNow();
        }

        using var reopened = Tsdb.Open(Options());
        var seriesId = SeriesId.Compute(new SeriesKey("vehicle",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["device"] = "car-1" }));
        var points = reopened.Query.Execute(new PointQuery(seriesId, "position",
            new TimeRange(0, long.MaxValue))).ToList();

        Assert.Equal(2, points.Count);
        Assert.Equal(new GeoPoint(39.9042, 116.4074), points[0].Value.AsGeoPoint());
        Assert.Equal(new GeoPoint(31.2304, 121.4737), points[1].Value.AsGeoPoint());
    }
}
