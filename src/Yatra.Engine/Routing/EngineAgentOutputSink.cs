using Microsoft.Extensions.Options;
using System.Text.Json;
using Yatra.AgentIntegration;
using Yatra.Contracts.Forms;
using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;
using Yatra.Engine.Persistence;
using Yatra.Engine.Queues;

namespace Yatra.Engine.Routing;

public sealed class EngineAgentOutputSink : IEngineAgentOutputSink
{
    private readonly IOutboundMessageQueue _outboundQueue;
    private readonly IPendingReplyRegistry _pendingReplyRegistry;
    private readonly IOptions<YatraEngineOptions> _options;
    private readonly IConversationMessageStore? _messageStore;
    private readonly FileEngineStateStore? _durableState;

    public EngineAgentOutputSink(
        IOutboundMessageQueue outboundQueue,
        IPendingReplyRegistry pendingReplyRegistry,
        IOptions<YatraEngineOptions> options,
        IConversationMessageStore? messageStore = null,
        FileEngineStateStore? durableState = null)
    {
        _outboundQueue = outboundQueue;
        _pendingReplyRegistry = pendingReplyRegistry;
        _options = options;
        _messageStore = messageStore;
        _durableState = durableState;
    }

    public async ValueTask PublishAsync(AgentOutput output, CancellationToken cancellationToken)
    {
        if (_durableState is not null)
        {
            await _durableState.PublishAsync(output, cancellationToken);
            return;
        }
        if (output.Message.ExpectsReply)
        {
            if (output.ReturnPath is null)
            {
                throw new InvalidOperationException("A reply-expected message must include an internal return path.");
            }

            var (expectedFormId, expectedFormVersion, validatorKey) = GetReplyMetadata(output);

            await _pendingReplyRegistry.RegisterAsync(
                new PendingReply(
                    output.Message.MessageId,
                    output.Message.ConversationId,
                    output.ReturnPath,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddMinutes(_options.Value.PendingReplyLifetimeMinutes),
                    PendingReplyStatus.Pending,
                    expectedFormId,
                    expectedFormVersion,
                    validatorKey),
                cancellationToken);
        }

        if (_messageStore is not null)
        {
            await _messageStore.AppendAsync(output.Message, cancellationToken);
        }

        await _outboundQueue.WriteAsync(new RoutableMessage(output.Message, output.ReturnPath), cancellationToken);
    }

    private static (string? ExpectedFormId, string? ExpectedFormVersion, string? ValidatorKey) GetReplyMetadata(
        AgentOutput output)
    {
        if (output.Message.Type != YatraMessageTypes.FormRequest)
        {
            return (null, null, null);
        }

        var payload = output.Message.Payload.Deserialize<FormRequestPayload>(YatraJson.SerializerOptions);
        return payload is null
            ? (null, null, null)
            : (payload.FormId, payload.FormVersion, payload.FormId);
    }
}
