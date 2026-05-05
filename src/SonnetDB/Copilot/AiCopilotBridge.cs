using SonnetDB.Configuration;

namespace SonnetDB.Copilot;

/// <summary>
/// 把面向用户的 <see cref="AiOptions"/>（sonnetdb.com Cloud Token）
/// 同步到底层 <see cref="CopilotChatOptions"/> / <see cref="CopilotEmbeddingOptions"/>
/// 单例，使 <see cref="CopilotReadiness"/> 与 <see cref="OpenAICompatibleChatProvider"/>
/// 在 Web 端保存配置后立即就绪，避免 <c>503 chat.endpoint_invalid</c>。
/// </summary>
internal static class AiCopilotBridge
{
    private const string OfficialGatewayBaseUrl = "https://ai.sonnetdb.com";
    private const string OfficialEndpoint = OfficialGatewayBaseUrl + "/v1/";

    /// <summary>
    /// 把 <paramref name="ai"/> 的官方 Gateway / Cloud Token 同步进 Copilot 的 chat 选项。
    /// </summary>
    public static void Apply(AiOptions ai, CopilotChatOptions chat, CopilotEmbeddingOptions embedding)
    {
        ArgumentNullException.ThrowIfNull(ai);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(embedding);

        chat.Provider = "openai";
        chat.Endpoint = OfficialEndpoint;

        if (string.IsNullOrWhiteSpace(ai.CloudAccessToken))
        {
            chat.ApiKey = null;
            return;
        }

        chat.ApiKey = ai.CloudAccessToken;
        chat.Model = null;
        if (ai.TimeoutSeconds > 0)
            chat.TimeoutSeconds = ai.TimeoutSeconds;

        // 仅当 embedding 也是 openai 且自身字段为空时回填，避免覆盖运维显式配置的独立 embedding 服务。
        if (string.Equals(embedding.Provider, "openai", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(embedding.Endpoint))
                embedding.Endpoint = OfficialEndpoint;
            if (string.IsNullOrWhiteSpace(embedding.ApiKey))
                embedding.ApiKey = ai.CloudAccessToken;
        }
    }
}
