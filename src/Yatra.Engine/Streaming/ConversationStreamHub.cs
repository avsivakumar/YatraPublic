using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yatra.Contracts.Messages;

namespace Yatra.Engine.Streaming;

public sealed class ConversationStreamHub : IConversationStreamHub
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<YatraMessage>>> _subscriptions = new();
    private readonly IOptions<YatraEngineOptions> _options;
    private readonly ILogger<ConversationStreamHub> _logger;

    public ConversationStreamHub(
        IOptions<YatraEngineOptions> options,
        ILogger<ConversationStreamHub> logger)
    {
        _options = options;
        _logger = logger;
    }

    public ConversationStreamSubscription Subscribe(string conversationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        var subscriptionId = Guid.NewGuid();
        var channel = Channel.CreateBounded<YatraMessage>(new BoundedChannelOptions(_options.Value.StreamChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        var conversationSubscriptions = _subscriptions.GetOrAdd(
            conversationId,
            _ => new ConcurrentDictionary<Guid, Channel<YatraMessage>>());

        conversationSubscriptions[subscriptionId] = channel;

        return new ConversationStreamSubscription(
            subscriptionId,
            conversationId,
            channel,
            RemoveSubscriptionAsync);
    }

    public async ValueTask<bool> PublishAsync(YatraMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_subscriptions.TryGetValue(message.ConversationId, out var conversationSubscriptions))
        {
            return false;
        }

        var delivered = false;
        foreach (var pair in conversationSubscriptions)
        {
            if (pair.Value.Writer.TryWrite(message))
            {
                delivered = true;
                continue;
            }

            _logger.LogWarning(
                "Disconnecting slow SSE subscriber {SubscriptionId} for conversation {ConversationId}; stream channel is full.",
                pair.Key,
                message.ConversationId);
            await RemoveSubscriptionAsync(pair.Key);
            delivered = true;
        }

        return delivered;
    }

    private ValueTask RemoveSubscriptionAsync(Guid subscriptionId)
    {
        foreach (var pair in _subscriptions)
        {
            if (!pair.Value.TryRemove(subscriptionId, out var channel))
            {
                continue;
            }

            channel.Writer.TryComplete();

            if (pair.Value.IsEmpty)
            {
                _subscriptions.TryRemove(pair.Key, out _);
            }

            break;
        }

        return ValueTask.CompletedTask;
    }
}
