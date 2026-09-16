using System.Threading.Channels;
using Yatra.Contracts.Messages;

namespace Yatra.Engine.Queues;

public sealed class ChannelInboundMessageQueue : IInboundMessageQueue
{
    private readonly Channel<YatraMessage> _channel;

    public ChannelInboundMessageQueue(YatraQueueOptions? options = null)
    {
        var queueOptions = options ?? new YatraQueueOptions();
        ValidateOptions(queueOptions);
        _channel = Channel.CreateBounded<YatraMessage>(CreateOptions(queueOptions));
    }

    public ValueTask WriteAsync(YatraMessage message, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(message, cancellationToken);

    public IAsyncEnumerable<YatraMessage> ReadAllAsync(CancellationToken cancellationToken) =>
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
