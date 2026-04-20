using System.Text.Json.Serialization;
using TSLite.Server.Contracts;

namespace TSLite.Server.Json;

/// <summary>
/// AOT-friendly <see cref="System.Text.Json"/> source-gen context。
/// 所有走 HTTP API 的请求 / 响应类型都必须出现在这里，
/// 保证 Native AOT publish 时不依赖反射。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(SqlRequest))]
[JsonSerializable(typeof(SqlBatchRequest))]
[JsonSerializable(typeof(JsonElementValue))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, JsonElementValue>))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(ResultMeta))]
[JsonSerializable(typeof(ResultEnd))]
[JsonSerializable(typeof(CreateDatabaseRequest))]
[JsonSerializable(typeof(DatabaseOperationResponse))]
[JsonSerializable(typeof(DatabaseListResponse))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(MetricsSnapshotEvent))]
[JsonSerializable(typeof(SlowQueryEvent))]
[JsonSerializable(typeof(DatabaseEvent))]
internal sealed partial class ServerJsonContext : JsonSerializerContext;
