using Yatra.Contracts.Messages;

namespace Yatra.Engine.Streaming;

public interface IConversationStreamHub
{
    ConversationStreamSubscription Subscribe(string conversationId);

    ValueTask<bool> PublishAsync(YatraMessage message, CancellationToken cancellationToken);
}
