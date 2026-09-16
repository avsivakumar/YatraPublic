namespace Yatra.AgentIntegration;

public interface IAgentOutputSink
{
    ValueTask PublishAsync(
        AgentOutput output,
        CancellationToken cancellationToken);
}
