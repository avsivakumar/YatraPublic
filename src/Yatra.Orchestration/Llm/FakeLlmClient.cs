namespace Yatra.Orchestration.Llm;

public sealed class FakeLlmClient : ILlmClient
{
    public Task<string> AskAsync(
        string conversationId,
        string userText,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult($"Fake LLM response for conversation {conversationId}: {userText}");
    }
}
