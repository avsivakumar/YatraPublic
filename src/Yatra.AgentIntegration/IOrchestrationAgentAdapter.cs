namespace Yatra.AgentIntegration;

public interface IOrchestrationAgentAdapter
{
    string AgentType { get; }

    Task HandleAsync(
        AgentInput input,
        IAgentOutputSink output,
        CancellationToken cancellationToken);
}
