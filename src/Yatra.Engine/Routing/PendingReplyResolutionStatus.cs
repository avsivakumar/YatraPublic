namespace Yatra.Engine.Routing;

public enum PendingReplyResolutionStatus
{
    Resolved,
    NotFound,
    ConversationMismatch,
    Expired,
    AlreadyProcessing,
    AlreadyCompleted,
    Cancelled
}
