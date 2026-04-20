using System.Globalization;
using System.Text;

namespace TSLite.Sql;

/// <summary>
/// 单遍 SQL 词法分析器：把源文本扫描成 <see cref="Token"/> 序列。
/// 关键字大小写不敏感；标识符保留原始大小写（双引号引用的标识符按字面保留）。
/// </summary>
public sealed class SqlLexer
{
    private static readonly Dictionary<string, TokenKind> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["create"] = TokenKind.KeywordCreate,
        ["measurement"] = TokenKind.KeywordMeasurement,
        ["insert"] = TokenKind.KeywordInsert,
        ["into"] = TokenKind.KeywordInto,
        ["values"] = TokenKind.KeywordValues,
        ["select"] = TokenKind.KeywordSelect,
        ["from"] = TokenKind.KeywordFrom,
        ["where"] = TokenKind.KeywordWhere,
        ["group"] = TokenKind.KeywordGroup,
        ["by"] = TokenKind.KeywordBy,
        ["time"] = TokenKind.KeywordTime,
        ["delete"] = TokenKind.KeywordDelete,
        ["and"] = TokenKind.KeywordAnd,
        ["or"] = TokenKind.KeywordOr,
        ["not"] = TokenKind.KeywordNot,
        ["as"] = TokenKind.KeywordAs,
        ["null"] = TokenKind.KeywordNull,
        ["true"] = TokenKind.KeywordTrue,
        ["false"] = TokenKind.KeywordFalse,
        ["tag"] = TokenKind.KeywordTag,
        ["field"] = TokenKind.KeywordField,
        ["float"] = TokenKind.KeywordFloat,
        ["int"] = TokenKind.KeywordInt,
        ["bool"] = TokenKind.KeywordBool,
        ["string"] = TokenKind.KeywordString,

        // PR #34a：控制面 DDL 关键字
        ["user"] = TokenKind.KeywordUser,
        ["password"] = TokenKind.KeywordPassword,
        ["grant"] = TokenKind.KeywordGrant,
        ["revoke"] = TokenKind.KeywordRevoke,
        ["on"] = TokenKind.KeywordOn,
        ["to"] = TokenKind.KeywordTo,
        ["with"] = TokenKind.KeywordWith,
        ["read"] = TokenKind.KeywordRead,
        ["write"] = TokenKind.KeywordWrite,
        ["admin"] = TokenKind.KeywordAdmin,
        ["database"] = TokenKind.KeywordDatabase,
        ["drop"] = TokenKind.KeywordDrop,
        ["alter"] = TokenKind.KeywordAlter,

        // PR #34b-1：SHOW 控制面查询
        ["show"] = TokenKind.KeywordShow,
        ["users"] = TokenKind.KeywordUsers,
        ["grants"] = TokenKind.KeywordGrants,
        ["databases"] = TokenKind.KeywordDatabases,
        ["for"] = TokenKind.KeywordFor,
        // PR #34b-3：CREATE USER ... SUPERUSER
        ["superuser"] = TokenKind.KeywordSuperuser,
    };

    private readonly string _source;
    private int _position;

    /// <summary>构造一个新的词法分析器。</summary>
    /// <param name="source">SQL 源文本。</param>
    public SqlLexer(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _position = 0;
    }

    /// <summary>
    /// 一次性把源文本完整 token 化（最后一个 token 总是 <see cref="TokenKind.EndOfFile"/>）。
    /// </summary>
    /// <param name="source">SQL 源文本。</param>
    /// <returns>token 列表，结尾包含 EOF。</returns>
    public static IReadOnlyList<Token> Tokenize(string source)
    {
        var lexer = new SqlLexer(source);
        var list = new List<Token>(32);
        while (true)
        {
            var token = lexer.NextToken();
            list.Add(token);
            if (token.Kind == TokenKind.EndOfFile) break;
        }
        return list;
    }

    /// <summary>读取下一个 token；到达末尾时持续返回 EOF。</summary>
    public Token NextToken()
    {
        SkipWhitespaceAndComments();

        if (_position >= _source.Length)
            return new Token(TokenKind.EndOfFile, string.Empty, _position);

        var start = _position;
        var ch = _source[_position];

        // 标识符 / 关键字
        if (IsIdentifierStart(ch))
            return ScanIdentifierOrKeyword(start);

        // 数字（含 duration 后缀）
        if (char.IsAsciiDigit(ch))
            return ScanNumber(start);

        // 字符串字面量（单引号；引号内 '' 表示一个 '）
        if (ch == '\'')
            return ScanString(start);

        // 双引号引用的标识符
        if (ch == '"')
            return ScanQuotedIdentifier(start);

        // 标点 / 运算符
        switch (ch)
        {
            case '(': _position++; return new Token(TokenKind.LeftParen, "(", start);
            case ')': _position++; return new Token(TokenKind.RightParen, ")", start);
            case ',': _position++; return new Token(TokenKind.Comma, ",", start);
            case ';': _position++; return new Token(TokenKind.Semicolon, ";", start);
            case '*': _position++; return new Token(TokenKind.Star, "*", start);
            case '+': _position++; return new Token(TokenKind.Plus, "+", start);
            case '-': _position++; return new Token(TokenKind.Minus, "-", start);
            case '/': _position++; return new Token(TokenKind.Slash, "/", start);
            case '%': _position++; return new Token(TokenKind.Percent, "%", start);
            case '=': _position++; return new Token(TokenKind.Equal, "=", start);
            case '!':
                if (Peek(1) == '=') { _position += 2; return new Token(TokenKind.NotEqual, "!=", start); }
                throw new SqlParseException("无法识别的字符 '!'", start);
            case '<':
                if (Peek(1) == '=') { _position += 2; return new Token(TokenKind.LessThanOrEqual, "<=", start); }
                if (Peek(1) == '>') { _position += 2; return new Token(TokenKind.NotEqual, "<>", start); }
                _position++;
                return new Token(TokenKind.LessThan, "<", start);
            case '>':
                if (Peek(1) == '=') { _position += 2; return new Token(TokenKind.GreaterThanOrEqual, ">=", start); }
                _position++;
                return new Token(TokenKind.GreaterThan, ">", start);
        }

        throw new SqlParseException($"无法识别的字符 '{ch}'", start);
    }

    // ── 私有扫描例程 ─────────────────────────────────────────────────────────

    private Token ScanIdentifierOrKeyword(int start)
    {
        while (_position < _source.Length && IsIdentifierContinue(_source[_position]))
            _position++;

        var text = _source.Substring(start, _position - start);
        return Keywords.TryGetValue(text, out var keyword)
            ? new Token(keyword, text, start)
            : new Token(TokenKind.IdentifierLiteral, text, start);
    }

    private Token ScanQuotedIdentifier(int start)
    {
        _position++; // 跳过开引号
        var sb = new StringBuilder();
        while (_position < _source.Length)
        {
            var ch = _source[_position];
            if (ch == '"')
            {
                if (Peek(1) == '"')
                {
                    sb.Append('"');
                    _position += 2;
                    continue;
                }
                _position++;
                return new Token(TokenKind.IdentifierLiteral, sb.ToString(), start);
            }
            sb.Append(ch);
            _position++;
        }
        throw new SqlParseException("未闭合的引号标识符", start);
    }

    private Token ScanString(int start)
    {
        _position++; // 跳过开引号
        var sb = new StringBuilder();
        while (_position < _source.Length)
        {
            var ch = _source[_position];
            if (ch == '\'')
            {
                if (Peek(1) == '\'')
                {
                    sb.Append('\'');
                    _position += 2;
                    continue;
                }
                _position++;
                return new Token(TokenKind.StringLiteral, sb.ToString(), start);
            }
            sb.Append(ch);
            _position++;
        }
        throw new SqlParseException("未闭合的字符串字面量", start);
    }

    private Token ScanNumber(int start)
    {
        while (_position < _source.Length && char.IsAsciiDigit(_source[_position]))
            _position++;

        var isFloat = false;

        // 小数部分
        if (_position < _source.Length && _source[_position] == '.' && char.IsAsciiDigit(Peek(1)))
        {
            isFloat = true;
            _position++; // '.'
            while (_position < _source.Length && char.IsAsciiDigit(_source[_position]))
                _position++;
        }

        // 指数部分
        if (_position < _source.Length && (_source[_position] == 'e' || _source[_position] == 'E'))
        {
            isFloat = true;
            _position++;
            if (_position < _source.Length && (_source[_position] == '+' || _source[_position] == '-'))
                _position++;
            if (_position >= _source.Length || !char.IsAsciiDigit(_source[_position]))
                throw new SqlParseException("浮点数指数缺少数字", start);
            while (_position < _source.Length && char.IsAsciiDigit(_source[_position]))
                _position++;
        }

        var numericText = _source.Substring(start, _position - start);

        // duration 后缀：ns / us / ms / s / m / h / d；只对整数生效
        if (!isFloat && _position < _source.Length && IsDurationSuffixStart(_source[_position]))
        {
            var suffixStart = _position;
            // 最长匹配两字符（ns/us/ms）；其余为单字符（s/m/h/d）
            string suffix;
            if (_position + 1 < _source.Length && IsDurationTwoCharSuffix(_source, _position))
            {
                suffix = _source.Substring(_position, 2);
                _position += 2;
            }
            else
            {
                suffix = _source[_position].ToString();
                _position++;
            }

            // 后缀后必须是非标识符续字符（避免误把 "1day" 这类拆错）
            if (_position < _source.Length && IsIdentifierContinue(_source[_position]))
                throw new SqlParseException($"无效的 duration 后缀 '{suffix}{_source[_position]}'", suffixStart);

            if (!long.TryParse(numericText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawValue) || rawValue < 0)
                throw new SqlParseException($"非法的 duration 数值 '{numericText}'", start);

            var ms = ConvertToMilliseconds(rawValue, suffix, suffixStart);
            return new Token(TokenKind.DurationLiteral, numericText, start, IntegerValue: ms);
        }

        if (isFloat)
        {
            var floatValue = double.Parse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture);
            return new Token(TokenKind.FloatLiteral, numericText, start, DoubleValue: floatValue);
        }

        if (!long.TryParse(numericText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            throw new SqlParseException($"整数字面量超出 Int64 范围 '{numericText}'", start);

        return new Token(TokenKind.IntegerLiteral, numericText, start, IntegerValue: intValue);
    }

    private static long ConvertToMilliseconds(long value, string suffix, int position)
    {
        // 用 checked 保证溢出抛 OverflowException → 转换成 SqlParseException
        try
        {
            return suffix switch
            {
                "ns" => checked(value / 1_000_000L),
                "us" => checked(value / 1_000L),
                "ms" => value,
                "s" => checked(value * 1_000L),
                "m" => checked(value * 60_000L),
                "h" => checked(value * 3_600_000L),
                "d" => checked(value * 86_400_000L),
                _ => throw new SqlParseException($"未知的 duration 单位 '{suffix}'", position),
            };
        }
        catch (OverflowException)
        {
            throw new SqlParseException($"duration 计算溢出（{value}{suffix}）", position);
        }
    }

    private void SkipWhitespaceAndComments()
    {
        while (_position < _source.Length)
        {
            var ch = _source[_position];
            if (char.IsWhiteSpace(ch))
            {
                _position++;
                continue;
            }
            // 行注释 -- ...
            if (ch == '-' && Peek(1) == '-')
            {
                _position += 2;
                while (_position < _source.Length && _source[_position] != '\n')
                    _position++;
                continue;
            }
            // 块注释 /* ... */
            if (ch == '/' && Peek(1) == '*')
            {
                var commentStart = _position;
                _position += 2;
                while (_position < _source.Length && !(_source[_position] == '*' && Peek(1) == '/'))
                    _position++;
                if (_position >= _source.Length)
                    throw new SqlParseException("未闭合的块注释", commentStart);
                _position += 2;
                continue;
            }
            return;
        }
    }

    private char Peek(int offset)
    {
        var index = _position + offset;
        return index < _source.Length ? _source[index] : '\0';
    }

    private static bool IsIdentifierStart(char ch) => ch == '_' || char.IsLetter(ch);
    private static bool IsIdentifierContinue(char ch) => ch == '_' || char.IsLetterOrDigit(ch);

    private static bool IsDurationSuffixStart(char ch)
        => ch is 'n' or 'u' or 'm' or 's' or 'h' or 'd';

    private static bool IsDurationTwoCharSuffix(string s, int index)
    {
        var a = s[index];
        var b = s[index + 1];
        // 两字符单位：ns / us / ms
        return (a == 'n' && b == 's') || (a == 'u' && b == 's') || (a == 'm' && b == 's');
    }
}
