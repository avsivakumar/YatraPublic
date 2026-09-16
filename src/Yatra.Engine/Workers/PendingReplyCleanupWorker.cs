using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yatra.Engine.Routing;

namespace Yatra.Engine.Workers;

public sealed class PendingReplyCleanupWorker : BackgroundService
{
    private readonly IPendingReplyRegistry _registry;
    private readonly IOptions<YatraEngineOptions> _options;
    private readonly ILogger<PendingReplyCleanupWorker> _logger;

    public PendingReplyCleanupWorker(
        IPendingReplyRegistry registry,
        IOptions<YatraEngineOptions> options,
        ILogger<PendingReplyCleanupWorker> logger)
    {
        _registry = registry;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.Value.PendingReplyCleanupSeconds);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTimeOffset.UtcNow;
            var expired = await _registry.ExpireAsync(now, stoppingToken);
            var terminalBefore = now.AddMinutes(-_options.Value.PendingReplyTerminalRetentionMinutes);
            var removed = await _registry.RemoveTerminalAsync(terminalBefore, stoppingToken);

            if (expired.Count > 0 || removed > 0)
            {
                _logger.LogInformation(
                    "Pending reply cleanup expired {ExpiredCount} entries and removed {RemovedCount} terminal entries.",
                    expired.Count,
                    removed);
            }
        }
    }
}
