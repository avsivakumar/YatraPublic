using System.Text.Json;

namespace Yatra.Engine.Api;

public sealed record BrowserMessageSubmission
{
    public required string MessageId { get; init; }

    public required string Type { get; init; }

    public string? InReplyTo { get; init; }

    public required JsonElement Payload { get; init; }
}
