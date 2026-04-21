namespace SonnetDB.Sql.Ast;

/// <summary>列在 measurement schema 中的角色。</summary>
public enum ColumnKind
{
    /// <summary>Tag 列：参与 SeriesKey 规范化、可作为索引维度。</summary>
    Tag,
    /// <summary>Field 列：实际承载时间序列值。</summary>
    Field,
}

/// <summary>SQL 层支持的列数据类型，对应 <see cref="SonnetDB.Storage.Format.FieldType"/>。</summary>
public enum SqlDataType
{
    /// <summary>64 位双精度浮点。</summary>
    Float64,
    /// <summary>64 位有符号整数。</summary>
    Int64,
    /// <summary>布尔值。</summary>
    Boolean,
    /// <summary>字符串。</summary>
    String,
    /// <summary>定长 32 位浮点向量；维度由 <c>ColumnDefinition.VectorDimension</c> 声明（PR #58 b）。</summary>
    Vector,
}

/// <summary>SQL 层支持的二元运算符。</summary>
public enum SqlBinaryOperator
{
    /// <summary>逻辑或。</summary>
    Or,
    /// <summary>逻辑与。</summary>
    And,
    /// <summary>等于。</summary>
    Equal,
    /// <summary>不等于。</summary>
    NotEqual,
    /// <summary>小于。</summary>
    LessThan,
    /// <summary>小于等于。</summary>
    LessThanOrEqual,
    /// <summary>大于。</summary>
    GreaterThan,
    /// <summary>大于等于。</summary>
    GreaterThanOrEqual,
    /// <summary>加。</summary>
    Add,
    /// <summary>减。</summary>
    Subtract,
    /// <summary>乘。</summary>
    Multiply,
    /// <summary>除。</summary>
    Divide,
    /// <summary>取模。</summary>
    Modulo,
}

/// <summary>SQL 层支持的一元运算符。</summary>
public enum SqlUnaryOperator
{
    /// <summary>逻辑非。</summary>
    Not,
    /// <summary>负号。</summary>
    Negate,
}
