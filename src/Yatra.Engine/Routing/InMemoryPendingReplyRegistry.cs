using System.Collections.Concurrent;
using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;

namespace Yatra.Engine.Routing;

public sealed class InMemoryPendingReplyRegistry : IPendingReplyRegistry
{
    private readonly ConcurrentDictionary<string, PendingReply> _pendingReplies = new();

    public Task RegisterAsync(PendingReply pendingReply, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!YatraUlid.IsValid(pendingReply.RequestMessageId))
        {
            throw new ArgumentException("Pending reply request message ID must be a valid ULID.", nameof(pendingReply));
        }

        if (!YatraUlid.IsValid(pendingReply.ConversationId))
        {
            throw new ArgumentException("Pending reply conversation ID must be a valid ULID.", nameof(pendingReply));
        }

        if (string.IsNullOrWhiteSpace(pendingReply.ReturnPath.QualifiedPath))
        {
            throw new ArgumentException("Pending reply return path is required.", nameof(pendingReply));
        }

        if (pendingReply.ExpiresAtUtc <= pendingReply.CreatedAtUtc)
        {
            throw new ArgumentException("Pending reply expiration must be after creation.", nameof(pendingReply));
        }

        if (!_pendingReplies.TryAdd(pendingReply.RequestMessageId, pendingReply))
        {
            throw new InvalidOperationException($"Pending reply already exists for message '{pendingReply.RequestMessageId}'.");
        }

        return Task.CompletedTask;
    }

    public Task<PendingReply?> ResolveAsync(
        string requestMessageId,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_pendingReplies.TryGetValue(requestMessageId, out var pendingReply))
        {
            return Task.FromResult<PendingReply?>(null);
        }

        if (pendingReply.ConversationId != conversationId || pendingReply.Status != PendingReplyStatus.Pending)
        {
            return Task.FromResult<PendingReply?>(null);
        }

        if (pendingReply.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            var expired = pendingReply with { Status = PendingReplyStatus.Expired };
            _pendingReplies.TryUpdate(requestMessageId, expired, pendingReply);
            return Task.FromResult<PendingReply?>(null);
        }

        return Task.FromResult<PendingReply?>(pendingReply);
    }

    public Task<PendingReplyResolution> ClaimForResponseAsync(
        string requestMessageId,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        while (_pendingReplies.TryGetValue(requestMessageId, out var pendingReply))
        {
            var terminalResolution = GetTerminalResolution(pendingReply, conversationId);
            if (terminalResolution is not null)
            {
                return Task.FromResult(terminalResolution);
            }

            if (pendingReply.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                var expired = pendingReply with { Status = PendingReplyStatus.Expired };
                if (_pendingReplies.TryUpdate(requestMessageId, expired, pendingReply))
                {
                    return Task.FromResult(new PendingReplyResolution(
                        PendingReplyResolutionStatus.Expired,
                        expired));
                }

                continue;
            }

            var claimed = pendingReply with { Status = PendingReplyStatus.Processing };
            if (_pendingReplies.TryUpdate(requestMessageId, claimed, pendingReply))
            {
                return Task.FromResult(new PendingReplyResolution(
                    PendingReplyResolutionStatus.Resolved,
                    claimed));
            }
        }

        return Task.FromResult(PendingReplyResolution.NotFound);
    }

    public Task<bool> CompleteAsync(string requestMessageId, CancellationToken cancellationToken = default) =>
        SetStatusAsync(requestMessageId, PendingReplyStatus.Completed, cancellationToken, PendingReplyStatus.Processing);

    public Task<bool> CancelAsync(string requestMessageId, CancellationToken cancellationToken = default) =>
        SetStatusAsync(requestMessageId, PendingReplyStatus.Cancelled, cancellationToken, PendingReplyStatus.Processing);

    public Task<bool> ReleaseClaimAsync(string requestMessageId, CancellationToken cancellationToken = default) =>
        SetStatusAsync(requestMessageId, PendingReplyStatus.Pending, cancellationToken, PendingReplyStatus.Processing);

    public Task<IReadOnlyList<PendingReply>> ExpireAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expiredReplies = new List<PendingReply>();

        foreach (var pair in _pendingReplies)
        {
            var pendingReply = pair.Value;
            if (pendingReply.Status is not (PendingReplyStatus.Pending or PendingReplyStatus.Processing)
                || pendingReply.ExpiresAtUtc > now)
            {
                continue;
            }

            var expired = pendingReply with { Status = PendingReplyStatus.Expired };
            if (_pendingReplies.TryUpdate(pair.Key, expired, pendingReply))
            {
                expiredReplies.Add(expired);
            }
        }

        return Task.FromResult<IReadOnlyList<PendingReply>>(expiredReplies);
    }

    public Task<int> RemoveTerminalAsync(
        DateTimeOffset terminalBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var removedCount = 0;

        foreach (var pair in _pendingReplies)
        {
            var pendingReply = pair.Value;
            if (pendingReply.Status is PendingReplyStatus.Pending or PendingReplyStatus.Processing
                || pendingReply.ExpiresAtUtc > terminalBeforeUtc)
            {
                continue;
            }

            if (_pendingReplies.TryRemove(pair.Key, out _))
            {
                removedCount++;
            }
        }

        return Task.FromResult(removedCount);
    }

    private Task<bool> SetStatusAsync(
        string requestMessageId,
        PendingReplyStatus status,
        CancellationToken cancellationToken,
        params PendingReplyStatus[] allowedCurrentStatuses)
    {
        cancellationToken.ThrowIfCancellationRequested();

        while (_pendingReplies.TryGetValue(requestMessageId, out var current))
        {
            if (!allowedCurrentStatuses.Contains(current.Status))
            {
                return Task.FromResult(false);
            }

            var updated = current with { Status = status };
            if (_pendingReplies.TryUpdate(requestMessageId, updated, current))
            {
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    private static PendingReplyResolution? GetTerminalResolution(
        PendingReply pendingReply,
        string conversationId)
    {
        if (pendingReply.ConversationId != conversationId)
        {
            return new PendingReplyResolution(PendingReplyResolutionStatus.ConversationMismatch, pendingReply);
        }

        return pendingReply.Status switch
        {
            PendingReplyStatus.Processing => new PendingReplyResolution(PendingReplyResolutionStatus.AlreadyProcessing, pendingReply),
            PendingReplyStatus.Completed => new PendingReplyResolution(PendingReplyResolutionStatus.AlreadyCompleted, pendingReply),
            PendingReplyStatus.Cancelled => new PendingReplyResolution(PendingReplyResolutionStatus.Cancelled, pendingReply),
            PendingReplyStatus.Expired => new PendingReplyResolution(PendingReplyResolutionStatus.Expired, pendingReply),
            _ => null
        };
    }
}
