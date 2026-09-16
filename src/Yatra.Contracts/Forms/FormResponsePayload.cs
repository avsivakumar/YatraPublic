using System.Text.Json;

namespace Yatra.Contracts.Forms;

public sealed record FormResponsePayload
{
    public required string FormId { get; init; }

    public required string FormVersion { get; init; }

    public required FormResponseAction Action { get; init; }
    public string? ReplacesResponseId { get; init; }

    public required IReadOnlyDictionary<string, JsonElement> Values { get; init; }
}
