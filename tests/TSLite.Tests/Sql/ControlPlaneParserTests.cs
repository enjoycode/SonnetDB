using TSLite.Sql;
using TSLite.Sql.Ast;
using Xunit;

namespace TSLite.Tests.Sql;

/// <summary>
/// PR #34a-3：控制面 DDL（CREATE/DROP/ALTER USER、GRANT/REVOKE、CREATE/DROP DATABASE）解析测试。
/// </summary>
public sealed class ControlPlaneParserTests
{
    [Fact]
    public void Parse_CreateUser_BasicForm()
    {
        var stmt = (CreateUserStatement)SqlParser.Parse("CREATE USER alice WITH PASSWORD 'pa$$'");
        Assert.Equal("alice", stmt.UserName);
        Assert.Equal("pa$$", stmt.Password);
        Assert.False(stmt.IsSuperuser);
    }

    [Fact]
    public void Parse_CreateUser_PreservesIdentifierCase()
    {
        var stmt = (CreateUserStatement)SqlParser.Parse("create user Alice with password 'p'");
        Assert.Equal("Alice", stmt.UserName);
    }

    [Fact]
    public void Parse_CreateUser_WithEscapedQuoteInPassword()
    {
        var stmt = (CreateUserStatement)SqlParser.Parse("CREATE USER bob WITH PASSWORD 'O''Hara'");
        Assert.Equal("O'Hara", stmt.Password);
    }

    [Theory]
    [InlineData("CREATE USER")]
    [InlineData("CREATE USER alice")]
    [InlineData("CREATE USER alice WITH")]
    [InlineData("CREATE USER alice WITH PASSWORD")]
    [InlineData("CREATE USER alice WITH PASSWORD 123")]
    [InlineData("CREATE USER 'alice' WITH PASSWORD 'p'")]
    public void Parse_CreateUser_BadGrammar_Throws(string sql)
    {
        Assert.Throws<SqlParseException>(() => SqlParser.Parse(sql));
    }

    [Fact]
    public void Parse_AlterUser_PasswordChange()
    {
        var stmt = (AlterUserPasswordStatement)SqlParser.Parse("ALTER USER alice WITH PASSWORD 'new'");
        Assert.Equal("alice", stmt.UserName);
        Assert.Equal("new", stmt.NewPassword);
    }

    [Fact]
    public void Parse_DropUser()
    {
        var stmt = (DropUserStatement)SqlParser.Parse("DROP USER alice");
        Assert.Equal("alice", stmt.UserName);
    }

    [Fact]
    public void Parse_DropDatabase()
    {
        var stmt = (DropDatabaseStatement)SqlParser.Parse("DROP DATABASE metrics");
        Assert.Equal("metrics", stmt.DatabaseName);
    }

    [Fact]
    public void Parse_Drop_WithoutKind_Throws()
    {
        Assert.Throws<SqlParseException>(() => SqlParser.Parse("DROP alice"));
    }

    [Fact]
    public void Parse_CreateDatabase()
    {
        var stmt = (CreateDatabaseStatement)SqlParser.Parse("CREATE DATABASE metrics");
        Assert.Equal("metrics", stmt.DatabaseName);
    }

    [Theory]
    [InlineData("GRANT READ ON DATABASE metrics TO alice", GrantPermission.Read, "metrics", "alice")]
    [InlineData("GRANT WRITE ON DATABASE metrics TO bob", GrantPermission.Write, "metrics", "bob")]
    [InlineData("GRANT ADMIN ON DATABASE metrics TO carol", GrantPermission.Admin, "metrics", "carol")]
    [InlineData("grant read on database metrics to alice", GrantPermission.Read, "metrics", "alice")]
    public void Parse_Grant_Variations(string sql, GrantPermission perm, string db, string user)
    {
        var stmt = (GrantStatement)SqlParser.Parse(sql);
        Assert.Equal(perm, stmt.Permission);
        Assert.Equal(db, stmt.Database);
        Assert.Equal(user, stmt.UserName);
    }

    [Fact]
    public void Parse_Grant_StarDatabase_AcceptsWildcard()
    {
        var stmt = (GrantStatement)SqlParser.Parse("GRANT READ ON DATABASE * TO alice");
        Assert.Equal("*", stmt.Database);
    }

    [Theory]
    [InlineData("GRANT FOO ON DATABASE m TO a")]
    [InlineData("GRANT READ DATABASE m TO a")]
    [InlineData("GRANT READ ON m TO a")]
    [InlineData("GRANT READ ON DATABASE m a")]
    public void Parse_Grant_BadGrammar_Throws(string sql)
    {
        Assert.Throws<SqlParseException>(() => SqlParser.Parse(sql));
    }

    [Fact]
    public void Parse_Revoke_FromUser()
    {
        var stmt = (RevokeStatement)SqlParser.Parse("REVOKE ON DATABASE metrics FROM alice");
        Assert.Equal("metrics", stmt.Database);
        Assert.Equal("alice", stmt.UserName);
    }

    [Fact]
    public void Parse_Revoke_StarDatabase()
    {
        var stmt = (RevokeStatement)SqlParser.Parse("REVOKE ON DATABASE * FROM alice");
        Assert.Equal("*", stmt.Database);
    }

    [Fact]
    public void Parse_Script_MixesControlPlaneAndDml()
    {
        var stmts = SqlParser.ParseScript(
            "CREATE USER alice WITH PASSWORD 'p';" +
            "GRANT WRITE ON DATABASE metrics TO alice;" +
            "CREATE MEASUREMENT cpu (host TAG, value FIELD FLOAT);" +
            "DROP USER alice");
        Assert.Equal(4, stmts.Count);
        Assert.IsType<CreateUserStatement>(stmts[0]);
        Assert.IsType<GrantStatement>(stmts[1]);
        Assert.IsType<CreateMeasurementStatement>(stmts[2]);
        Assert.IsType<DropUserStatement>(stmts[3]);
    }
}
