namespace Yatra.Contracts.Forms;

public sealed record FormOption
{
    public required string Value { get; init; }

    public required string Label { get; init; }
}
