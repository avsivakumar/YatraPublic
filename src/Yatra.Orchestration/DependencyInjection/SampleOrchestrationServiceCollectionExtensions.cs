using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Yatra.AgentIntegration;
using Yatra.Orchestration.Forms;
using Yatra.Orchestration.Llm;

namespace Yatra.Orchestration.DependencyInjection;

public static class SampleOrchestrationServiceCollectionExtensions
{
    public static IServiceCollection AddSampleYatraOrchestration(
        this IServiceCollection services)
    {
        services.TryAddSingleton<ILlmClient>(_ => SampleOrchestrationAgentAdapter.CreateLocalLlmClient());

        services.AddSingleton<IOrchestrationAgentAdapter, SampleOrchestrationAgentAdapter>();
        services.AddSingleton<IFormResponseValidator, SampleFormResponseAdapterValidator>();

        return services;
    }
}
