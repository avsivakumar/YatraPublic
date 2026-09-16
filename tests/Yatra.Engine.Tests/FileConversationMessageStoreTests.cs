using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Yatra.Contracts.Messages;
using Yatra.Engine.Persistence;

namespace Yatra.Engine.Tests;

public sealed class FileConversationMessageStoreTests
{
    [Fact]
    public async Task AppendAsync_WritesConversationMessagesToJsonFile()
    {
        var storageFolder = CreateTempFolder();
        var store = CreateStore(storageFolder);
        var conversationId = YatraUlid.NewUlid();
        var first = CreateMessage(conversationId, "first");
        var second = CreateMessage(conversationId, "second");

        await store.AppendAsync(first, CancellationToken.None);
        await store.AppendAsync(second, CancellationToken.None);

        var messages = await store.ReadConversationAsync(conversationId, CancellationToken.None);

        Assert.Equal([first.MessageId, second.MessageId], messages.Select(message => message.MessageId));
        Assert.True(File.Exists(Path.Combine(storageFolder, "conversations", $"{conversationId}.messages.json")));
    }

    [Fact]
    public async Task AppendAsync_DoesNotDuplicateMessageIds()
    {
        var store = CreateStore(CreateTempFolder());
        var conversationId = YatraUlid.NewUlid();
        var message = CreateMessage(conversationId, "hello");

        await store.AppendAsync(message, CancellationToken.None);
        await store.AppendAsync(message, CancellationToken.None);

        var messages = await store.ReadConversationAsync(conversationId, CancellationToken.None);

        Assert.Single(messages);
        Assert.Equal(message.MessageId, messages[0].MessageId);
    }

    private static FileConversationMessageStore CreateStore(string storageFolder) =>
        new(
            Options.Create(new YatraEngineOptions { StorageFolder = storageFolder }),
            NullLogger<FileConversationMessageStore>.Instance);

    private static string CreateTempFolder() =>
        Path.Combine(Path.GetTempPath(), "Yatra.Tests", Guid.NewGuid().ToString("N"));

    private static YatraMessage CreateMessage(string conversationId, string text) =>
        new()
        {
            MessageId = YatraUlid.NewUlid(),
            ConversationId = conversationId,
            Type = YatraMessageTypes.UserText,
            Source = "ui:current-conversation",
            Destination = "engine:inbound",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            InReplyTo = null,
            ExpectsReply = false,
            Payload = JsonSerializer.SerializeToElement(new { text }, YatraJson.SerializerOptions)
        };
}
