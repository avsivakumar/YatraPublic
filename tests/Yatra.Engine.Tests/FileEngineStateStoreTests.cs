using System.Text.Json;
using Microsoft.Extensions.Options;
using Yatra.AgentIntegration;
using Yatra.Contracts.Forms;
using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;
using Yatra.Engine;
using Yatra.Engine.Persistence;
using Yatra.Engine.Routing;

namespace Yatra.Engine.Tests;

public sealed class FileEngineStateStoreTests
{
    private static readonly CancellationToken Token = CancellationToken.None;
    private static string Folder() => Path.Combine(Path.GetTempPath(), "Yatra.State.Tests", Guid.NewGuid().ToString("N"));
    private static FileEngineStateStore Open(string folder) => new(Options.Create(new YatraEngineOptions { StorageFolder = folder }));
    private static YatraMessage Message(string? conversationId = null) => new()
    {
        MessageId = YatraUlid.NewUlid(), ConversationId = conversationId ?? YatraUlid.NewUlid(),
        Type = YatraMessageTypes.UserText, Source = "ui:test", Destination = "engine:inbound",
        CreatedAtUtc = DateTimeOffset.UtcNow, ExpectsReply = false,
        Payload = JsonSerializer.SerializeToElement(new { text = "hello" })
    };

    [Fact]
    public async Task UnacknowledgedInboxAndOutboxRecoverWithOriginalIds()
    {
        var folder = Folder(); var input = Message(); var output = Message(input.ConversationId) with { Type = YatraMessageTypes.AgentText };
        using (var first = Open(folder))
        {
            Assert.False(await first.AcceptAsync(input, Token));
            await first.PublishAsync(new AgentOutput(output), Token);
            await using var reader = first.ReadInboundAsync(Token).GetAsyncEnumerator();
            Assert.True(await reader.MoveNextAsync()); // Simulate a process exiting during delivery.
        }
        using var restored = Open(folder);
        await using var inbox = restored.ReadInboundAsync(Token).GetAsyncEnumerator();
        await using var outbox = restored.ReadOutboundAsync(Token).GetAsyncEnumerator();
        Assert.True(await inbox.MoveNextAsync()); Assert.Equal(input.MessageId, inbox.Current.MessageId);
        Assert.True(await outbox.MoveNextAsync()); Assert.Equal(output.MessageId, outbox.Current.Message.MessageId);
        Assert.True(await restored.AcceptAsync(input, Token));
        await Assert.ThrowsAsync<FileEngineStateStore.SubmissionConflictException>(() => restored.AcceptAsync(input with { Payload = JsonSerializer.SerializeToElement(new { text = "changed" }) }, Token));
    }

    [Fact]
    public async Task AtomicCompletionRestoresRoutingAndDoesNotReplayCompletedInput()
    {
        var folder = Folder(); var input = Message();
        var form = Message(input.ConversationId) with
        {
            Type = YatraMessageTypes.FormRequest, ExpectsReply = true,
            Payload = JsonSerializer.SerializeToElement(new { formId = "contact", formVersion = "1.0", title = "Contact", submitLabel = "Send", fields = Array.Empty<object>() })
        };
        using (var first = Open(folder))
        {
            await first.AcceptAsync(input, Token);
            await first.CommitInboundAsync(input, [new AgentOutput(form, new ReturnPath("orchestrator/sample"))], null, Token);
            Assert.Equal(PendingReplyResolutionStatus.Resolved, (await first.ClaimForResponseAsync(form.MessageId, input.ConversationId)).Status);
        }
        using var restored = Open(folder);
        var pending = await restored.ResolveAsync(form.MessageId, input.ConversationId);
        Assert.NotNull(pending); Assert.Equal("orchestrator/sample", pending.ReturnPath.QualifiedPath);
        Assert.Equal("contact", pending.ExpectedFormId); Assert.Equal("1.0", pending.ExpectedFormVersion);
        using var timeout = new CancellationTokenSource(200);
        await using var inbox = restored.ReadInboundAsync(timeout.Token).GetAsyncEnumerator();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await inbox.MoveNextAsync());
    }

    [Fact]
    public async Task CompletedCancelledAndExpiredRepliesStayTerminal()
    {
        var folder = Folder(); var now = DateTimeOffset.UtcNow; var conversation = YatraUlid.NewUlid();
        var ids = Enumerable.Range(0, 3).Select(_ => YatraUlid.NewUlid()).ToArray();
        using (var first = Open(folder))
        {
            foreach (var id in ids) await first.RegisterAsync(new PendingReply(id, conversation, new ReturnPath("test"), now, now.AddMinutes(30), PendingReplyStatus.Pending));
            await first.ClaimForResponseAsync(ids[0], conversation); await first.CompleteAsync(ids[0]);
            await first.ClaimForResponseAsync(ids[1], conversation); await first.CancelAsync(ids[1]);
            await first.ExpireAsync(now.AddHours(1));
            await first.RemoveTerminalAsync(now.AddDays(1));
        }
        using var restored = Open(folder);
        Assert.Equal(PendingReplyResolutionStatus.AlreadyCompleted, (await restored.ClaimForResponseAsync(ids[0], conversation)).Status);
        Assert.Equal(PendingReplyResolutionStatus.Cancelled, (await restored.ClaimForResponseAsync(ids[1], conversation)).Status);
        Assert.Equal(PendingReplyResolutionStatus.Expired, (await restored.ClaimForResponseAsync(ids[2], conversation)).Status);
    }

    [Fact]
    public void CorruptStateFailsClosedAndConcurrentOwnerIsRejected()
    {
        var folder = Folder();
        using (var owner = Open(folder)) Assert.Throws<IOException>(() => Open(folder));
        File.WriteAllText(Path.Combine(folder, "engine-state.json"), "{broken");
        Assert.Throws<JsonException>(() => Open(folder));
        Assert.Equal("{broken", File.ReadAllText(Path.Combine(folder, "engine-state.json")));
    }

    [Fact]
    public async Task FailedCommitDoesNotChangeDurableOrLiveState()
    {
        var folder = Folder(); var input = Message();
        using var store = Open(folder);
        await store.AcceptAsync(input, Token);
        var before = File.ReadAllText(Path.Combine(folder, "engine-state.json"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CommitInboundAsync(input,
            [new AgentOutput(Message() with { ExpectsReply = true })], null, Token));
        Assert.Equal(before, File.ReadAllText(Path.Combine(folder, "engine-state.json")));
        await using var reader = store.ReadInboundAsync(Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync()); Assert.Equal(input.MessageId, reader.Current.MessageId);
    }

    [Fact]
    public async Task AcknowledgedOutboxDoesNotRedeliverAfterRestart()
    {
        var folder = Folder(); var message = Message();
        using (var first = Open(folder))
        {
            await first.PublishAsync(new AgentOutput(message), Token);
            await first.CompleteOutboundAsync(message.MessageId, Token);
        }
        using var restored = Open(folder);
        using var timeout = new CancellationTokenSource(200);
        await using var reader = restored.ReadOutboundAsync(timeout.Token).GetAsyncEnumerator();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.MoveNextAsync());
    }

    [Fact]
    public void IncompleteSnapshotIsNotSilentlyReplacedWithEmptyState()
    {
        var folder = Folder(); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "engine-state.json"), "{}");
        Assert.Throws<JsonException>(() => Open(folder));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(folder, "engine-state.json")));
    }
}
