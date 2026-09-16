using System.Text.Json;

namespace Yatra.Contracts.Messages;

public sealed record YatraMessage
{
    public required string MessageId { get; init; }

    public required string ConversationId { get; init; }

    public required string Type { get; init; }

    public required string Source { get; init; }

    public required string Destination { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public string? InReplyTo { get; init; }

    public required bool ExpectsReply { get; init; }

    public required JsonElement Payload { get; init; }
}
