using Microsoft.Extensions.Options;
using Yatra.Engine.Queues;
using Yatra.Engine.Persistence;
using Yatra.Engine.Routing;
using Yatra.Engine.Streaming;
using Yatra.Engine.Workers;

namespace Yatra.Engine.DependencyInjection;

public static class YatraEngineServiceCollectionExtensions
{
    public static IServiceCollection AddYatraEngine(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<YatraEngineOptions>()
            .Bind(configuration.GetSection("Yatra"))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<YatraEngineOptions>, YatraEngineOptionsValidator>();

        services.AddSingleton<FileEngineStateStore>();
        services.AddSingleton<IInboundMessageQueue, PersistentInboundMessageQueue>();
        services.AddSingleton<IOutboundMessageQueue, PersistentOutboundMessageQueue>();

        services.AddSingleton<IConversationMessageStore, FileConversationMessageStore>();
        services.AddSingleton<Yatra.Engine.Api.BrowserSubmissionGate>();
        services.AddSingleton<IPendingReplyRegistry>(provider => provider.GetRequiredService<FileEngineStateStore>());
        services.AddSingleton<IConversationStreamHub, ConversationStreamHub>();
        services.AddSingleton<IEngineAgentOutputSink, EngineAgentOutputSink>();
        services.AddHostedService<InboundMessageWorker>();
        services.AddHostedService<OutboundMessageWorker>();
        services.AddHostedService<PendingReplyCleanupWorker>();

        return services;
    }
}
