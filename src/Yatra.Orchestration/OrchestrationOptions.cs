namespace Yatra.Orchestration;

public sealed record OrchestrationOptions
{
    public string Provider { get; init; } = "Fake";

    public string Model { get; init; } = "gpt-5.1";

    public int TimeoutSeconds { get; init; } = 30;
}
