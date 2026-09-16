using Yatra.Contracts.Routing;

namespace Yatra.Engine.Queues;

public interface IOutboundMessageQueue
{
    ValueTask WriteAsync(RoutableMessage message, CancellationToken cancellationToken);

    IAsyncEnumerable<RoutableMessage> ReadAllAsync(CancellationToken cancellationToken);
}
