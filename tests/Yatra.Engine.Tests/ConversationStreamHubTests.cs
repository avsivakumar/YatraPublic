using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;
using Yatra.Engine.Streaming;

namespace Yatra.Engine.Tests;

public sealed class ConversationStreamHubTests
{
    [Fact]
    public async Task PublishAsync_ReturnsFalseWithoutActiveStream()
    {
        var hub = CreateHub();

        var delivered = await hub.PublishAsync(CreateMessage(YatraUlid.NewUlid()), CancellationToken.None);

        Assert.False(delivered);
    }

    [Fact]
    public async Task PublishAsync_IsolatesConversations()
    {
        var hub = CreateHub();
        var conversationA = YatraUlid.NewUlid();
        var conversationB = YatraUlid.NewUlid();
        await using var subscriptionA = hub.Subscribe(conversationA);
        await using var subscriptionB = hub.Subscribe(conversationB);
        var messageA = CreateMessage(conversationA);

        Assert.True(await hub.PublishAsync(messageA, CancellationToken.None));

        Assert.True(subscriptionA.Reader.TryRead(out var receivedA));
        Assert.Equal(messageA.MessageId, receivedA.MessageId);
        Assert.False(subscriptionB.Reader.TryRead(out _));
    }

    [Fact]
    public async Task PublishAsync_DeliversToMultipleSubscriptionsInSameConversation()
    {
        var hub = CreateHub();
        var conversationId = YatraUlid.NewUlid();
        await using var first = hub.Subscribe(conversationId);
        await using var second = hub.Subscribe(conversationId);
        var message = CreateMessage(conversationId);

        Assert.True(await hub.PublishAsync(message, CancellationToken.None));

        Assert.True(first.Reader.TryRead(out var firstMessage));
        Assert.True(second.Reader.TryRead(out var secondMessage));
        Assert.Equal(message.MessageId, firstMessage.MessageId);
        Assert.Equal(message.MessageId, secondMessage.MessageId);
    }

    [Fact]
    public async Task DisposedSubscription_IsRemoved()
    {
        var hub = CreateHub();
        var conversationId = YatraUlid.NewUlid();
        var subscription = hub.Subscribe(conversationId);

        await subscription.DisposeAsync();

        Assert.False(await hub.PublishAsync(CreateMessage(conversationId), CancellationToken.None));
    }

    [Fact]
    public async Task SlowSubscriber_IsDisconnectedWhenSubscriptionQueueIsFull()
    {
        var hub = CreateHub(streamCapacity: 1);
        var conversationId = YatraUlid.NewUlid();
        await using var subscription = hub.Subscribe(conversationId);

        Assert.True(await hub.PublishAsync(CreateMessage(conversationId), CancellationToken.None));
        Assert.True(await hub.PublishAsync(CreateMessage(conversationId), CancellationToken.None));

        Assert.True(subscription.Reader.TryRead(out _));
        Assert.False(await subscription.Reader.WaitToReadAsync(CancellationToken.None));
        Assert.False(await hub.PublishAsync(CreateMessage(conversationId), CancellationToken.None));
    }

    [Fact]
    public void RoutableReturnPath_IsNotPartOfSerializedPublicMessage()
    {
        var message = CreateMessage(YatraUlid.NewUlid());
        var routable = new RoutableMessage(message, new ReturnPath("orchestrator/sample"));

        var json = JsonSerializer.Serialize(routable.Message, YatraJson.SerializerOptions);

        Assert.DoesNotContain("returnPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orchestrator/sample", json, StringComparison.OrdinalIgnoreCase);
    }

    private static ConversationStreamHub CreateHub(int streamCapacity = 10) =>
        new(
            Options.Create(new YatraEngineOptions { StreamChannelCapacity = streamCapacity }),
            NullLogger<ConversationStreamHub>.Instance);

    private static YatraMessage CreateMessage(string conversationId) =>
        new()
        {
            MessageId = YatraUlid.NewUlid(),
            ConversationId = conversationId,
            Type = YatraMessageTypes.AgentText,
            Source = "orchestrator:sample",
            Destination = "ui:current-conversation",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            InReplyTo = null,
            ExpectsReply = false,
            Payload = JsonSerializer.SerializeToElement(new { text = "hello" }, YatraJson.SerializerOptions)
        };
}
