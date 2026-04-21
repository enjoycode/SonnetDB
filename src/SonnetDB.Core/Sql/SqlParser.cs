using SonnetDB.Sql.Ast;

namespace SonnetDB.Sql;

/// <summary>
/// 递归下降 SQL 语法分析器：把 token 流转换为 <see cref="SqlStatement"/> AST。
/// </summary>
/// <remarks>
/// 支持的语句：<c>CREATE MEASUREMENT</c> / <c>INSERT INTO ... VALUES</c> /
/// <c>SELECT ... FROM ... [WHERE ...] [GROUP BY time(...)]</c> / <c>DELETE FROM ... WHERE ...</c>。
/// 不做任何语义校验（measurement / column 是否存在留给执行层）。
/// </remarks>
public sealed class SqlParser
{
    private readonly IReadOnlyList<Token> _tokens;
    private int _index;

    /// <summary>构造解析器实例。</summary>
    /// <param name="tokens">已经词法化的 token 序列（必须以 EOF 结尾）。</param>
    public SqlParser(IReadOnlyList<Token> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0 || tokens[^1].Kind != TokenKind.EndOfFile)
            throw new ArgumentException("token 序列必须以 EndOfFile 结尾。", nameof(tokens));
        _tokens = tokens;
        _index = 0;
    }

    /// <summary>
    /// 解析单条 SQL 语句（支持末尾分号）。
    /// </summary>
    /// <param name="source">SQL 源文本。</param>
    /// <returns>解析得到的语句 AST。</returns>
    /// <exception cref="SqlParseException">词法或语法错误时抛出。</exception>
    public static SqlStatement Parse(string source)
    {
        var tokens = SqlLexer.Tokenize(source);
        var parser = new SqlParser(tokens);
        var statement = parser.ParseStatement();
        parser.ConsumeOptionalSemicolon();
        parser.ExpectEndOfFile();
        return statement;
    }

    /// <summary>解析 1 ~ N 条以分号分隔的语句（末尾分号可选）。</summary>
    /// <param name="source">SQL 源文本。</param>
    /// <returns>语句列表。</returns>
    public static IReadOnlyList<SqlStatement> ParseScript(string source)
    {
        var tokens = SqlLexer.Tokenize(source);
        var parser = new SqlParser(tokens);
        var list = new List<SqlStatement>();
        while (parser.Current.Kind != TokenKind.EndOfFile)
        {
            list.Add(parser.ParseStatement());
            parser.ConsumeOptionalSemicolon();
        }
        return list;
    }

    /// <summary>解析下一条语句。</summary>
    public SqlStatement ParseStatement()
    {
        return Current.Kind switch
        {
            TokenKind.KeywordCreate => ParseCreate(),
            TokenKind.KeywordInsert => ParseInsert(),
            TokenKind.KeywordSelect => ParseSelect(),
            TokenKind.KeywordDelete => ParseDelete(),
            TokenKind.KeywordDrop => ParseDrop(),
            TokenKind.KeywordAlter => ParseAlterUser(),
            TokenKind.KeywordGrant => ParseGrant(),
            TokenKind.KeywordRevoke => ParseRevoke(),
            TokenKind.KeywordShow => ParseShow(),
            TokenKind.KeywordIssue => ParseIssue(),
            _ => throw Error("期望 CREATE / INSERT / SELECT / DELETE / DROP / ALTER / GRANT / REVOKE / SHOW / ISSUE 关键字"),
        };
    }

    // ── CREATE 分发：MEASUREMENT / USER / DATABASE ─────────────────────────

    private SqlStatement ParseCreate()
    {
        Expect(TokenKind.KeywordCreate);
        return Current.Kind switch
        {
            TokenKind.KeywordMeasurement => ParseCreateMeasurementBody(),
            TokenKind.KeywordUser => ParseCreateUserBody(),
            TokenKind.KeywordDatabase => ParseCreateDatabaseBody(),
            _ => throw Error("CREATE 后面期望 MEASUREMENT / USER / DATABASE"),
        };
    }

    // ── CREATE MEASUREMENT ─────────────────────────────────────────────────

    private CreateMeasurementStatement ParseCreateMeasurementBody()
    {
        Expect(TokenKind.KeywordMeasurement);
        var name = ExpectIdentifierName();
        Expect(TokenKind.LeftParen);

        var columns = new List<ColumnDefinition>();
        while (true)
        {
            columns.Add(ParseColumnDefinition());
            if (Current.Kind == TokenKind.Comma) { Advance(); continue; }
            break;
        }

        Expect(TokenKind.RightParen);
        return new CreateMeasurementStatement(name, columns);
    }

    private ColumnDefinition ParseColumnDefinition()
    {
        var columnName = ExpectIdentifierName();
        ColumnKind kind;
        SqlDataType dataType;
        switch (Current.Kind)
        {
            case TokenKind.KeywordTag:
                Advance();
                kind = ColumnKind.Tag;
                dataType = SqlDataType.String;
                // tag 列可选地写 STRING 类型（仅允许 STRING）
                if (Current.Kind == TokenKind.KeywordString)
                {
                    Advance();
                }
                else if (IsDataTypeKeyword(Current.Kind))
                {
                    throw Error("Tag 列只能是 STRING 类型");
                }
                break;

            case TokenKind.KeywordField:
                Advance();
                kind = ColumnKind.Field;
                dataType = ParseDataType();
                break;

            default:
                throw Error("期望 TAG 或 FIELD");
        }
        return new ColumnDefinition(columnName, kind, dataType);
    }

    private SqlDataType ParseDataType()
    {
        var token = Current;
        switch (token.Kind)
        {
            case TokenKind.KeywordFloat: Advance(); return SqlDataType.Float64;
            case TokenKind.KeywordInt: Advance(); return SqlDataType.Int64;
            case TokenKind.KeywordBool: Advance(); return SqlDataType.Boolean;
            case TokenKind.KeywordString: Advance(); return SqlDataType.String;
            default: throw Error("期望数据类型 FLOAT / INT / BOOL / STRING");
        }
    }

    private static bool IsDataTypeKeyword(TokenKind kind)
        => kind is TokenKind.KeywordFloat or TokenKind.KeywordInt
                or TokenKind.KeywordBool or TokenKind.KeywordString;

    // ── INSERT INTO ────────────────────────────────────────────────────────

    private InsertStatement ParseInsert()
    {
        Expect(TokenKind.KeywordInsert);
        Expect(TokenKind.KeywordInto);
        var measurement = ExpectIdentifierName();

        Expect(TokenKind.LeftParen);
        var columns = new List<string>();
        columns.Add(ExpectColumnName());
        while (Current.Kind == TokenKind.Comma)
        {
            Advance();
            columns.Add(ExpectColumnName());
        }
        Expect(TokenKind.RightParen);

        Expect(TokenKind.KeywordValues);

        var rows = new List<IReadOnlyList<SqlExpression>>();
        rows.Add(ParseValueRow(columns.Count));
        while (Current.Kind == TokenKind.Comma)
        {
            Advance();
            rows.Add(ParseValueRow(columns.Count));
        }

        return new InsertStatement(measurement, columns, rows);
    }

    private IReadOnlyList<SqlExpression> ParseValueRow(int expectedColumnCount)
    {
        var rowStart = Current.Position;
        Expect(TokenKind.LeftParen);
        var values = new List<SqlExpression>(expectedColumnCount);
        values.Add(ParseExpression());
        while (Current.Kind == TokenKind.Comma)
        {
            Advance();
            values.Add(ParseExpression());
        }
        Expect(TokenKind.RightParen);
        if (values.Count != expectedColumnCount)
            throw new SqlParseException(
                $"VALUES 行的列数 ({values.Count}) 与 INSERT 列列表 ({expectedColumnCount}) 不一致", rowStart);
        return values;
    }

    // ── SELECT ─────────────────────────────────────────────────────────────

    private SelectStatement ParseSelect()
    {
        Expect(TokenKind.KeywordSelect);
        var projections = ParseSelectList();
        Expect(TokenKind.KeywordFrom);

        // FROM 后允许两种形式：
        //   1) 普通 measurement 标识符
        //   2) 表值函数调用，例如 forecast(measurement, field, horizon, 'algo'[, season])
        string measurement;
        FunctionCallExpression? tvf = null;
        if (Current.Kind == TokenKind.IdentifierLiteral
            && _index + 1 < _tokens.Count
            && _tokens[_index + 1].Kind == TokenKind.LeftParen)
        {
            var name = Current.Text;
            Advance();
            var fnCall = ParseFunctionCallTail(name);
            if (fnCall is not FunctionCallExpression call || call.IsStar)
                throw Error("FROM 子句的表值函数调用非法");
            tvf = call;
            // 第一个参数必须是 source measurement 标识符
            if (call.Arguments.Count == 0 || call.Arguments[0] is not IdentifierExpression sourceId)
                throw Error($"表值函数 {name}(...) 第 1 个参数必须是 measurement 列名");
            measurement = sourceId.Name;
        }
        else
        {
            measurement = ExpectIdentifierName();
        }

        SqlExpression? where = null;
        if (Current.Kind == TokenKind.KeywordWhere)
        {
            Advance();
            where = ParseExpression();
        }

        var groupBy = Array.Empty<SqlExpression>();
        if (Current.Kind == TokenKind.KeywordGroup)
        {
            Advance();
            Expect(TokenKind.KeywordBy);
            groupBy = ParseGroupByList();
        }

        return new SelectStatement(projections, measurement, where, groupBy, tvf);
    }

    private IReadOnlyList<SelectItem> ParseSelectList()
    {
        var items = new List<SelectItem>();
        items.Add(ParseSelectItem());
        while (Current.Kind == TokenKind.Comma)
        {
            Advance();
            items.Add(ParseSelectItem());
        }
        return items;
    }

    private SelectItem ParseSelectItem()
    {
        SqlExpression expression;
        if (Current.Kind == TokenKind.Star)
        {
            Advance();
            expression = StarExpression.Instance;
        }
        else
        {
            expression = ParseExpression();
        }

        string? alias = null;
        if (Current.Kind == TokenKind.KeywordAs)
        {
            Advance();
            alias = ExpectIdentifierName();
        }
        else if (Current.Kind == TokenKind.IdentifierLiteral)
        {
            // 可选的 alias（无 AS）；只接受一个标识符（避免吞掉后续子句关键字）
            alias = Current.Text;
            Advance();
        }

        return new SelectItem(expression, alias);
    }

    private SqlExpression[] ParseGroupByList()
    {
        var items = new List<SqlExpression> { ParseGroupByExpression() };
        while (Current.Kind == TokenKind.Comma)
        {
            Advance();
            items.Add(ParseGroupByExpression());
        }

        return items.ToArray();
    }

    private SqlExpression ParseGroupByExpression()
    {
        var expression = ParseExpression();

        if (expression is FunctionCallExpression
            {
                Name: var name,
                IsStar: false,
                Arguments: [DurationLiteralExpression { Milliseconds: <= 0 }]
            }
            && string.Equals(name, "time", StringComparison.OrdinalIgnoreCase))
        {
            throw Error("GROUP BY time(...) 桶大小必须 > 0");
        }

        return expression;
    }

    // ── DELETE ─────────────────────────────────────────────────────────────

    private DeleteStatement ParseDelete()
    {
        Expect(TokenKind.KeywordDelete);
        Expect(TokenKind.KeywordFrom);
        var measurement = ExpectIdentifierName();
        Expect(TokenKind.KeywordWhere);
        var where = ParseExpression();
        return new DeleteStatement(measurement, where);
    }

    // ── 表达式（按优先级从低到高） ──────────────────────────────────────────

    /// <summary>解析单个表达式（公开供测试 / 子表达式调试使用）。</summary>
    public SqlExpression ParseExpression() => ParseOr();

    private SqlExpression ParseOr()
    {
        var left = ParseAnd();
        while (Current.Kind == TokenKind.KeywordOr)
        {
            Advance();
            var right = ParseAnd();
            left = new BinaryExpression(SqlBinaryOperator.Or, left, right);
        }
        return left;
    }

    private SqlExpression ParseAnd()
    {
        var left = ParseNot();
        while (Current.Kind == TokenKind.KeywordAnd)
        {
            Advance();
            var right = ParseNot();
            left = new BinaryExpression(SqlBinaryOperator.And, left, right);
        }
        return left;
    }

    private SqlExpression ParseNot()
    {
        if (Current.Kind == TokenKind.KeywordNot)
        {
            Advance();
            return new UnaryExpression(SqlUnaryOperator.Not, ParseNot());
        }
        return ParseComparison();
    }

    private SqlExpression ParseComparison()
    {
        var left = ParseAdditive();
        while (TryMapComparison(Current.Kind, out var op))
        {
            Advance();
            var right = ParseAdditive();
            left = new BinaryExpression(op, left, right);
        }
        return left;
    }

    private static bool TryMapComparison(TokenKind kind, out SqlBinaryOperator op)
    {
        switch (kind)
        {
            case TokenKind.Equal: op = SqlBinaryOperator.Equal; return true;
            case TokenKind.NotEqual: op = SqlBinaryOperator.NotEqual; return true;
            case TokenKind.LessThan: op = SqlBinaryOperator.LessThan; return true;
            case TokenKind.LessThanOrEqual: op = SqlBinaryOperator.LessThanOrEqual; return true;
            case TokenKind.GreaterThan: op = SqlBinaryOperator.GreaterThan; return true;
            case TokenKind.GreaterThanOrEqual: op = SqlBinaryOperator.GreaterThanOrEqual; return true;
            default: op = default; return false;
        }
    }

    private SqlExpression ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (Current.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            var op = Current.Kind == TokenKind.Plus ? SqlBinaryOperator.Add : SqlBinaryOperator.Subtract;
            Advance();
            var right = ParseMultiplicative();
            left = new BinaryExpression(op, left, right);
        }
        return left;
    }

    private SqlExpression ParseMultiplicative()
    {
        var left = ParseUnary();
        while (Current.Kind is TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
        {
            var op = Current.Kind switch
            {
                TokenKind.Star => SqlBinaryOperator.Multiply,
                TokenKind.Slash => SqlBinaryOperator.Divide,
                _ => SqlBinaryOperator.Modulo,
            };
            Advance();
            var right = ParseUnary();
            left = new BinaryExpression(op, left, right);
        }
        return left;
    }

    private SqlExpression ParseUnary()
    {
        if (Current.Kind == TokenKind.Minus)
        {
            Advance();
            return new UnaryExpression(SqlUnaryOperator.Negate, ParseUnary());
        }
        if (Current.Kind == TokenKind.Plus)
        {
            Advance();
            return ParseUnary();
        }
        return ParsePrimary();
    }

    private SqlExpression ParsePrimary()
    {
        var token = Current;
        switch (token.Kind)
        {
            case TokenKind.IntegerLiteral:
                Advance();
                return LiteralExpression.Integer(token.IntegerValue);
            case TokenKind.FloatLiteral:
                Advance();
                return LiteralExpression.Float(token.DoubleValue);
            case TokenKind.StringLiteral:
                Advance();
                return LiteralExpression.String(token.Text);
            case TokenKind.DurationLiteral:
                Advance();
                return new DurationLiteralExpression(token.IntegerValue);
            case TokenKind.KeywordNull:
                Advance();
                return LiteralExpression.Null();
            case TokenKind.KeywordTrue:
                Advance();
                return LiteralExpression.Bool(true);
            case TokenKind.KeywordFalse:
                Advance();
                return LiteralExpression.Bool(false);
            case TokenKind.LeftParen:
                Advance();
                var inner = ParseExpression();
                Expect(TokenKind.RightParen);
                return inner;
            case TokenKind.KeywordTime:
                // time 既可以作为列名（time >= 100），也可以作为函数（time(1m)）；
                // 看下一个 token 是否为 '(' 决定。
                return ParseIdentifierOrFunctionCall();
            case TokenKind.IdentifierLiteral:
                return ParseIdentifierOrFunctionCall();
            default:
                throw Error("期望表达式");
        }
    }

    private SqlExpression ParseIdentifierOrFunctionCall()
    {
        var name = Current.Text;
        Advance();
        if (Current.Kind == TokenKind.LeftParen)
        {
            return ParseFunctionCallTail(name);
        }
        return new IdentifierExpression(name);
    }

    private SqlExpression ParseFunctionCallTail(string name)
    {
        Expect(TokenKind.LeftParen);

        // fn(*) 形式
        if (Current.Kind == TokenKind.Star)
        {
            Advance();
            Expect(TokenKind.RightParen);
            return new FunctionCallExpression(name, Array.Empty<SqlExpression>(), IsStar: true);
        }

        // fn() 零参
        if (Current.Kind == TokenKind.RightParen)
        {
            Advance();
            return new FunctionCallExpression(name, Array.Empty<SqlExpression>());
        }

        var args = new List<SqlExpression>();
        args.Add(ParseExpression());
        while (Current.Kind == TokenKind.Comma)
        {
            Advance();
            args.Add(ParseExpression());
        }
        Expect(TokenKind.RightParen);
        return new FunctionCallExpression(name, args);
    }

    // ── 工具方法 ────────────────────────────────────────────────────────────

    private Token Current => _tokens[_index];

    private void Advance() => _index++;

    private void Expect(TokenKind kind)
    {
        if (Current.Kind != kind)
            throw Error($"期望 token {kind}，实际为 {Current.Kind}");
        Advance();
    }

    private string ExpectIdentifierName()
    {
        if (Current.Kind != TokenKind.IdentifierLiteral)
            throw Error("期望标识符");
        var name = Current.Text;
        Advance();
        return name;
    }

    /// <summary>
    /// 期望一个列名 token：普通标识符；或者 <see cref="TokenKind.KeywordTime"/>（保留字 <c>time</c> 在列名上下文中
    /// 视为名为 <c>"time"</c> 的列，与时间戳伪列对应）。
    /// </summary>
    private string ExpectColumnName()
    {
        switch (Current.Kind)
        {
            case TokenKind.IdentifierLiteral:
                var name = Current.Text;
                Advance();
                return name;
            case TokenKind.KeywordTime:
                Advance();
                return "time";
            default:
                throw Error("期望列名");
        }
    }

    private void ConsumeOptionalSemicolon()
    {
        if (Current.Kind == TokenKind.Semicolon)
            Advance();
    }

    private void ExpectEndOfFile()
    {
        if (Current.Kind != TokenKind.EndOfFile)
            throw Error("语句末尾存在多余内容");
    }

    // ── 控制面 DDL（PR #34a）─────────────────────────────────────────────

    /// <summary><c>CREATE USER name WITH PASSWORD 'pwd'</c>。</summary>
    private CreateUserStatement ParseCreateUserBody()
    {
        Expect(TokenKind.KeywordUser);
        var name = ExpectIdentifierName();
        Expect(TokenKind.KeywordWith);
        Expect(TokenKind.KeywordPassword);
        var password = ExpectStringLiteral();
        bool isSuperuser = false;
        if (Current.Kind == TokenKind.KeywordSuperuser)
        {
            Advance();
            isSuperuser = true;
        }
        return new CreateUserStatement(name, password, IsSuperuser: isSuperuser);
    }

    /// <summary><c>CREATE DATABASE name</c>。</summary>
    private CreateDatabaseStatement ParseCreateDatabaseBody()
    {
        Expect(TokenKind.KeywordDatabase);
        var name = ExpectIdentifierName();
        return new CreateDatabaseStatement(name);
    }

    /// <summary><c>DROP USER name</c> 或 <c>DROP DATABASE name</c>。</summary>
    private SqlStatement ParseDrop()
    {
        Expect(TokenKind.KeywordDrop);
        switch (Current.Kind)
        {
            case TokenKind.KeywordUser:
                Advance();
                return new DropUserStatement(ExpectIdentifierName());
            case TokenKind.KeywordDatabase:
                Advance();
                return new DropDatabaseStatement(ExpectIdentifierName());
            default:
                throw Error("DROP 后面期望 USER 或 DATABASE");
        }
    }

    /// <summary><c>ALTER USER name WITH PASSWORD 'pwd'</c>。</summary>
    private AlterUserPasswordStatement ParseAlterUser()
    {
        Expect(TokenKind.KeywordAlter);
        Expect(TokenKind.KeywordUser);
        var name = ExpectIdentifierName();
        Expect(TokenKind.KeywordWith);
        Expect(TokenKind.KeywordPassword);
        var password = ExpectStringLiteral();
        return new AlterUserPasswordStatement(name, password);
    }

    /// <summary><c>GRANT READ|WRITE|ADMIN ON DATABASE db TO user</c>。</summary>
    private GrantStatement ParseGrant()
    {
        Expect(TokenKind.KeywordGrant);
        var perm = Current.Kind switch
        {
            TokenKind.KeywordRead => GrantPermission.Read,
            TokenKind.KeywordWrite => GrantPermission.Write,
            TokenKind.KeywordAdmin => GrantPermission.Admin,
            _ => throw Error("GRANT 后面期望 READ / WRITE / ADMIN"),
        };
        Advance();
        Expect(TokenKind.KeywordOn);
        Expect(TokenKind.KeywordDatabase);
        var db = ExpectDatabaseNameOrStar();
        Expect(TokenKind.KeywordTo);
        var user = ExpectIdentifierName();
        return new GrantStatement(perm, db, user);
    }

    /// <summary><c>REVOKE ON DATABASE db FROM user</c> 或 <c>REVOKE TOKEN '&lt;id&gt;'</c>。</summary>
    private SqlStatement ParseRevoke()
    {
        Expect(TokenKind.KeywordRevoke);
        if (Current.Kind == TokenKind.KeywordToken)
        {
            Advance();
            var tokenId = ExpectStringLiteral();
            return new RevokeTokenStatement(tokenId);
        }
        Expect(TokenKind.KeywordOn);
        Expect(TokenKind.KeywordDatabase);
        var db = ExpectDatabaseNameOrStar();
        Expect(TokenKind.KeywordFrom);
        var user = ExpectIdentifierName();
        return new RevokeStatement(db, user);
    }

    /// <summary><c>ISSUE TOKEN FOR &lt;user&gt;</c>：为指定用户颁发一个新 token。</summary>
    private IssueTokenStatement ParseIssue()
    {
        Expect(TokenKind.KeywordIssue);
        Expect(TokenKind.KeywordToken);
        Expect(TokenKind.KeywordFor);
        var user = ExpectIdentifierName();
        return new IssueTokenStatement(user);
    }

    /// <summary>
    /// <c>SHOW USERS</c> / <c>SHOW GRANTS [FOR &lt;user&gt;]</c> / <c>SHOW DATABASES</c>。
    /// </summary>
    private SqlStatement ParseShow()
    {
        Expect(TokenKind.KeywordShow);
        switch (Current.Kind)
        {
            case TokenKind.KeywordUsers:
                Advance();
                return new ShowUsersStatement();
            case TokenKind.KeywordDatabases:
                Advance();
                return new ShowDatabasesStatement();
            case TokenKind.KeywordGrants:
                Advance();
                if (Current.Kind == TokenKind.KeywordFor)
                {
                    Advance();
                    var user = ExpectIdentifierName();
                    return new ShowGrantsStatement(user);
                }
                return new ShowGrantsStatement(null);
            case TokenKind.KeywordTokens:
                Advance();
                if (Current.Kind == TokenKind.KeywordFor)
                {
                    Advance();
                    var tu = ExpectIdentifierName();
                    return new ShowTokensStatement(tu);
                }
                return new ShowTokensStatement(null);
            default:
                throw Error("SHOW 后面期望 USERS / GRANTS / DATABASES / TOKENS");
        }
    }

    private string ExpectStringLiteral()
    {
        if (Current.Kind != TokenKind.StringLiteral)
            throw Error("期望字符串字面量");
        var value = Current.Text;
        Advance();
        return value;
    }

    /// <summary>数据库名：标识符或 <c>*</c>（通配）。</summary>
    private string ExpectDatabaseNameOrStar()
    {
        if (Current.Kind == TokenKind.Star)
        {
            Advance();
            return "*";
        }
        return ExpectIdentifierName();
    }

    private SqlParseException Error(string message)
        => new(message, Current.Position);
}
