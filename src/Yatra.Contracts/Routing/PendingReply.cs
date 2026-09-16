namespace Yatra.Contracts.Routing;

public sealed record PendingReply(
    string RequestMessageId,
    string ConversationId,
    ReturnPath ReturnPath,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    PendingReplyStatus Status,
    string? ExpectedFormId = null,
    string? ExpectedFormVersion = null,
    string? ValidatorKey = null);
