namespace Yatra.Engine;

public sealed record YatraEngineOptions
{
    public int InboundQueueCapacity { get; init; } = 100;

    public int OutboundQueueCapacity { get; init; } = 100;

    public int PendingReplyLifetimeMinutes { get; init; } = 30;

    public int PendingReplyTerminalRetentionMinutes { get; init; } = 30;

    public int PendingReplyCleanupSeconds { get; init; } = 60;

    public int SseHeartbeatSeconds { get; init; } = 15;

    public int StreamChannelCapacity { get; init; } = 100;

    public string? StorageFolder { get; init; }
}
