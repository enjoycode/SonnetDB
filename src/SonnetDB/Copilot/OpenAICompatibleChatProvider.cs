using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SonnetDB.Configuration;
using SonnetDB.Contracts;
using SonnetDB.Json;

namespace SonnetDB.Copilot;

/// <summary>
/// 基于 OpenAI-compatible 协议的 chat provider。
/// </summary>
public sealed class OpenAICompatibleChatProvider : IChatProvider
{
    private readonly CopilotChatOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;

    public OpenAICompatibleChatProvider(CopilotChatOptions options, IHttpClientFactory httpClientFactory)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
    }

    public async ValueTask<string> CompleteAsync(IReadOnlyList<AiMessage> messages, CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
            throw new ArgumentException("Chat messages cannot be empty.", nameof(messages));

        if (!CopilotReadiness.TryValidateAbsoluteUri(_options.Endpoint, out var endpoint) || endpoint is null)
            throw new InvalidOperationException("Copilot chat endpoint is not configured correctly.");

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("Copilot chat API key is missing.");

        if (string.IsNullOrWhiteSpace(_options.Model))
            throw new InvalidOperationException("Copilot chat model is missing.");

        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds > 0 ? _options.TimeoutSeconds : 60);

        var request = new OpenAiChatCompletionRequest(_options.Model, messages, Stream: false);
        var json = JsonSerializer.Serialize(request, ServerJsonContext.Default.OpenAiChatCompletionRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "chat/completions"))
        {
            Content = content,
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Copilot chat provider returned {(int)response.StatusCode}: {payload}");

        var parsed = JsonSerializer.Deserialize(payload, ServerJsonContext.Default.OpenAiChatCompletionResponse);
        var reply = parsed?.Choices.Count > 0 ? parsed.Choices[0].Message.Content : null;
        if (string.IsNullOrWhiteSpace(reply))
            throw new InvalidOperationException("Copilot chat provider returned an empty completion.");

        return reply;
    }
}
