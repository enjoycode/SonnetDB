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

    /// <summary>
    /// 文档摄入 / 检索配置。
    /// </summary>
    public CopilotDocsOptions Docs { get; set; } = new();
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

/// <summary>
/// 文档摄入 / 检索配置。
/// </summary>
public sealed class CopilotDocsOptions
{
    /// <summary>
    /// 服务端启动后是否自动执行一次后台增量摄入。默认 <c>true</c>。
    /// </summary>
    public bool AutoIngestOnStartup { get; set; } = true;

    /// <summary>
    /// 文档根目录列表。默认优先扫描仓库源码文档 <c>./docs</c>，其次兼容 <c>./web/help</c> 与运行时生成目录。
    /// </summary>
    public List<string> Roots { get; set; } =
    [
        "./docs",
        "./web/help",
        "./src/SonnetDB/wwwroot/help",
    ];

    /// <summary>
    /// 单块最大字符数。默认 <c>800</c>。
    /// </summary>
    public int ChunkSize { get; set; } = 800;

    /// <summary>
    /// 相邻块重叠字符数。默认 <c>100</c>。
    /// </summary>
    public int ChunkOverlap { get; set; } = 100;
}
