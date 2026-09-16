namespace Yatra.Engine.Queues;

public sealed record YatraQueueOptions
{
    public int Capacity { get; init; } = 100;
}
