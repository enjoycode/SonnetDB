using System.Text.Json.Serialization;
using SonnetDB.Contracts;
using SonnetDB.Copilot;
using SonnetDB.Mcp;

namespace SonnetDB.Json;

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
[JsonSerializable(typeof(SetupStatusResponse))]
[JsonSerializable(typeof(SetupInitializeRequest))]
[JsonSerializable(typeof(SetupInitializeResponse))]
[JsonSerializable(typeof(BulkIngestResponse))]
[JsonSerializable(typeof(MetricsSnapshotEvent))]
[JsonSerializable(typeof(SlowQueryEvent))]
[JsonSerializable(typeof(DatabaseEvent))]
[JsonSerializable(typeof(McpDatabaseStatsResult))]
[JsonSerializable(typeof(McpDatabaseListResult))]
[JsonSerializable(typeof(McpMeasurementColumnResult))]
[JsonSerializable(typeof(McpMeasurementListResult))]
[JsonSerializable(typeof(McpMeasurementSchemaResult))]
[JsonSerializable(typeof(McpSqlQueryResult))]
[JsonSerializable(typeof(McpSampleRowsResult))]
[JsonSerializable(typeof(McpExplainSqlResult))]
[JsonSerializable(typeof(McpDocsSearchHit))]
[JsonSerializable(typeof(McpDocsSearchResult))]
[JsonSerializable(typeof(McpSkillSearchHit))]
[JsonSerializable(typeof(McpSkillSearchResult))]
[JsonSerializable(typeof(McpSkillLoadResult))]
// ---- Schema API ----
[JsonSerializable(typeof(SchemaResponse))]
[JsonSerializable(typeof(MeasurementInfo))]
[JsonSerializable(typeof(ColumnInfo))]
[JsonSerializable(typeof(List<MeasurementInfo>))]
[JsonSerializable(typeof(List<ColumnInfo>))]
// ---- AI 公开 API ----
[JsonSerializable(typeof(AiConfigResponse))]
[JsonSerializable(typeof(AiConfigRequest))]
[JsonSerializable(typeof(AiChatRequest))]
[JsonSerializable(typeof(AiMessage))]
[JsonSerializable(typeof(AiStatusResponse))]
[JsonSerializable(typeof(List<AiMessage>))]
// ---- AI SSE 内部事件 ----
[JsonSerializable(typeof(SseTokenEvent))]
[JsonSerializable(typeof(SseErrorEvent))]
// ---- OpenAI-compatible 代理内部协议 ----
[JsonSerializable(typeof(OpenAiRequest))]
[JsonSerializable(typeof(OpenAiChunk))]
[JsonSerializable(typeof(OpenAiChoice))]
[JsonSerializable(typeof(OpenAiDelta))]
[JsonSerializable(typeof(List<OpenAiChoice>))]
// ---- Copilot OpenAI-compatible 内部协议 ----
[JsonSerializable(typeof(OpenAiEmbeddingRequest))]
[JsonSerializable(typeof(OpenAiEmbeddingResponse))]
[JsonSerializable(typeof(OpenAiEmbeddingItem))]
[JsonSerializable(typeof(OpenAiChatCompletionRequest))]
[JsonSerializable(typeof(OpenAiChatCompletionResponse))]
[JsonSerializable(typeof(OpenAiChatCompletionChoice))]
[JsonSerializable(typeof(OpenAiChatCompletionMessage))]
// ---- Copilot 公开 API（PR #64）----
[JsonSerializable(typeof(CopilotIngestRequest))]
[JsonSerializable(typeof(CopilotIngestResponse))]
[JsonSerializable(typeof(CopilotSearchRequest))]
[JsonSerializable(typeof(CopilotSearchResponse))]
[JsonSerializable(typeof(CopilotSearchHit))]
[JsonSerializable(typeof(List<CopilotSearchHit>))]
// ---- Copilot 技能库 API（PR #65）----
[JsonSerializable(typeof(CopilotSkillsIngestRequest))]
[JsonSerializable(typeof(CopilotSkillsIngestResponse))]
[JsonSerializable(typeof(CopilotSkillsSearchRequest))]
[JsonSerializable(typeof(CopilotSkillsSearchResponse))]
[JsonSerializable(typeof(CopilotSkillsSearchHit))]
[JsonSerializable(typeof(List<CopilotSkillsSearchHit>))]
[JsonSerializable(typeof(CopilotSkillLoadResponse))]
[JsonSerializable(typeof(CopilotSkillsListResponse))]
[JsonSerializable(typeof(CopilotChatRequest))]
[JsonSerializable(typeof(CopilotChatEvent))]
[JsonSerializable(typeof(CopilotCitation))]
[JsonSerializable(typeof(List<CopilotCitation>))]
// ---- Copilot Agent 内部协议（PR #67）----
[JsonSerializable(typeof(CopilotToolPlan))]
[JsonSerializable(typeof(CopilotPlannedTool))]
internal sealed partial class ServerJsonContext : JsonSerializerContext;
