using SonnetDB.Configuration;

namespace SonnetDB.Copilot;

/// <summary>
/// 把面向用户的 <see cref="AiOptions"/>（国际版 / 国内版 + ApiKey + Model）
/// 同步到底层 <see cref="CopilotChatOptions"/> / <see cref="CopilotEmbeddingOptions"/>
/// 单例，使 <see cref="CopilotReadiness"/> 与 <see cref="OpenAICompatibleChatProvider"/>
/// 在 Web 端保存配置后立即就绪，避免 <c>503 chat.endpoint_invalid</c>。
/// </summary>
internal static class AiCopilotBridge
{
    /// <summary>固定服务节点 URL（与 <c>AiEndpointHandler</c> 保持一致）。</summary>
    private const string InternationalBaseUrl = "https://sonnet.vip";
    private const string DomesticBaseUrl = "https://ai.sonnetdb.com";

    /// <summary>
    /// 把 <paramref name="ai"/> 的服务节点 / Key / 模型同步进 Copilot 的 chat（与 openai 类型 embedding）选项。
    /// </summary>
    public static void Apply(AiOptions ai, CopilotChatOptions chat, CopilotEmbeddingOptions embedding)
    {
        ArgumentNullException.ThrowIfNull(ai);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(embedding);

        if (!ai.Enabled || string.IsNullOrWhiteSpace(ai.ApiKey))
        {
            // 未启用或还没配置 API Key 时，保留 appsettings 原始值（通常为空）。
            return;
        }

        var endpoint = ResolveEndpoint(ai.Provider);

        chat.Provider = "openai";
        chat.Endpoint = endpoint;
        chat.ApiKey = ai.ApiKey;
        chat.Model = string.IsNullOrWhiteSpace(ai.Model) ? "claude-sonnet-4-6" : ai.Model;
        if (ai.TimeoutSeconds > 0)
            chat.TimeoutSeconds = ai.TimeoutSeconds;

        // 仅当 embedding 也是 openai 且自身字段为空时回填，避免覆盖运维显式配置的独立 embedding 服务。
        if (string.Equals(embedding.Provider, "openai", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(embedding.Endpoint))
                embedding.Endpoint = endpoint;
            if (string.IsNullOrWhiteSpace(embedding.ApiKey))
                embedding.ApiKey = ai.ApiKey;
        }
    }

    private static string ResolveEndpoint(string provider)
    {
        var baseUrl = string.Equals(provider, "domestic", StringComparison.OrdinalIgnoreCase)
            ? DomesticBaseUrl
            : InternationalBaseUrl;
        // OpenAICompatibleChatProvider 用 new Uri(endpoint, "chat/completions") 拼接，
        // 所以末尾 '/' 必须保留，否则会丢掉 '/v1' 段。
        return baseUrl + "/v1/";
    }
}
