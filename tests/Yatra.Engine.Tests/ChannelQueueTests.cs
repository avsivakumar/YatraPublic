using System.Text.Json;
using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;
using Yatra.Engine.Queues;

namespace Yatra.Engine.Tests;

public sealed class ChannelQueueTests
{
    [Fact]
    public async Task InboundQueue_PreservesOrderForSingleProducer()
    {
        var queue = new ChannelInboundMessageQueue(new YatraQueueOptions { Capacity = 10 });
        var messages = CreateMessages("first", "second", "third");

        foreach (var message in messages)
        {
            await queue.WriteAsync(message, CancellationToken.None);
        }

        var received = await ReadInboundAsync(queue, messages.Length);

        Assert.Equal(messages.Select(message => message.MessageId), received.Select(message => message.MessageId));
    }

    [Fact]
    public async Task OutboundQueue_PreservesOrderForSingleProducer()
    {
        var queue = new ChannelOutboundMessageQueue(new YatraQueueOptions { Capacity = 10 });
        var messages = CreateMessages("first", "second", "third")
            .Select(message => new RoutableMessage(message, new ReturnPath("orchestrator/sample")))
            .ToArray();

        foreach (var message in messages)
        {
            await queue.WriteAsync(message, CancellationToken.None);
        }

        var received = await ReadOutboundAsync(queue, messages.Length);

        Assert.Equal(
            messages.Select(message => message.Message.MessageId),
            received.Select(message => message.Message.MessageId));
    }

    [Fact]
    public async Task OutboundQueue_PreservesMessageAndReturnPath()
    {
        var queue = new ChannelOutboundMessageQueue(new YatraQueueOptions { Capacity = 10 });
        var returnPath = new ReturnPath("orchestrator/sample");
        var routable = new RoutableMessage(CreateMessage("form"), returnPath);

        await queue.WriteAsync(routable, CancellationToken.None);

        var received = await ReadOutboundAsync(queue, count: 1);

        Assert.Equal(routable.Message.MessageId, received[0].Message.MessageId);
        Assert.Equal(returnPath, received[0].ReturnPath);
    }

    [Fact]
    public async Task InboundQueue_ReadHonorsCancellation()
    {
        var queue = new ChannelInboundMessageQueue(new YatraQueueOptions { Capacity = 1 });
        using var cts = new CancellationTokenSource();

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in queue.ReadAllAsync(cts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task BoundedQueue_WaitsWhenFullUntilReaderConsumes()
    {
        var queue = new ChannelInboundMessageQueue(new YatraQueueOptions { Capacity = 1 });
        await queue.WriteAsync(CreateMessage("first"), CancellationToken.None);

        var blockedWrite = queue.WriteAsync(CreateMessage("second"), CancellationToken.None).AsTask();

        Assert.False(blockedWrite.IsCompleted);

        _ = await ReadInboundAsync(queue, count: 1);

        await blockedWrite.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(blockedWrite.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task WriterWaitingOnFullQueue_HonorsCancellation()
    {
        var queue = new ChannelInboundMessageQueue(new YatraQueueOptions { Capacity = 1 });
        using var cts = new CancellationTokenSource();

        await queue.WriteAsync(CreateMessage("first"), CancellationToken.None);
        var blockedWrite = queue.WriteAsync(CreateMessage("second"), cts.Token).AsTask();

        Assert.False(blockedWrite.IsCompleted);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blockedWrite);
    }

    [Fact]
    public async Task MultipleMessages_PassThroughInboundQueueAsynchronously()
    {
        var queue = new ChannelInboundMessageQueue(new YatraQueueOptions { Capacity = 2 });
        var messages = CreateMessages("one", "two", "three", "four", "five");
        var reader = ReadInboundAsync(queue, messages.Length);

        foreach (var message in messages)
        {
            await queue.WriteAsync(message, CancellationToken.None);
        }

        var received = await reader.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(messages.Length, received.Count);
        Assert.Equal(messages.Select(message => message.MessageId), received.Select(message => message.MessageId));
    }

    [Fact]
    public async Task MultipleMessages_PassThroughOutboundQueueAsynchronously()
    {
        var queue = new ChannelOutboundMessageQueue(new YatraQueueOptions { Capacity = 2 });
        var messages = CreateMessages("one", "two", "three", "four", "five")
            .Select(message => new RoutableMessage(message))
            .ToArray();
        var reader = ReadOutboundAsync(queue, messages.Length);

        foreach (var message in messages)
        {
            await queue.WriteAsync(message, CancellationToken.None);
        }

        var received = await reader.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(messages.Length, received.Count);
        Assert.Equal(
            messages.Select(message => message.Message.MessageId),
            received.Select(message => message.Message.MessageId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InboundQueue_RejectsInvalidCapacity(int capacity)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChannelInboundMessageQueue(new YatraQueueOptions { Capacity = capacity }));

        Assert.Contains("greater than zero", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void OutboundQueue_RejectsInvalidCapacity(int capacity)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChannelOutboundMessageQueue(new YatraQueueOptions { Capacity = capacity }));

        Assert.Contains("greater than zero", exception.Message);
    }

    private static async Task<List<YatraMessage>> ReadInboundAsync(
        IInboundMessageQueue queue,
        int count)
    {
        var received = new List<YatraMessage>();

        await foreach (var message in queue.ReadAllAsync(CancellationToken.None))
        {
            received.Add(message);

            if (received.Count == count)
            {
                break;
            }
        }

        return received;
    }

    private static async Task<List<RoutableMessage>> ReadOutboundAsync(
        IOutboundMessageQueue queue,
        int count)
    {
        var received = new List<RoutableMessage>();

        await foreach (var message in queue.ReadAllAsync(CancellationToken.None))
        {
            received.Add(message);

            if (received.Count == count)
            {
                break;
            }
        }

        return received;
    }

    private static YatraMessage[] CreateMessages(params string[] texts) =>
        texts.Select(CreateMessage).ToArray();

    private static YatraMessage CreateMessage(string text) =>
        new()
        {
            MessageId = YatraUlid.NewUlid(),
            ConversationId = YatraUlid.NewUlid(),
            Type = YatraMessageTypes.UserText,
            Source = "ui:current-conversation",
            Destination = "engine:inbound",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            InReplyTo = null,
            ExpectsReply = false,
            Payload = JsonSerializer.SerializeToElement(new { text }, YatraJson.SerializerOptions)
        };
}
