using System.Threading.Channels;
using Yatra.Contracts.Messages;

namespace Yatra.Engine.Streaming;

public sealed class ConversationStreamSubscription : IAsyncDisposable
{
    private readonly Func<Guid, ValueTask> _dispose;

    internal ConversationStreamSubscription(
        Guid subscriptionId,
        string conversationId,
        Channel<YatraMessage> channel,
        Func<Guid, ValueTask> dispose)
    {
        SubscriptionId = subscriptionId;
        ConversationId = conversationId;
        Channel = channel;
        _dispose = dispose;
    }

    public Guid SubscriptionId { get; }

    public string ConversationId { get; }

    internal Channel<YatraMessage> Channel { get; }

    public ChannelReader<YatraMessage> Reader => Channel.Reader;

    public ValueTask DisposeAsync() => _dispose(SubscriptionId);
}
