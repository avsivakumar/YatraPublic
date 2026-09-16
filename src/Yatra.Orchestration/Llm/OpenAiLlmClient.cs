using OpenAI.Chat;

namespace Yatra.Orchestration.Llm;

public sealed class OpenAiLlmClient : ILlmClient
{
    private readonly OrchestrationOptions _options;
    private readonly string? _apiKey;

    public OpenAiLlmClient(
        OrchestrationOptions options,
        string apiKey)
    {
        _options = options;
        _apiKey = apiKey;
    }

    public async Task<string> AskAsync(
        string conversationId,
        string userText,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException(
                "Set AiApiKey in SampleOrchestrationAgentAdapter for local AI work.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        var client = new ChatClient(_options.Model, _apiKey);
        var completion = await client.CompleteChatAsync(
            [
                new SystemChatMessage("Answer briefly and plainly for the Yatra sample application."),
                new UserChatMessage(userText)
            ],
            cancellationToken: timeout.Token);

        return completion.Value.Content.Count > 0
            ? completion.Value.Content[0].Text
            : string.Empty;
    }

}
