using Yatra.Contracts.Routing;

namespace Yatra.Engine.Routing;

public sealed record PendingReplyResolution(
    PendingReplyResolutionStatus Status,
    PendingReply? PendingReply)
{
    public static PendingReplyResolution NotFound { get; } = new(PendingReplyResolutionStatus.NotFound, null);
}
