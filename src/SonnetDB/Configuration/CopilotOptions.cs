namespace SonnetDB.Configuration;

/// <summary>
/// Copilot 子系统配置。绑定路径：<c>"SonnetDBServer:Copilot"</c>。
/// </summary>
public sealed class CopilotOptions
{
    /// <summary>
    /// 是否启用 Copilot 子系统。默认 <c>true</c>。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Embedding provider 配置。
    /// </summary>
    public CopilotEmbeddingOptions Embedding { get; set; } = new();

    /// <summary>
    /// Chat provider 配置。
    /// </summary>
    public CopilotChatOptions Chat { get; set; } = new();
}

/// <summary>
/// Embedding provider 配置。
/// </summary>
public sealed class CopilotEmbeddingOptions
{
    /// <summary>
    /// provider 名称：<c>local</c> 或 <c>openai</c>。
    /// </summary>
    public string Provider { get; set; } = "local";

    /// <summary>
    /// 本地 ONNX 模型路径。
    /// </summary>
    public string? LocalModelPath { get; set; }

    /// <summary>
    /// OpenAI-compatible 服务端点。
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// OpenAI-compatible API Key。
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// embedding 模型名。
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 请求超时（秒）。默认 <c>60</c>。
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// Chat provider 配置。
/// </summary>
public sealed class CopilotChatOptions
{
    /// <summary>
    /// provider 名称：当前仅支持 <c>openai</c>。
    /// </summary>
    public string Provider { get; set; } = "openai";

    /// <summary>
    /// OpenAI-compatible 服务端点。
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// OpenAI-compatible API Key。
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// chat 模型名。
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 请求超时（秒）。默认 <c>60</c>。
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;
}
