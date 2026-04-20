using TSLite.Catalog;
using TSLite.Storage.Format;
using Xunit;

namespace TSLite.Tests.Catalog;

public class MeasurementSchemaTests
{
    private static MeasurementColumn Field(string name, FieldType type = FieldType.Float64)
        => new(name, MeasurementColumnRole.Field, type);

    private static MeasurementColumn Tag(string name)
        => new(name, MeasurementColumnRole.Tag, FieldType.String);

    [Fact]
    public void Create_WithValidColumns_BuildsSchema()
    {
        var schema = MeasurementSchema.Create("cpu", new MeasurementColumn[]
        {
            Tag("host"),
            Field("usage", FieldType.Float64),
        });

        Assert.Equal("cpu", schema.Name);
        Assert.Equal(2, schema.Columns.Count);
        Assert.NotNull(schema.TryGetColumn("host"));
        Assert.NotNull(schema.TryGetColumn("usage"));
        Assert.Null(schema.TryGetColumn("missing"));
        Assert.Single(schema.TagColumns);
        Assert.Single(schema.FieldColumns);
    }

    [Fact]
    public void Create_WithEmptyColumns_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MeasurementSchema.Create("cpu", []));
    }

    [Fact]
    public void Create_WithoutAnyField_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MeasurementSchema.Create("cpu", new[] { Tag("host") }));
    }

    [Fact]
    public void Create_WithDuplicateColumnName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MeasurementSchema.Create("cpu", new[]
            {
                Field("a"),
                Field("a"),
            }));
    }

    [Fact]
    public void Create_WithNonStringTag_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MeasurementSchema.Create("cpu", new[]
            {
                new MeasurementColumn("host", MeasurementColumnRole.Tag, FieldType.Int64),
                Field("usage"),
            }));
    }

    [Fact]
    public void Create_WithUnknownDataType_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MeasurementSchema.Create("cpu", new[]
            {
                new MeasurementColumn("usage", MeasurementColumnRole.Field, FieldType.Unknown),
            }));
    }

    [Fact]
    public void Create_WithEmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MeasurementSchema.Create(" ", new[] { Field("x") }));
    }

    [Fact]
    public void Catalog_Add_RejectsDuplicate()
    {
        var cat = new MeasurementCatalog();
        cat.Add(MeasurementSchema.Create("m", new[] { Field("x") }));
        Assert.Throws<InvalidOperationException>(() =>
            cat.Add(MeasurementSchema.Create("m", new[] { Field("y") })));
        Assert.True(cat.Contains("m"));
        Assert.Equal(1, cat.Count);
    }

    [Fact]
    public void Catalog_Snapshot_ReturnsSortedByName()
    {
        var cat = new MeasurementCatalog();
        cat.Add(MeasurementSchema.Create("zeta", new[] { Field("x") }));
        cat.Add(MeasurementSchema.Create("alpha", new[] { Field("x") }));
        cat.Add(MeasurementSchema.Create("mu", new[] { Field("x") }));

        var names = cat.Snapshot().Select(s => s.Name).ToArray();
        Assert.Equal(new[] { "alpha", "mu", "zeta" }, names);
    }
}
