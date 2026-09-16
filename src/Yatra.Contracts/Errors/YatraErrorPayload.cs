namespace Yatra.Contracts.Errors;

public sealed record YatraErrorPayload
{
    public required string Code { get; init; }

    public required string Message { get; init; }

    public required bool Retryable { get; init; }

    public string? RelatedMessageId { get; init; }
}
