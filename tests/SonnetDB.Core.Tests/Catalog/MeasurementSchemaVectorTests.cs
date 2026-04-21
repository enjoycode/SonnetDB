using SonnetDB.Catalog;
using SonnetDB.Storage.Format;
using Xunit;

namespace SonnetDB.Core.Tests.Catalog;

/// <summary>
/// PR #58 b：<see cref="MeasurementSchema"/> + <see cref="MeasurementSchemaCodec"/>
/// 对 VECTOR 列的校验与持久化测试。
/// </summary>
public class MeasurementSchemaVectorTests
{
    [Fact]
    public void Create_VectorFieldWithDim_Succeeds()
    {
        var schema = MeasurementSchema.Create("docs", new[]
        {
            new MeasurementColumn("source", MeasurementColumnRole.Tag, FieldType.String),
            new MeasurementColumn("embedding", MeasurementColumnRole.Field, FieldType.Vector, 384),
        });

        var col = schema.TryGetColumn("embedding")!;
        Assert.Equal(FieldType.Vector, col.DataType);
        Assert.Equal(384, col.VectorDimension);
    }

    [Fact]
    public void Create_VectorWithoutDim_Throws()
    {
        Assert.Throws<ArgumentException>(() => MeasurementSchema.Create("m", new[]
        {
            new MeasurementColumn("e", MeasurementColumnRole.Field, FieldType.Vector),
        }));
    }

    [Fact]
    public void Create_VectorWithZeroDim_Throws()
    {
        Assert.Throws<ArgumentException>(() => MeasurementSchema.Create("m", new[]
        {
            new MeasurementColumn("e", MeasurementColumnRole.Field, FieldType.Vector, 0),
        }));
    }

    [Fact]
    public void Create_VectorAsTag_Throws()
    {
        Assert.Throws<ArgumentException>(() => MeasurementSchema.Create("m", new[]
        {
            new MeasurementColumn("t", MeasurementColumnRole.Tag, FieldType.Vector, 4),
            new MeasurementColumn("v", MeasurementColumnRole.Field, FieldType.Float64),
        }));
    }

    [Fact]
    public void Create_NonVectorWithDim_Throws()
    {
        Assert.Throws<ArgumentException>(() => MeasurementSchema.Create("m", new[]
        {
            new MeasurementColumn("v", MeasurementColumnRole.Field, FieldType.Float64, 4),
        }));
    }

    // ── Codec round-trip ──────────────────────────────────────────────────

    [Fact]
    public void Codec_VectorColumn_RoundTripsThroughFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "sndb-vec-codec-" + Guid.NewGuid().ToString("N") + ".tslschema");
        try
        {
            var original = MeasurementSchema.Create("docs", new[]
            {
                new MeasurementColumn("source", MeasurementColumnRole.Tag, FieldType.String),
                new MeasurementColumn("embedding", MeasurementColumnRole.Field, FieldType.Vector, 384),
                new MeasurementColumn("score", MeasurementColumnRole.Field, FieldType.Float64),
            });

            MeasurementSchemaCodec.Save(path, new[] { original });

            var loaded = MeasurementSchemaCodec.Load(path);
            Assert.Single(loaded);
            var s = loaded[0];
            Assert.Equal("docs", s.Name);
            Assert.Equal(3, s.Columns.Count);

            var emb = s.TryGetColumn("embedding")!;
            Assert.Equal(FieldType.Vector, emb.DataType);
            Assert.Equal(384, emb.VectorDimension);

            var score = s.TryGetColumn("score")!;
            Assert.Equal(FieldType.Float64, score.DataType);
            Assert.Null(score.VectorDimension);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Codec_MultipleSchemasWithMixedVectorDims_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "sndb-vec-multi-" + Guid.NewGuid().ToString("N") + ".tslschema");
        try
        {
            var schemas = new[]
            {
                MeasurementSchema.Create("a", new[]
                {
                    new MeasurementColumn("e", MeasurementColumnRole.Field, FieldType.Vector, 3),
                }),
                MeasurementSchema.Create("b", new[]
                {
                    new MeasurementColumn("v", MeasurementColumnRole.Field, FieldType.Float64),
                    new MeasurementColumn("e", MeasurementColumnRole.Field, FieldType.Vector, 1024),
                }),
            };

            MeasurementSchemaCodec.Save(path, schemas);
            var loaded = MeasurementSchemaCodec.Load(path);

            Assert.Equal(2, loaded.Count);
            Assert.Equal(3, loaded[0].TryGetColumn("e")!.VectorDimension);
            Assert.Equal(1024, loaded[1].TryGetColumn("e")!.VectorDimension);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
