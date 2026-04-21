using TSLite.Catalog;
using TSLite.Query;
using TSLite.Query.Functions;
using TSLite.Sql.Ast;
using TSLite.Storage.Format;
using Xunit;

namespace TSLite.Tests.Query.Functions;

public sealed class FunctionRegistryTests
{
    private static readonly MeasurementSchema _schema = MeasurementSchema.Create(
        "cpu",
        new[]
        {
            new MeasurementColumn("host", MeasurementColumnRole.Tag, FieldType.String),
            new MeasurementColumn("usage", MeasurementColumnRole.Field, FieldType.Float64),
            new MeasurementColumn("label", MeasurementColumnRole.Field, FieldType.String),
        });

    [Theory]
    [InlineData("count", Aggregator.Count)]
    [InlineData("sum", Aggregator.Sum)]
    [InlineData("min", Aggregator.Min)]
    [InlineData("max", Aggregator.Max)]
    [InlineData("avg", Aggregator.Avg)]
    [InlineData("first", Aggregator.First)]
    [InlineData("last", Aggregator.Last)]
    public void TryGetAggregate_ResolvesBuiltIns(string name, Aggregator aggregator)
    {
        Assert.True(FunctionRegistry.TryGetAggregate(name.ToUpperInvariant(), out var function));
        Assert.Equal(name, function.Name);
        Assert.Equal(aggregator, function.LegacyAggregator);
    }

    [Fact]
    public void TryGetAggregate_UnknownFunction_ReturnsFalse()
    {
        Assert.False(FunctionRegistry.TryGetAggregate("stddev", out _));
    }

    [Fact]
    public void GetAggregate_MapsEveryLegacyBuiltIn()
    {
        foreach (var aggregator in new[]
                 {
                     Aggregator.Count, Aggregator.Sum, Aggregator.Min, Aggregator.Max,
                     Aggregator.Avg, Aggregator.First, Aggregator.Last,
                 })
        {
            var function = FunctionRegistry.GetAggregate(aggregator);
            Assert.Equal(aggregator, function.LegacyAggregator);
        }
    }

    [Fact]
    public void ResolveFieldName_CountStar_ReturnsNull()
    {
        var function = FunctionRegistry.GetAggregate(Aggregator.Count);
        var fieldName = function.ResolveFieldName(new FunctionCallExpression("count", [], true), _schema);
        Assert.Null(fieldName);
    }

    [Fact]
    public void ResolveFieldName_SumStar_Throws()
    {
        var function = FunctionRegistry.GetAggregate(Aggregator.Sum);
        Assert.Throws<InvalidOperationException>(() =>
            function.ResolveFieldName(new FunctionCallExpression("sum", [], true), _schema));
    }

    [Fact]
    public void ResolveFieldName_TagColumn_Throws()
    {
        var function = FunctionRegistry.GetAggregate(Aggregator.Sum);
        Assert.Throws<InvalidOperationException>(() =>
            function.ResolveFieldName(
                new FunctionCallExpression("sum", new[] { new IdentifierExpression("host") }),
                _schema));
    }

    [Fact]
    public void ResolveFieldName_StringField_ThrowsForNonCount()
    {
        var function = FunctionRegistry.GetAggregate(Aggregator.Sum);
        Assert.Throws<InvalidOperationException>(() =>
            function.ResolveFieldName(
                new FunctionCallExpression("sum", new[] { new IdentifierExpression("label") }),
                _schema));
    }

    [Fact]
    public void ResolveFieldName_ValidField_ReturnsColumnName()
    {
        var function = FunctionRegistry.GetAggregate(Aggregator.Avg);
        var fieldName = function.ResolveFieldName(
            new FunctionCallExpression("avg", new[] { new IdentifierExpression("usage") }),
            _schema);
        Assert.Equal("usage", fieldName);
    }
}
