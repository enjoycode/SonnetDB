using System.Text.Json.Serialization;

namespace TSLite.Server.Contracts;

/// <summary>
/// 单条 SQL 提交请求体。
/// </summary>
/// <param name="Sql">要执行的 SQL 文本。</param>
/// <param name="Parameters">可选命名参数集合（支持基础标量：bool/long/double/string/null）。</param>
public sealed record SqlRequest(string Sql, IReadOnlyDictionary<string, JsonElementValue>? Parameters = null);

/// <summary>
/// 批量 SQL 提交请求体。所有语句按顺序、单事务语义执行。
/// </summary>
/// <param name="Statements">SQL 语句列表。</param>
public sealed record SqlBatchRequest(IReadOnlyList<SqlRequest> Statements);

/// <summary>
/// 简化的标量参数包装。仅支持时序场景常用的几个 JSON 类型，避免在 AOT 下处理任意 <c>JsonElement</c>。
/// </summary>
/// <param name="Kind">参数类型。</param>
/// <param name="StringValue">当 <see cref="Kind"/> 为 <see cref="ScalarKind.String"/> 时使用。</param>
/// <param name="IntegerValue">当 <see cref="Kind"/> 为 <see cref="ScalarKind.Integer"/> 时使用。</param>
/// <param name="DoubleValue">当 <see cref="Kind"/> 为 <see cref="ScalarKind.Double"/> 时使用。</param>
/// <param name="BooleanValue">当 <see cref="Kind"/> 为 <see cref="ScalarKind.Boolean"/> 时使用。</param>
public sealed record JsonElementValue(
    ScalarKind Kind,
    string? StringValue = null,
    long? IntegerValue = null,
    double? DoubleValue = null,
    bool? BooleanValue = null);

/// <summary>
/// 参数标量类型枚举。
/// </summary>
public enum ScalarKind
{
    /// <summary>JSON null。</summary>
    Null = 0,
    /// <summary>JSON 字符串。</summary>
    String,
    /// <summary>整数（fits in long）。</summary>
    Integer,
    /// <summary>双精度浮点。</summary>
    Double,
    /// <summary>布尔。</summary>
    Boolean,
}

/// <summary>
/// 通用错误响应。
/// </summary>
/// <param name="Error">错误标识，例如 <c>unauthorized</c> / <c>forbidden</c> / <c>db_not_found</c> / <c>sql_error</c>。</param>
/// <param name="Message">人类可读的描述。</param>
public sealed record ErrorResponse(string Error, string Message);

/// <summary>
/// SQL 流式响应的元信息行（ndjson 第一行）。
/// </summary>
/// <param name="Type">固定为 <c>"meta"</c>。</param>
/// <param name="Columns">列名列表。</param>
public sealed record ResultMeta(string Type, IReadOnlyList<string> Columns);

/// <summary>
/// SQL 流式响应的尾部统计（ndjson 最后一行）。
/// </summary>
/// <param name="Type">固定为 <c>"end"</c>。</param>
/// <param name="RowCount">本次结果集行数。</param>
/// <param name="RecordsAffected">受影响的行数（非 SELECT 时有效；SELECT 始终为 -1）。</param>
/// <param name="ElapsedMilliseconds">服务端执行耗时（毫秒）。</param>
public sealed record ResultEnd(string Type, long RowCount, int RecordsAffected, double ElapsedMilliseconds);

/// <summary>
/// CREATE DATABASE 请求体。
/// </summary>
/// <param name="Name">数据库名（仅允许 <c>[a-zA-Z0-9_-]</c>，长度 1–64）。</param>
public sealed record CreateDatabaseRequest(string Name);

/// <summary>
/// 数据库管理操作的统一返回体。
/// </summary>
/// <param name="Database">数据库名。</param>
/// <param name="Status">操作结果（<c>"created"</c> / <c>"dropped"</c> / <c>"exists"</c>）。</param>
public sealed record DatabaseOperationResponse(string Database, string Status);

/// <summary>
/// <c>GET /v1/db</c> 列表响应。
/// </summary>
/// <param name="Databases">已注册的数据库名列表。</param>
public sealed record DatabaseListResponse(IReadOnlyList<string> Databases);

/// <summary>
/// 健康检查响应。
/// </summary>
/// <param name="Status">固定为 <c>"ok"</c>。</param>
/// <param name="Databases">已加载的数据库数量。</param>
/// <param name="UptimeSeconds">服务端运行秒数。</param>
public sealed record HealthResponse(string Status, int Databases, double UptimeSeconds);
