namespace SonnetDB.Sql;

/// <summary>
/// SQL 词法分析器产出的 token 类别。
/// </summary>
public enum TokenKind
{
    // 终止符
    EndOfFile,

    // 字面量
    IdentifierLiteral,
    IntegerLiteral,
    FloatLiteral,
    StringLiteral,
    DurationLiteral,

    // 标点
    LeftParen,
    RightParen,
    Comma,
    Semicolon,
    Star,

    // 比较 / 算术运算符
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    Plus,
    Minus,
    Slash,
    Percent,

    // 关键字
    KeywordCreate,
    KeywordMeasurement,
    KeywordInsert,
    KeywordInto,
    KeywordValues,
    KeywordSelect,
    KeywordFrom,
    KeywordWhere,
    KeywordGroup,
    KeywordBy,
    KeywordTime,
    KeywordDelete,
    KeywordAnd,
    KeywordOr,
    KeywordNot,
    KeywordAs,
    KeywordNull,
    KeywordTrue,
    KeywordFalse,
    KeywordTag,
    KeywordField,
    KeywordFloat,
    KeywordInt,
    KeywordBool,
    KeywordString,

    // PR #34a：控制面 DDL
    KeywordUser,
    KeywordPassword,
    KeywordGrant,
    KeywordRevoke,
    KeywordOn,
    KeywordTo,
    KeywordWith,
    KeywordRead,
    KeywordWrite,
    KeywordAdmin,
    KeywordDatabase,
    KeywordDrop,
    KeywordAlter,

    // PR #34b-1：SHOW 控制面查询
    KeywordShow,
    KeywordUsers,
    KeywordGrants,
    KeywordDatabases,
    KeywordFor,

    // PR #34b-3：CREATE USER ... SUPERUSER
    KeywordSuperuser,

    // PR #34b-3-tokens：API token 管理（SHOW TOKENS / ISSUE TOKEN / REVOKE TOKEN）
    KeywordTokens,
    KeywordToken,
    KeywordIssue,
}
