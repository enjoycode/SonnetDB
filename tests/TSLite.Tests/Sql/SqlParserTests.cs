using TSLite.Sql;
using TSLite.Sql.Ast;
using Xunit;

namespace TSLite.Tests.Sql;

public class SqlParserTests
{
    // ── CREATE MEASUREMENT ────────────────────────────────────────────────

    [Fact]
    public void Parse_CreateMeasurement_WithTagsAndFields_ReturnsAst()
    {
        var stmt = (CreateMeasurementStatement)SqlParser.Parse(
            "CREATE MEASUREMENT cpu (host TAG, region TAG STRING, value FIELD FLOAT, ok FIELD BOOL)");

        Assert.Equal("cpu", stmt.Name);
        Assert.Equal(4, stmt.Columns.Count);
        Assert.Equal(new ColumnDefinition("host", ColumnKind.Tag, SqlDataType.String), stmt.Columns[0]);
        Assert.Equal(new ColumnDefinition("region", ColumnKind.Tag, SqlDataType.String), stmt.Columns[1]);
        Assert.Equal(new ColumnDefinition("value", ColumnKind.Field, SqlDataType.Float64), stmt.Columns[2]);
        Assert.Equal(new ColumnDefinition("ok", ColumnKind.Field, SqlDataType.Boolean), stmt.Columns[3]);
    }

    [Fact]
    public void Parse_CreateMeasurement_TagWithNonStringType_Throws()
    {
        Assert.Throws<SqlParseException>(() =>
            SqlParser.Parse("CREATE MEASUREMENT m (host TAG INT)"));
    }

    [Fact]
    public void Parse_CreateMeasurement_MissingType_Throws()
    {
        Assert.Throws<SqlParseException>(() =>
            SqlParser.Parse("CREATE MEASUREMENT m (value FIELD)"));
    }

    // ── INSERT ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Insert_SingleRow_ReturnsAst()
    {
        var stmt = (InsertStatement)SqlParser.Parse(
            "INSERT INTO cpu (host, value, ts) VALUES ('node-1', 1.5, 1700000000000)");

        Assert.Equal("cpu", stmt.Measurement);
        Assert.Equal(new[] { "host", "value", "ts" }, stmt.Columns);
        Assert.Single(stmt.Rows);
        var row = stmt.Rows[0];
        Assert.Equal(LiteralExpression.String("node-1"), row[0]);
        Assert.Equal(LiteralExpression.Float(1.5), row[1]);
        Assert.Equal(LiteralExpression.Integer(1_700_000_000_000L), row[2]);
    }

    [Fact]
    public void Parse_Insert_MultipleRows_ReturnsAllRows()
    {
        var stmt = (InsertStatement)SqlParser.Parse(
            "INSERT INTO cpu (host, value) VALUES ('a', 1), ('b', 2), ('c', 3)");
        Assert.Equal(3, stmt.Rows.Count);
        Assert.Equal(LiteralExpression.String("c"), stmt.Rows[2][0]);
        Assert.Equal(LiteralExpression.Integer(3), stmt.Rows[2][1]);
    }

    [Fact]
    public void Parse_Insert_RowArityMismatch_Throws()
    {
        Assert.Throws<SqlParseException>(() =>
            SqlParser.Parse("INSERT INTO cpu (host, value) VALUES ('a')"));
    }

    [Fact]
    public void Parse_Insert_BooleanAndNullLiteralsSupported()
    {
        var stmt = (InsertStatement)SqlParser.Parse(
            "INSERT INTO m (a, b) VALUES (TRUE, NULL)");
        Assert.Equal(LiteralExpression.Bool(true), stmt.Rows[0][0]);
        Assert.Equal(LiteralExpression.Null(), stmt.Rows[0][1]);
    }

    // ── SELECT ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Select_Star_FromOnly()
    {
        var stmt = (SelectStatement)SqlParser.Parse("SELECT * FROM cpu");
        Assert.Equal("cpu", stmt.Measurement);
        Assert.Single(stmt.Projections);
        Assert.IsType<StarExpression>(stmt.Projections[0].Expression);
        Assert.Null(stmt.Projections[0].Alias);
        Assert.Null(stmt.Where);
        Assert.Null(stmt.GroupByTime);
    }

    [Fact]
    public void Parse_Select_AggregateAndAlias()
    {
        var stmt = (SelectStatement)SqlParser.Parse(
            "SELECT count(*) AS c, avg(value) v FROM cpu");
        Assert.Equal(2, stmt.Projections.Count);

        var first = (FunctionCallExpression)stmt.Projections[0].Expression;
        Assert.Equal("count", first.Name);
        Assert.True(first.IsStar);
        Assert.Equal("c", stmt.Projections[0].Alias);

        var second = (FunctionCallExpression)stmt.Projections[1].Expression;
        Assert.Equal("avg", second.Name);
        Assert.False(second.IsStar);
        Assert.Single(second.Arguments);
        Assert.Equal(new IdentifierExpression("value"), second.Arguments[0]);
        Assert.Equal("v", stmt.Projections[1].Alias);
    }

    [Fact]
    public void Parse_Select_WhereClause_PrecedenceAndOrAndComparison()
    {
        var stmt = (SelectStatement)SqlParser.Parse(
            "SELECT * FROM cpu WHERE host = 'a' AND value > 10 OR ok = TRUE");

        var or = Assert.IsType<BinaryExpression>(stmt.Where);
        Assert.Equal(SqlBinaryOperator.Or, or.Operator);

        var and = Assert.IsType<BinaryExpression>(or.Left);
        Assert.Equal(SqlBinaryOperator.And, and.Operator);

        var hostEq = Assert.IsType<BinaryExpression>(and.Left);
        Assert.Equal(SqlBinaryOperator.Equal, hostEq.Operator);
        Assert.Equal(new IdentifierExpression("host"), hostEq.Left);
        Assert.Equal(LiteralExpression.String("a"), hostEq.Right);

        var valueGt = Assert.IsType<BinaryExpression>(and.Right);
        Assert.Equal(SqlBinaryOperator.GreaterThan, valueGt.Operator);

        var okEq = Assert.IsType<BinaryExpression>(or.Right);
        Assert.Equal(SqlBinaryOperator.Equal, okEq.Operator);
        Assert.Equal(LiteralExpression.Bool(true), okEq.Right);
    }

    [Fact]
    public void Parse_Select_NotOperator()
    {
        var stmt = (SelectStatement)SqlParser.Parse("SELECT * FROM cpu WHERE NOT ok = TRUE");
        var notExpr = Assert.IsType<UnaryExpression>(stmt.Where);
        Assert.Equal(SqlUnaryOperator.Not, notExpr.Operator);
        Assert.IsType<BinaryExpression>(notExpr.Operand);
    }

    [Fact]
    public void Parse_Select_ParenthesesOverridePrecedence()
    {
        var stmt = (SelectStatement)SqlParser.Parse(
            "SELECT * FROM cpu WHERE (host = 'a' OR host = 'b') AND value > 0");
        var and = Assert.IsType<BinaryExpression>(stmt.Where);
        Assert.Equal(SqlBinaryOperator.And, and.Operator);
        var or = Assert.IsType<BinaryExpression>(and.Left);
        Assert.Equal(SqlBinaryOperator.Or, or.Operator);
    }

    [Fact]
    public void Parse_Select_GroupByTime_ParsesBucketSize()
    {
        var stmt = (SelectStatement)SqlParser.Parse(
            "SELECT avg(v) FROM cpu WHERE time >= 1000 AND time < 2000 GROUP BY time(1m)");
        Assert.NotNull(stmt.GroupByTime);
        Assert.Equal(60_000L, stmt.GroupByTime!.BucketSizeMs);
    }

    [Fact]
    public void Parse_Select_GroupByTime_ZeroDuration_Throws()
    {
        Assert.Throws<SqlParseException>(() =>
            SqlParser.Parse("SELECT avg(v) FROM cpu GROUP BY time(0ms)"));
    }

    [Fact]
    public void Parse_Select_NegativeNumberInWhere()
    {
        var stmt = (SelectStatement)SqlParser.Parse("SELECT * FROM cpu WHERE value > -1.5");
        var binary = Assert.IsType<BinaryExpression>(stmt.Where);
        var negate = Assert.IsType<UnaryExpression>(binary.Right);
        Assert.Equal(SqlUnaryOperator.Negate, negate.Operator);
        Assert.Equal(LiteralExpression.Float(1.5), negate.Operand);
    }

    [Fact]
    public void Parse_Select_NotEqualOperators()
    {
        var stmt1 = (SelectStatement)SqlParser.Parse("SELECT * FROM cpu WHERE host != 'a'");
        var stmt2 = (SelectStatement)SqlParser.Parse("SELECT * FROM cpu WHERE host <> 'a'");
        var b1 = Assert.IsType<BinaryExpression>(stmt1.Where);
        var b2 = Assert.IsType<BinaryExpression>(stmt2.Where);
        Assert.Equal(SqlBinaryOperator.NotEqual, b1.Operator);
        Assert.Equal(SqlBinaryOperator.NotEqual, b2.Operator);
    }

    // ── DELETE ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Delete_WithTimeRange()
    {
        var stmt = (DeleteStatement)SqlParser.Parse(
            "DELETE FROM cpu WHERE time >= 100 AND time < 200");
        Assert.Equal("cpu", stmt.Measurement);
        var and = Assert.IsType<BinaryExpression>(stmt.Where);
        Assert.Equal(SqlBinaryOperator.And, and.Operator);
    }

    [Fact]
    public void Parse_Delete_RequiresWhere()
    {
        Assert.Throws<SqlParseException>(() => SqlParser.Parse("DELETE FROM cpu"));
    }

    // ── 综合 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_AllowsTrailingSemicolon()
    {
        var stmt = SqlParser.Parse("SELECT * FROM cpu;");
        Assert.IsType<SelectStatement>(stmt);
    }

    [Fact]
    public void Parse_TrailingGarbage_Throws()
    {
        Assert.Throws<SqlParseException>(() => SqlParser.Parse("SELECT * FROM cpu garbage"));
    }

    [Fact]
    public void Parse_UnknownStatement_Throws()
    {
        Assert.Throws<SqlParseException>(() => SqlParser.Parse("UPDATE cpu SET value = 1"));
    }

    [Fact]
    public void ParseScript_MultipleStatements_AreReturnedInOrder()
    {
        var stmts = SqlParser.ParseScript(
            "CREATE MEASUREMENT m (h TAG, v FIELD FLOAT); INSERT INTO m (h, v) VALUES ('a', 1); SELECT * FROM m");
        Assert.Equal(3, stmts.Count);
        Assert.IsType<CreateMeasurementStatement>(stmts[0]);
        Assert.IsType<InsertStatement>(stmts[1]);
        Assert.IsType<SelectStatement>(stmts[2]);
    }
}
