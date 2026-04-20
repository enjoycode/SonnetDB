using System.Text.Json.Serialization;

namespace TSLite.Data.Remote;

/// <summary>
/// 提交给 <c>POST /v1/db/{db}/sql</c> 的请求体。仅包含 <c>sql</c> 字段；
/// 参数已在客户端通过 <see cref="Internal.ParameterBinder"/> 内联，避免与服务端 DTO 耦合。
/// </summary>
internal sealed class SqlRequestBody
{
    [JsonPropertyName("sql")]
    public string Sql { get; set; } = string.Empty;
}

/// <summary>
/// ndjson 第一行：列元信息。
/// </summary>
internal sealed class ResultMetaLine
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("columns")]
    public List<string> Columns { get; set; } = [];
}

/// <summary>
/// ndjson 末行：统计信息。
/// </summary>
internal sealed class ResultEndLine
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("rowCount")]
    public long RowCount { get; set; }

    [JsonPropertyName("recordsAffected")]
    public int RecordsAffected { get; set; }

    [JsonPropertyName("elapsedMilliseconds")]
    public double ElapsedMilliseconds { get; set; }
}

/// <summary>
/// 服务端在请求阶段失败时返回的 JSON 错误体。
/// </summary>
internal sealed class ServerErrorBody
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
