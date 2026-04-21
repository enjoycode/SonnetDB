namespace SonnetDB.Contracts;

/// <summary>AI 助手配置响应（不含 ApiKey 明文）。</summary>
public sealed record AiConfigResponse(
    bool Enabled,
    string Provider,
    string Model,
    int TimeoutSeconds,
    bool HasApiKey);

/// <summary>AI 助手配置写入请求；ApiKey 为 null 时保留原密钥。</summary>
public sealed record AiConfigRequest(
    bool Enabled,
    string Provider,
    string? ApiKey,
    string Model,
    int TimeoutSeconds);

/// <summary>前端发送的 AI 聊天请求。</summary>
public sealed record AiChatRequest(
    List<AiMessage> Messages,
    string? Db = null,
    string Mode = "chat");

/// <summary>AI 消息（与 OpenAI 格式对齐）。</summary>
public sealed record AiMessage(string Role, string Content);

/// <summary>AI 启用状态（任何已认证用户可读）。</summary>
public sealed record AiStatusResponse(bool Enabled, string Provider, string Model);

/// <summary>SSE 流式 token 事件（内部 SSE 数据格式）。</summary>
internal sealed record SseTokenEvent(string Token);

/// <summary>SSE 流式错误事件。</summary>
internal sealed record SseErrorEvent(string Error);
