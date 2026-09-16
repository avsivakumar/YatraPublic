using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Yatra.AgentIntegration;
using Yatra.Orchestration.DependencyInjection;
using Yatra.Orchestration.Llm;

namespace Yatra.Orchestration.Tests;

public sealed class OrchestrationDependencyInjectionTests
{
    [Fact]
    public void AddSampleYatraOrchestration_CreatesAgentOwnedClientWithoutHostConfiguration()
    {
        var services = new ServiceCollection();
        services.AddSampleYatraOrchestration();
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ILlmClient>();
        Assert.True(client is FakeLlmClient or OpenAiLlmClient);
        Assert.IsType<SampleOrchestrationAgentAdapter>(provider.GetRequiredService<IOrchestrationAgentAdapter>());
    }

    [Fact]
    public async Task OpenAiClient_RejectsEmptyExplicitKeyWithoutUsingHostCredentials()
    {
        var client = new OpenAiLlmClient(new OrchestrationOptions(), "");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.AskAsync("test", "hello", CancellationToken.None));
        Assert.Contains("AiApiKey", error.Message);
    }

    [Fact]
    public void AddSampleYatraOrchestration_PreservesInjectedLlmAndRegistersAdapter()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILlmClient, FakeLlmClient>();

        services.AddSampleYatraOrchestration();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<FakeLlmClient>(provider.GetRequiredService<ILlmClient>());
        Assert.IsType<SampleOrchestrationAgentAdapter>(
            provider.GetRequiredService<IOrchestrationAgentAdapter>());
    }

    [Theory]
    [InlineData("Fake", "")]
    [InlineData("OpenAI", "gpt-5.1")]
    public void OptionsValidator_AcceptsSupportedProviders(string provider, string model)
    {
        var result = new OrchestrationOptionsValidator().Validate(
            null,
            new OrchestrationOptions
            {
                Provider = provider,
                Model = model,
                TimeoutSeconds = 30
            });

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void OptionsValidator_RejectsUnknownProvider()
    {
        var result = new OrchestrationOptionsValidator().Validate(
            null,
            new OrchestrationOptions
            {
                Provider = "OpenAiX",
                Model = "gpt-5.1",
                TimeoutSeconds = 30
            });

        Assert.True(result.Failed);
        Assert.Contains("Provider", string.Join(Environment.NewLine, result.Failures));
    }

    [Fact]
    public void OptionsValidator_RejectsBlankOpenAiModel()
    {
        var result = new OrchestrationOptionsValidator().Validate(
            null,
            new OrchestrationOptions
            {
                Provider = "OpenAI",
                Model = " ",
                TimeoutSeconds = 30
            });

        Assert.True(result.Failed);
        Assert.Contains("Model", string.Join(Environment.NewLine, result.Failures));
    }
}
