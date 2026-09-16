using System.Text.Json;

namespace Yatra.Contracts.Messages;

public static class YatraMessageValidator
{
    public static YatraMessageValidationResult Validate(YatraMessage message)
    {
        var errors = new List<YatraMessageValidationError>();

        AddUlidError(errors, nameof(message.MessageId), message.MessageId);
        AddUlidError(errors, nameof(message.ConversationId), message.ConversationId);

        if (message.InReplyTo is not null && !YatraUlid.IsValid(message.InReplyTo))
        {
            errors.Add(new(
                nameof(message.InReplyTo),
                "invalid_ulid",
                "InReplyTo must be a valid ULID when present."));
        }

        if (!YatraMessageTypes.IsKnown(message.Type))
        {
            errors.Add(new(
                nameof(message.Type),
                "unsupported_message_type",
                "Message type must be registered."));
        }

        if (string.IsNullOrWhiteSpace(message.Source))
        {
            errors.Add(new(
                nameof(message.Source),
                "required",
                "Source is required."));
        }

        if (string.IsNullOrWhiteSpace(message.Destination))
        {
            errors.Add(new(
                nameof(message.Destination),
                "required",
                "Destination is required."));
        }

        if (message.CreatedAtUtc == default)
        {
            errors.Add(new(
                nameof(message.CreatedAtUtc),
                "required",
                "CreatedAtUtc is required."));
        }
        else if (message.CreatedAtUtc.Offset != TimeSpan.Zero)
        {
            errors.Add(new(
                nameof(message.CreatedAtUtc),
                "not_utc",
                "CreatedAtUtc must use a UTC offset."));
        }

        if (message.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            errors.Add(new(
                nameof(message.Payload),
                "required",
                "Payload is required."));
        }
        else if (message.Payload.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new(
                nameof(message.Payload),
                "invalid_payload",
                "Payload must be a JSON object."));
        }

        if (message.Type == YatraMessageTypes.FormResponse && string.IsNullOrWhiteSpace(message.InReplyTo))
        {
            errors.Add(new(
                nameof(message.InReplyTo),
                "required",
                "Form responses must identify the original message using InReplyTo."));
        }

        return errors.Count == 0
            ? YatraMessageValidationResult.Success
            : new YatraMessageValidationResult(errors);
    }

    private static void AddUlidError(
        ICollection<YatraMessageValidationError> errors,
        string field,
        string? value)
    {
        if (!YatraUlid.IsValid(value))
        {
            errors.Add(new(
                field,
                "invalid_ulid",
                $"{field} must be a valid ULID."));
        }
    }
}
