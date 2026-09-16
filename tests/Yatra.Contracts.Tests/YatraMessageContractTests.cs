using System.Text.Json;
using Yatra.Contracts.Errors;
using Yatra.Contracts.Forms;
using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;

namespace Yatra.Contracts.Tests;

public sealed class YatraMessageContractTests
{
    public static TheoryData<string, JsonElement> RegisteredMessagePayloads => new()
    {
        { YatraMessageTypes.UserText, JsonSerializer.SerializeToElement(new { text = "Hello" }, YatraJson.SerializerOptions) },
        { YatraMessageTypes.AgentText, JsonSerializer.SerializeToElement(new { text = "Hi there" }, YatraJson.SerializerOptions) },
        { YatraMessageTypes.FormRequest, JsonSerializer.SerializeToElement(CreateFormRequestPayload(), YatraJson.SerializerOptions) },
        { YatraMessageTypes.FormResponse, JsonSerializer.SerializeToElement(CreateFormResponsePayload(), YatraJson.SerializerOptions) },
        { YatraMessageTypes.Status, JsonSerializer.SerializeToElement(new { state = "processing" }, YatraJson.SerializerOptions) },
        {
            YatraMessageTypes.Error,
            JsonSerializer.SerializeToElement(
                new YatraErrorPayload
                {
                    Code = "invalid_message",
                    Message = "Message could not be accepted.",
                    Retryable = false,
                    RelatedMessageId = YatraUlid.NewUlid()
                },
                YatraJson.SerializerOptions)
        }
    };

    [Theory]
    [MemberData(nameof(RegisteredMessagePayloads))]
    public void RegisteredMessageType_RoundTrips_WithCamelCaseEnvelopeProperties(
        string messageType,
        JsonElement payload)
    {
        var message = CreateMessage(messageType, payload);

        var json = JsonSerializer.Serialize(message, YatraJson.SerializerOptions);
        var roundTripped = JsonSerializer.Deserialize<YatraMessage>(json, YatraJson.SerializerOptions);

        Assert.Contains("\"messageId\"", json);
        Assert.Contains("\"conversationId\"", json);
        Assert.Contains("\"createdAtUtc\"", json);
        Assert.Contains("\"inReplyTo\"", json);
        Assert.Contains("\"expectsReply\"", json);
        Assert.Contains("\"payload\"", json);
        Assert.Equal(message.MessageId, roundTripped!.MessageId);
        Assert.Equal(message.ConversationId, roundTripped.ConversationId);
        Assert.Equal(messageType, roundTripped.Type);
        Assert.Equal(NormalizeJson(payload), NormalizeJson(roundTripped.Payload));
    }

    [Fact]
    public void FormRequestPayload_SerializesEnumsAsCamelCaseStrings()
    {
        var json = JsonSerializer.Serialize(CreateFormRequestPayload(), YatraJson.SerializerOptions);
        var payload = JsonSerializer.Deserialize<FormRequestPayload>(json, YatraJson.SerializerOptions);

        Assert.Contains("\"type\": \"select\"", json);
        Assert.Contains("\"type\": \"textarea\"", json);
        Assert.Equal(FormFieldType.Select, payload!.Fields[0].Type);
        Assert.Equal(FormFieldType.Textarea, payload.Fields[1].Type);
    }

    [Fact]
    public void FormResponsePayload_SerializesActionAsCamelCaseString()
    {
        var json = JsonSerializer.Serialize(CreateFormResponsePayload(), YatraJson.SerializerOptions);
        var payload = JsonSerializer.Deserialize<FormResponsePayload>(json, YatraJson.SerializerOptions);

        Assert.Contains("\"action\": \"submit\"", json);
        Assert.Equal(FormResponseAction.Submit, payload!.Action);
        Assert.Equal("Asha", payload.Values["name"].GetString());
    }

    [Fact]
    public void FormResponsePayload_RejectsNumericActionDuringDeserialization()
    {
        const string json = """
            {
              "formId": "contact",
              "formVersion": "1.0",
              "action": 42,
              "values": {}
            }
            """;

        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<FormResponsePayload>(json, YatraJson.SerializerOptions));
    }

    [Fact]
    public void KnownMessageTypes_MatchesInitialRegistry()
    {
        Assert.True(YatraMessageTypes.IsKnown("user.text"));
        Assert.True(YatraMessageTypes.IsKnown("agent.text"));
        Assert.True(YatraMessageTypes.IsKnown("form.request"));
        Assert.True(YatraMessageTypes.IsKnown("form.response"));
        Assert.True(YatraMessageTypes.IsKnown("status"));
        Assert.True(YatraMessageTypes.IsKnown("error"));
        Assert.False(YatraMessageTypes.IsKnown("form.script"));
    }

    [Fact]
    public void UlidGenerator_ProducesValidSortableLengthIdentifiers()
    {
        var value = YatraUlid.NewUlid();

        Assert.Equal(26, value.Length);
        Assert.True(YatraUlid.IsValid(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("01K4Y8M2J7V6R3N9B5QX1TACD")]
    [InlineData("01K4Y8M2J7V6R3N9B5QX1TACDE1")]
    [InlineData("01K4Y8M2J7V6R3N9B5QX1TACI")]
    [InlineData("01K4Y8M2J7V6R3N9B5QX1TACO")]
    [InlineData("01K4Y8M2J7V6R3N9B5QX1TACL")]
    [InlineData("01K4Y8M2J7V6R3N9B5QX1TACU")]
    [InlineData("81K4Y8M2J7V6R3N9B5QX1TACD")]
    [InlineData("Z1K4Y8M2J7V6R3N9B5QX1TACD")]
    [InlineData("81k4y8m2j7v6r3n9b5qx1tacd")]
    [InlineData("z1k4y8m2j7v6r3n9b5qx1tacd")]
    public void UlidValidator_RejectsMissingMalformedAmbiguousAndOverflowValues(string? value)
    {
        Assert.False(YatraUlid.IsValid(value));
    }

    [Fact]
    public void UlidValidator_AcceptsLowercaseValidValues()
    {
        Assert.True(YatraUlid.IsValid(YatraUlid.NewUlid().ToLowerInvariant()));
    }

    [Fact]
    public void RoutingContracts_CaptureQualifiedReturnPathWithoutEmbeddingItInMessage()
    {
        var returnPath = new ReturnPath("orchestrator/sample");

        var pending = new PendingReply(
            RequestMessageId: YatraUlid.NewUlid(),
            ConversationId: YatraUlid.NewUlid(),
            ReturnPath: returnPath,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(30),
            Status: PendingReplyStatus.Pending);

        Assert.Equal("orchestrator/sample", pending.ReturnPath.QualifiedPath);
        Assert.Equal(PendingReplyStatus.Pending, pending.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void ReturnPath_RejectsBlankQualifiedPath(string qualifiedPath)
    {
        var exception = Assert.Throws<ArgumentException>(() => new ReturnPath(qualifiedPath));

        Assert.Equal("qualifiedPath", exception.ParamName);
        Assert.Contains("required", exception.Message);
    }

    [Fact]
    public void Validator_AcceptsValidMessage()
    {
        var result = YatraMessageValidator.Validate(CreateMessage());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("", "MessageId")]
    [InlineData("not-a-ulid", "MessageId")]
    public void Validator_RejectsMissingOrInvalidMessageId(string messageId, string expectedField)
    {
        var result = YatraMessageValidator.Validate(CreateMessage() with { MessageId = messageId });

        Assert.Contains(result.Errors, error => error.Field == expectedField && error.Code == "invalid_ulid");
    }

    [Fact]
    public void Validator_RejectsInvalidConversationId()
    {
        var result = YatraMessageValidator.Validate(CreateMessage() with { ConversationId = "not-a-ulid" });

        Assert.Contains(result.Errors, error => error.Field == "ConversationId" && error.Code == "invalid_ulid");
    }

    [Fact]
    public void Validator_RejectsInvalidInReplyTo()
    {
        var result = YatraMessageValidator.Validate(CreateMessage() with { InReplyTo = "not-a-ulid" });

        Assert.Contains(result.Errors, error => error.Field == "InReplyTo" && error.Code == "invalid_ulid");
    }

    [Fact]
    public void Validator_RejectsUnknownMessageType()
    {
        var result = YatraMessageValidator.Validate(CreateMessage() with { Type = "form.script" });

        Assert.Contains(result.Errors, error => error.Field == "Type" && error.Code == "unsupported_message_type");
    }

    [Fact]
    public void Validator_RejectsMissingSource()
    {
        var result = YatraMessageValidator.Validate(CreateMessage() with { Source = " " });

        Assert.Contains(result.Errors, error => error.Field == "Source" && error.Code == "required");
    }

    [Fact]
    public void Validator_RejectsMissingDestination()
    {
        var result = YatraMessageValidator.Validate(CreateMessage() with { Destination = " " });

        Assert.Contains(result.Errors, error => error.Field == "Destination" && error.Code == "required");
    }

    [Fact]
    public void Validator_RejectsMissingPayload()
    {
        var result = YatraMessageValidator.Validate(CreateMessage() with { Payload = default });

        Assert.Contains(result.Errors, error => error.Field == "Payload" && error.Code == "required");
    }

    [Fact]
    public void Validator_RejectsNonObjectPayload()
    {
        var result = YatraMessageValidator.Validate(CreateMessage() with
        {
            Payload = JsonSerializer.SerializeToElement(new[] { "not", "an", "object" }, YatraJson.SerializerOptions)
        });

        Assert.Contains(result.Errors, error => error.Field == "Payload" && error.Code == "invalid_payload");
    }

    [Fact]
    public void Validator_RejectsFormResponseWithoutInReplyTo()
    {
        var result = YatraMessageValidator.Validate(CreateMessage(
            YatraMessageTypes.FormResponse,
            JsonSerializer.SerializeToElement(CreateFormResponsePayload(), YatraJson.SerializerOptions)) with
            {
                InReplyTo = null
            });

        Assert.Contains(result.Errors, error => error.Field == "InReplyTo" && error.Code == "required");
    }

    [Fact]
    public void Validator_RejectsNonUtcCreatedAt()
    {
        var result = YatraMessageValidator.Validate(CreateMessage() with
        {
            CreatedAtUtc = new DateTimeOffset(2026, 9, 9, 20, 0, 0, TimeSpan.FromHours(-5))
        });

        Assert.Contains(result.Errors, error => error.Field == "CreatedAtUtc" && error.Code == "not_utc");
    }

    private static YatraMessage CreateMessage() =>
        CreateMessage(
            YatraMessageTypes.UserText,
            JsonSerializer.SerializeToElement(new { text = "Hello" }, YatraJson.SerializerOptions));

    private static YatraMessage CreateMessage(string messageType, JsonElement payload)
    {
        var inReplyTo = messageType == YatraMessageTypes.FormResponse
            ? YatraUlid.NewUlid()
            : null;

        return new YatraMessage
        {
            MessageId = YatraUlid.NewUlid(),
            ConversationId = YatraUlid.NewUlid(),
            Type = messageType,
            Source = messageType is YatraMessageTypes.UserText or YatraMessageTypes.FormResponse
                ? "ui:current-conversation"
                : "orchestrator:sample",
            Destination = messageType is YatraMessageTypes.UserText or YatraMessageTypes.FormResponse
                ? "engine:inbound"
                : "ui:current-conversation",
            CreatedAtUtc = DateTimeOffset.Parse("2026-09-09T20:00:00Z"),
            InReplyTo = inReplyTo,
            ExpectsReply = messageType == YatraMessageTypes.FormRequest,
            Payload = payload
        };
    }

    private static FormRequestPayload CreateFormRequestPayload() =>
        new()
        {
            FormId = "access-request",
            FormVersion = "1.0",
            Title = "Application Access Request",
            Description = "Provide the requested access details.",
            SubmitLabel = "Submit",
            CancelLabel = "Cancel",
            Fields =
            [
                new FormFieldDefinition
                {
                    Id = "application",
                    Type = FormFieldType.Select,
                    Label = "Application",
                    Required = true,
                    Options =
                    [
                        new FormOption { Value = "finance", Label = "Finance" },
                        new FormOption { Value = "operations", Label = "Operations" }
                    ]
                },
                new FormFieldDefinition
                {
                    Id = "justification",
                    Type = FormFieldType.Textarea,
                    Label = "Business justification",
                    Required = true,
                    MaxLength = 500
                }
            ]
        };

    private static FormResponsePayload CreateFormResponsePayload() =>
        new()
        {
            FormId = "contact",
            FormVersion = "1.0",
            Action = FormResponseAction.Submit,
            Values = new Dictionary<string, JsonElement>
            {
                ["name"] = JsonSerializer.SerializeToElement("Asha"),
                ["message"] = JsonSerializer.SerializeToElement("Hello")
            }
        };

    private static string NormalizeJson(JsonElement element) =>
        JsonSerializer.Serialize(element, new JsonSerializerOptions(YatraJson.SerializerOptions)
        {
            WriteIndented = false
        });
}
