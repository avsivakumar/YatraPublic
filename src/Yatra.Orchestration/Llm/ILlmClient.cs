namespace Yatra.Orchestration.Llm;

public interface ILlmClient
{
    Task<string> AskAsync(
        string conversationId,
        string userText,
        CancellationToken cancellationToken);
}
