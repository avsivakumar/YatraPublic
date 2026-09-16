using Yatra.Contracts.Messages;

namespace Yatra.Engine.Persistence;

public interface IConversationMessageStore
{
    ValueTask AppendAsync(YatraMessage message, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<YatraMessage>> ReadConversationAsync(
        string conversationId,
        CancellationToken cancellationToken);
}
