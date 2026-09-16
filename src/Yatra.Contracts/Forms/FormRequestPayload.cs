namespace Yatra.Contracts.Forms;

public sealed record FormRequestPayload
{
    public required string FormId { get; init; }

    public required string FormVersion { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public required string SubmitLabel { get; init; }

    public string? CancelLabel { get; init; }

    public required IReadOnlyList<FormFieldDefinition> Fields { get; init; }
}
