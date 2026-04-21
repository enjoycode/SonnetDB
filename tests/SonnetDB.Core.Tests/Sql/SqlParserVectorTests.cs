using SonnetDB.Sql;
using SonnetDB.Sql.Ast;
using Xunit;

namespace SonnetDB.Core.Tests.Sql;

/// <summary>
/// PR #58 b：CREATE MEASUREMENT 中 <c>VECTOR(dim)</c> 列声明 + 表达式层
/// <c>[v0, v1, ...]</c> 向量字面量解析测试。
/// </summary>
public class SqlParserVectorTests
{
    // ── CREATE MEASUREMENT ... VECTOR(dim) ────────────────────────────────

    [Fact]
    public void Parse_CreateMeasurement_VectorColumn_ReturnsAstWithDim()
    {
        var stmt = (CreateMeasurementStatement)SqlParser.Parse(
            "CREATE MEASUREMENT docs (source TAG, embedding FIELD VECTOR(384))");

        Assert.Equal("docs", stmt.Name);
        Assert.Equal(2, stmt.Columns.Count);
        Assert.Equal(new ColumnDefinition("source", ColumnKind.Tag, SqlDataType.String), stmt.Columns[0]);
        Assert.Equal(
            new ColumnDefinition("embedding", ColumnKind.Field, SqlDataType.Vector, 384),
            stmt.Columns[1]);
    }

    [Fact]
    public void Parse_CreateMeasurement_VectorWithoutDim_Throws()
    {
        Assert.Throws<SqlParseException>(() =>
            SqlParser.Parse("CREATE MEASUREMENT m (e FIELD VECTOR)"));
    }

    [Fact]
    public void Parse_CreateMeasurement_VectorEmptyParens_Throws()
    {
        Assert.Throws<SqlParseException>(() =>
            SqlParser.Parse("CREATE MEASUREMENT m (e FIELD VECTOR())"));
    }

    [Fact]
    public void Parse_CreateMeasurement_VectorZeroDim_Throws()
    {
        Assert.Throws<SqlParseException>(() =>
            SqlParser.Parse("CREATE MEASUREMENT m (e FIELD VECTOR(0))"));
    }

    [Fact]
    public void Parse_CreateMeasurement_VectorNegativeDim_Throws()
    {
        // -1 进入 ParseFieldDataType 时，'-' 不是 IntegerLiteral，先报"VECTOR 必须声明维度"。
        Assert.Throws<SqlParseException>(() =>
            SqlParser.Parse("CREATE MEASUREMENT m (e FIELD VECTOR(-1))"));
    }

    [Fact]
    public void Parse_CreateMeasurement_TagVector_Throws()
    {
        // Tag 列只能 STRING：解析阶段就拒绝 VECTOR。
        Assert.Throws<SqlParseException>(() =>
            SqlParser.Parse("CREATE MEASUREMENT m (host TAG VECTOR(3))"));
    }

    // ── 向量字面量 [v0, v1, ...] ──────────────────────────────────────────

    [Fact]
    public void Parse_Insert_VectorLiteral_ParsesComponents()
    {
        var stmt = (InsertStatement)SqlParser.Parse(
            "INSERT INTO docs (source, embedding) VALUES ('a', [0.1, 0.2, -0.3])");

        var row = stmt.Rows[0];
        Assert.IsType<LiteralExpression>(row[0]);
        var vec = Assert.IsType<VectorLiteralExpression>(row[1]);
        Assert.Equal(new double[] { 0.1, 0.2, -0.3 }, vec.Components);
    }

    [Fact]
    public void Parse_Insert_VectorLiteral_AllowsIntComponents()
    {
        var stmt = (InsertStatement)SqlParser.Parse(
            "INSERT INTO docs (source, embedding) VALUES ('a', [1, -2, 3])");

        var vec = Assert.IsType<VectorLiteralExpression>(stmt.Rows[0][1]);
        Assert.Equal(new double[] { 1.0, -2.0, 3.0 }, vec.Components);
    }

    [Fact]
    public void Parse_Insert_VectorLiteral_SingleComponent()
    {
        var stmt = (InsertStatement)SqlParser.Parse(
            "INSERT INTO docs (source, embedding) VALUES ('a', [42])");
        var vec = Assert.IsType<VectorLiteralExpression>(stmt.Rows[0][1]);
        Assert.Equal(new double[] { 42.0 }, vec.Components);
    }

    [Fact]
    public void Parse_VectorLiteral_Empty_Throws()
    {
        Assert.Throws<SqlParseException>(() =>
            SqlParser.Parse("INSERT INTO docs (e) VALUES ([])"));
    }

    [Fact]
    public void Parse_VectorLiteral_Unclosed_Throws()
    {
        Assert.Throws<SqlParseException>(() =>
            SqlParser.Parse("INSERT INTO docs (e) VALUES ([1, 2, 3)"));
    }

    [Fact]
    public void Parse_VectorLiteral_NonNumericComponent_Throws()
    {
        Assert.Throws<SqlParseException>(() =>
            SqlParser.Parse("INSERT INTO docs (e) VALUES ([1, 'x', 3])"));
    }
}
