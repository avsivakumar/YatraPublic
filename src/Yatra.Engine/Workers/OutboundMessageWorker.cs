using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Yatra.Engine.Queues;
using Yatra.Engine.Streaming;
using Yatra.Engine.Persistence;

namespace Yatra.Engine.Workers;

public sealed class OutboundMessageWorker : BackgroundService
{
    private readonly IOutboundMessageQueue _outboundQueue;
    private readonly IConversationStreamHub _streamHub;
    private readonly ILogger<OutboundMessageWorker> _logger;
    private readonly FileEngineStateStore? _durableState;
    private readonly IConversationMessageStore? _messageStore;

    public OutboundMessageWorker(
        IOutboundMessageQueue outboundQueue,
        IConversationStreamHub streamHub,
        ILogger<OutboundMessageWorker> logger,
        FileEngineStateStore? durableState = null,
        IConversationMessageStore? messageStore = null)
    {
        _outboundQueue = outboundQueue;
        _streamHub = streamHub;
        _logger = logger;
        _durableState = durableState;
        _messageStore = messageStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var routableMessage in _outboundQueue.ReadAllAsync(stoppingToken))
        {
            try
            {
                if (_messageStore is not null) await _messageStore.AppendAsync(routableMessage.Message, stoppingToken);
                var delivered = await _streamHub.PublishAsync(routableMessage.Message, stoppingToken);
                if (!delivered)
                {
                    _logger.LogInformation(
                        "No active stream for outbound message {MessageId} in conversation {ConversationId}.",
                        routableMessage.Message.MessageId,
                        routableMessage.Message.ConversationId);
                }
                if (_durableState is not null) await _durableState.CompleteOutboundAsync(routableMessage.Message.MessageId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to deliver outbound message {MessageId} in conversation {ConversationId}.",
                    routableMessage.Message.MessageId,
                    routableMessage.Message.ConversationId);
                if (_durableState is not null) await _durableState.RetryAsync(routableMessage.Message.MessageId, inbound: false, stoppingToken);
            }
        }
    }
}
