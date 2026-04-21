namespace SonnetDB.Configuration;

/// <summary>
/// AI 助手配置。支持两个固定服务端点（国际版 / 国内版），均为 OpenAI-compatible 协议。
/// 运行时通过 <see cref="SonnetDB.Auth.AiConfigStore"/> 读写持久化副本（<c>.system/ai-config.json</c>）。
/// </summary>
public sealed class AiOptions
{
    /// <summary>是否启用 AI 助手功能。默认 <c>false</c>。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 服务商选择。
    /// <c>"international"</c>（默认，https://sonnet.vip/）或
    /// <c>"domestic"</c>（国内，https://ai.sonnetdb.com/）。
    /// </summary>
    public string Provider { get; set; } = "international";

    /// <summary>API Key（向 SonnetDB 团队申请）。</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>模型名称。默认 <c>claude-sonnet-4-6</c>。</summary>
    public string Model { get; set; } = "claude-sonnet-4-6";

    /// <summary>请求超时（秒）。默认 <c>60</c>。</summary>
    public int TimeoutSeconds { get; set; } = 60;
}
