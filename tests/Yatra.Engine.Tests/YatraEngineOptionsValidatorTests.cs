using Microsoft.Extensions.Options;

namespace Yatra.Engine.Tests;

public sealed class YatraEngineOptionsValidatorTests
{
    [Fact]
    public void Validator_AcceptsDefaultOptions()
    {
        var result = new YatraEngineOptionsValidator().Validate(null, new YatraEngineOptions());

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Theory]
    [InlineData(nameof(YatraEngineOptions.InboundQueueCapacity))]
    [InlineData(nameof(YatraEngineOptions.OutboundQueueCapacity))]
    [InlineData(nameof(YatraEngineOptions.PendingReplyLifetimeMinutes))]
    [InlineData(nameof(YatraEngineOptions.PendingReplyTerminalRetentionMinutes))]
    [InlineData(nameof(YatraEngineOptions.PendingReplyCleanupSeconds))]
    [InlineData(nameof(YatraEngineOptions.SseHeartbeatSeconds))]
    [InlineData(nameof(YatraEngineOptions.StreamChannelCapacity))]
    public void Validator_RejectsNonPositiveConfigurationValues(string propertyName)
    {
        var options = propertyName switch
        {
            nameof(YatraEngineOptions.InboundQueueCapacity) => new YatraEngineOptions { InboundQueueCapacity = 0 },
            nameof(YatraEngineOptions.OutboundQueueCapacity) => new YatraEngineOptions { OutboundQueueCapacity = 0 },
            nameof(YatraEngineOptions.PendingReplyLifetimeMinutes) => new YatraEngineOptions { PendingReplyLifetimeMinutes = 0 },
            nameof(YatraEngineOptions.PendingReplyTerminalRetentionMinutes) => new YatraEngineOptions { PendingReplyTerminalRetentionMinutes = 0 },
            nameof(YatraEngineOptions.PendingReplyCleanupSeconds) => new YatraEngineOptions { PendingReplyCleanupSeconds = 0 },
            nameof(YatraEngineOptions.SseHeartbeatSeconds) => new YatraEngineOptions { SseHeartbeatSeconds = 0 },
            nameof(YatraEngineOptions.StreamChannelCapacity) => new YatraEngineOptions { StreamChannelCapacity = 0 },
            _ => throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, null)
        };

        var result = new YatraEngineOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(propertyName, string.Join(Environment.NewLine, result.Failures));
    }
}
