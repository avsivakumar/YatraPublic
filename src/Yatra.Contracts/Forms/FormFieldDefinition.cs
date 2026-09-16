namespace Yatra.Contracts.Forms;

public sealed record FormFieldDefinition
{
    public required string Id { get; init; }

    public required FormFieldType Type { get; init; }

    public required string Label { get; init; }

    public bool Required { get; init; }

    public System.Text.Json.JsonElement? InitialValue { get; init; }
    public bool ReadOnly { get; init; }
    public bool Disabled { get; init; }
    public string? HelpText { get; init; }
    public bool? IntegerOnly { get; init; }
    public int? MinSelections { get; init; }
    public int? MaxSelections { get; init; }
    public string? MinDate { get; init; }
    public string? MaxDate { get; init; }

    public int? MinLength { get; init; }

    public int? MaxLength { get; init; }

    public decimal? Min { get; init; }

    public decimal? Max { get; init; }

    public string? Pattern { get; init; }

    public string? Placeholder { get; init; }

    public IReadOnlyList<FormOption>? Options { get; init; }
}
