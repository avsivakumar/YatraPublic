using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;
using Yatra.Engine.Routing;

namespace Yatra.Engine.Tests;

public sealed class PendingReplyRegistryTests
{
    [Fact]
    public async Task RegisterAndResolve_ReturnsPendingReplyByMessageIdAndConversation()
    {
        var registry = new InMemoryPendingReplyRegistry();
        var pendingReply = CreatePendingReply();

        await registry.RegisterAsync(pendingReply);

        var resolved = await registry.ResolveAsync(pendingReply.RequestMessageId, pendingReply.ConversationId);

        Assert.Equal(pendingReply, resolved);
    }

    [Fact]
    public async Task Resolve_ReturnsNullForDifferentConversation()
    {
        var registry = new InMemoryPendingReplyRegistry();
        var pendingReply = CreatePendingReply();

        await registry.RegisterAsync(pendingReply);

        var resolved = await registry.ResolveAsync(pendingReply.RequestMessageId, YatraUlid.NewUlid());

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ClaimForResponse_ReturnsConversationMismatchForDifferentConversation()
    {
        var registry = new InMemoryPendingReplyRegistry();
        var pendingReply = CreatePendingReply();

        await registry.RegisterAsync(pendingReply);

        var resolution = await registry.ClaimForResponseAsync(pendingReply.RequestMessageId, YatraUlid.NewUlid());

        Assert.Equal(PendingReplyResolutionStatus.ConversationMismatch, resolution.Status);
        Assert.Equal(pendingReply, resolution.PendingReply);
    }

    [Fact]
    public async Task Complete_PreventsFurtherResolve()
    {
        var registry = new InMemoryPendingReplyRegistry();
        var pendingReply = CreatePendingReply();

        await registry.RegisterAsync(pendingReply);

        var claim = await registry.ClaimForResponseAsync(pendingReply.RequestMessageId, pendingReply.ConversationId);
        Assert.Equal(PendingReplyResolutionStatus.Resolved, claim.Status);
        Assert.True(await registry.CompleteAsync(pendingReply.RequestMessageId));

        var resolved = await registry.ResolveAsync(pendingReply.RequestMessageId, pendingReply.ConversationId);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ClaimForResponse_ReturnsAlreadyCompletedForDuplicateResponse()
    {
        var registry = new InMemoryPendingReplyRegistry();
        var pendingReply = CreatePendingReply();

        await registry.RegisterAsync(pendingReply);
        await registry.ClaimForResponseAsync(pendingReply.RequestMessageId, pendingReply.ConversationId);
        await registry.CompleteAsync(pendingReply.RequestMessageId);

        var resolution = await registry.ClaimForResponseAsync(
            pendingReply.RequestMessageId,
            pendingReply.ConversationId);

        Assert.Equal(PendingReplyResolutionStatus.AlreadyCompleted, resolution.Status);
    }

    [Fact]
    public async Task DuplicateRegister_ThrowsClearError()
    {
        var registry = new InMemoryPendingReplyRegistry();
        var pendingReply = CreatePendingReply();

        await registry.RegisterAsync(pendingReply);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.RegisterAsync(pendingReply));

        Assert.Contains("already exists", exception.Message);
    }

    [Fact]
    public async Task Expire_MarksOnlyExpiredPendingReplies()
    {
        var registry = new InMemoryPendingReplyRegistry();
        var expired = CreatePendingReply(
            createdAtUtc: DateTimeOffset.UtcNow.AddMinutes(-10),
            expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
        var active = CreatePendingReply(
            createdAtUtc: DateTimeOffset.UtcNow,
            expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(10));

        await registry.RegisterAsync(expired);
        await registry.RegisterAsync(active);

        var expiredReplies = await registry.ExpireAsync(DateTimeOffset.UtcNow);

        Assert.Single(expiredReplies);
        Assert.Equal(expired.RequestMessageId, expiredReplies[0].RequestMessageId);
        Assert.Null(await registry.ResolveAsync(expired.RequestMessageId, expired.ConversationId));
        Assert.NotNull(await registry.ResolveAsync(active.RequestMessageId, active.ConversationId));
    }

    [Fact]
    public async Task ClaimForResponse_ReturnsExpiredWhenPendingReplyExpired()
    {
        var registry = new InMemoryPendingReplyRegistry();
        var pendingReply = CreatePendingReply(
            createdAtUtc: DateTimeOffset.UtcNow.AddMinutes(-10),
            expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1));

        await registry.RegisterAsync(pendingReply);

        var resolution = await registry.ClaimForResponseAsync(
            pendingReply.RequestMessageId,
            pendingReply.ConversationId);

        Assert.Equal(PendingReplyResolutionStatus.Expired, resolution.Status);
    }

    [Fact]
    public async Task ClaimForResponse_PreventsConcurrentClaimUntilReleased()
    {
        var registry = new InMemoryPendingReplyRegistry();
        var pendingReply = CreatePendingReply();

        await registry.RegisterAsync(pendingReply);

        var first = await registry.ClaimForResponseAsync(pendingReply.RequestMessageId, pendingReply.ConversationId);
        var second = await registry.ClaimForResponseAsync(pendingReply.RequestMessageId, pendingReply.ConversationId);

        Assert.Equal(PendingReplyResolutionStatus.Resolved, first.Status);
        Assert.Equal(PendingReplyResolutionStatus.AlreadyProcessing, second.Status);

        Assert.True(await registry.ReleaseClaimAsync(pendingReply.RequestMessageId));
        var third = await registry.ClaimForResponseAsync(pendingReply.RequestMessageId, pendingReply.ConversationId);
        Assert.Equal(PendingReplyResolutionStatus.Resolved, third.Status);
    }

    [Fact]
    public async Task RemoveTerminal_RemovesOldTerminalEntries()
    {
        var registry = new InMemoryPendingReplyRegistry();
        var pendingReply = CreatePendingReply(
            createdAtUtc: DateTimeOffset.UtcNow.AddHours(-2),
            expiresAtUtc: DateTimeOffset.UtcNow.AddHours(-1));

        await registry.RegisterAsync(pendingReply);
        await registry.ExpireAsync(DateTimeOffset.UtcNow);

        var removed = await registry.RemoveTerminalAsync(DateTimeOffset.UtcNow);

        Assert.Equal(1, removed);
        Assert.Equal(PendingReplyResolutionStatus.NotFound,
            (await registry.ClaimForResponseAsync(pendingReply.RequestMessageId, pendingReply.ConversationId)).Status);
    }

    private static PendingReply CreatePendingReply(
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? expiresAtUtc = null)
    {
        var created = createdAtUtc ?? DateTimeOffset.UtcNow;

        return new PendingReply(
            RequestMessageId: YatraUlid.NewUlid(),
            ConversationId: YatraUlid.NewUlid(),
            ReturnPath: new ReturnPath("orchestrator/sample"),
            CreatedAtUtc: created,
            ExpiresAtUtc: expiresAtUtc ?? created.AddMinutes(30),
            Status: PendingReplyStatus.Pending);
    }
}
