using Microsoft.Extensions.Options;

namespace Yatra.Engine;

public sealed class YatraEngineOptionsValidator : IValidateOptions<YatraEngineOptions>
{
    public ValidateOptionsResult Validate(string? name, YatraEngineOptions options)
    {
        var failures = new List<string>();

        AddGreaterThanZeroFailure(failures, nameof(options.InboundQueueCapacity), options.InboundQueueCapacity);
        AddGreaterThanZeroFailure(failures, nameof(options.OutboundQueueCapacity), options.OutboundQueueCapacity);
        AddGreaterThanZeroFailure(failures, nameof(options.PendingReplyLifetimeMinutes), options.PendingReplyLifetimeMinutes);
        AddGreaterThanZeroFailure(failures, nameof(options.PendingReplyTerminalRetentionMinutes), options.PendingReplyTerminalRetentionMinutes);
        AddGreaterThanZeroFailure(failures, nameof(options.PendingReplyCleanupSeconds), options.PendingReplyCleanupSeconds);
        AddGreaterThanZeroFailure(failures, nameof(options.SseHeartbeatSeconds), options.SseHeartbeatSeconds);
        AddGreaterThanZeroFailure(failures, nameof(options.StreamChannelCapacity), options.StreamChannelCapacity);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void AddGreaterThanZeroFailure(List<string> failures, string name, int value)
    {
        if (value <= 0)
        {
            failures.Add($"{name} must be greater than zero.");
        }
    }
}
