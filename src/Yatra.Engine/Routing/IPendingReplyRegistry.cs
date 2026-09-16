using Yatra.Contracts.Routing;

namespace Yatra.Engine.Routing;

public interface IPendingReplyRegistry
{
    Task RegisterAsync(PendingReply pendingReply, CancellationToken cancellationToken = default);

    Task<PendingReply?> ResolveAsync(
        string requestMessageId,
        string conversationId,
        CancellationToken cancellationToken = default);

    Task<PendingReplyResolution> ClaimForResponseAsync(
        string requestMessageId,
        string conversationId,
        CancellationToken cancellationToken = default);

    Task<bool> CompleteAsync(string requestMessageId, CancellationToken cancellationToken = default);

    Task<bool> CancelAsync(string requestMessageId, CancellationToken cancellationToken = default);

    Task<bool> ReleaseClaimAsync(string requestMessageId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingReply>> ExpireAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<int> RemoveTerminalAsync(
        DateTimeOffset terminalBeforeUtc,
        CancellationToken cancellationToken = default);
}
