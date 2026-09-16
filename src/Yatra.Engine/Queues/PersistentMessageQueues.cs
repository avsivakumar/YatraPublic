using Yatra.AgentIntegration;
using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;
using Yatra.Engine.Persistence;

namespace Yatra.Engine.Queues;

public sealed class PersistentInboundMessageQueue(FileEngineStateStore state) : IInboundMessageQueue
{
    public async ValueTask WriteAsync(YatraMessage message, CancellationToken cancellationToken) =>
        await state.AcceptAsync(message, cancellationToken);
    public IAsyncEnumerable<YatraMessage> ReadAllAsync(CancellationToken cancellationToken) => state.ReadInboundAsync(cancellationToken);
}

public sealed class PersistentOutboundMessageQueue(FileEngineStateStore state) : IOutboundMessageQueue
{
    public async ValueTask WriteAsync(RoutableMessage message, CancellationToken cancellationToken) =>
        await state.PublishAsync(new AgentOutput(message.Message, message.ReturnPath), cancellationToken);
    public IAsyncEnumerable<RoutableMessage> ReadAllAsync(CancellationToken cancellationToken) => state.ReadOutboundAsync(cancellationToken);
}
