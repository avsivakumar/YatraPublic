using System.Text.Json.Nodes;
using Yatra.Contracts.Messages;
using Yatra.Engine.Persistence;
using Yatra.Engine.Queues;

namespace Yatra.Engine.Api;

// Serializes acceptance so simultaneous HTTP retries cannot enqueue the same response twice.
public sealed class BrowserSubmissionGate(FileEngineStateStore? durableState = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IResult> AcceptAsync(YatraMessage message, IConversationMessageStore store,
        IInboundMessageQueue queue, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var history = await store.ReadConversationAsync(message.ConversationId, cancellationToken);
            var previous = history.FirstOrDefault(item => item.MessageId == message.MessageId);
            if (previous is not null)
            {
                if (previous.Type != message.Type || previous.InReplyTo != message.InReplyTo ||
                    !JsonNode.DeepEquals(JsonNode.Parse(previous.Payload.GetRawText()), JsonNode.Parse(message.Payload.GetRawText())))
                {
                    return Results.Conflict(new { code = "response_conflict", message = "This response identity was already used with different content.", retryable = false });
                }
                return Results.Accepted(value: new { messageId = message.MessageId, status = "accepted", replayed = true });
            }

            // The durable inbox is committed before the history projection or acknowledgement.
            var replayed = durableState is not null && await durableState.AcceptAsync(message, cancellationToken);
            await store.AppendAsync(message, CancellationToken.None);
            if (durableState is null) await queue.WriteAsync(message, CancellationToken.None);
            return Results.Accepted(value: new { messageId = message.MessageId, status = "accepted", replayed });
        }
        catch (FileEngineStateStore.SubmissionConflictException)
        {
            return Results.Conflict(new { code = "response_conflict", message = "This response identity was already used with different content.", retryable = false });
        }
        finally { _gate.Release(); }
    }
}
