using Yatra.Contracts.Messages;

namespace Yatra.Engine.Queues;

public interface IInboundMessageQueue
{
    ValueTask WriteAsync(YatraMessage message, CancellationToken cancellationToken);

    IAsyncEnumerable<YatraMessage> ReadAllAsync(CancellationToken cancellationToken);
}
