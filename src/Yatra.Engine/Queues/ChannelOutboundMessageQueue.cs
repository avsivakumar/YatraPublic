using System.Threading.Channels;
using Yatra.Contracts.Routing;

namespace Yatra.Engine.Queues;

public sealed class ChannelOutboundMessageQueue : IOutboundMessageQueue
{
    private readonly Channel<RoutableMessage> _channel;

    public ChannelOutboundMessageQueue(YatraQueueOptions? options = null)
    {
        var queueOptions = options ?? new YatraQueueOptions();
        ValidateOptions(queueOptions);
        _channel = Channel.CreateBounded<RoutableMessage>(CreateOptions(queueOptions));
    }

    public ValueTask WriteAsync(RoutableMessage message, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(message, cancellationToken);

    public IAsyncEnumerable<RoutableMessage> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    private static BoundedChannelOptions CreateOptions(YatraQueueOptions options) =>
        new(options.Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };

    private static void ValidateOptions(YatraQueueOptions options)
    {
        if (options.Capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Capacity,
                "Queue capacity must be greater than zero.");
        }
    }
}
