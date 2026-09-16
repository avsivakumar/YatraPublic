using System.Text.Json;
using Yatra.AgentIntegration;
using Yatra.Contracts.Forms;
using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;
using Yatra.Orchestration.Llm;

namespace Yatra.Orchestration.Tests;

public sealed class SampleOrchestrationAgentAdapterTests
{
    [Theory]
    [InlineData("show contact form", "contact")]
    [InlineData("  SHOW CONTACT FORM  ", "contact")]
    [InlineData("request application access", "access-request")]
    [InlineData("confirm an action", "confirmation")]
    [InlineData("run sample skill", "sample-skill")]
    public async Task Commands_ProduceExpectedPredefinedForms(string userText, string expectedFormId)
    {
        var sink = new RecordingSink();
        var adapter = new SampleOrchestrationAgentAdapter(new RecordingLlmClient());

        var input = CreateUserText(userText);

        await adapter.HandleAsync(input, sink, CancellationToken.None);

        var output = Assert.Single(sink.Outputs);
        var payload = output.Message.Payload.Deserialize<FormRequestPayload>(YatraJson.SerializerOptions);

        Assert.Equal(YatraMessageTypes.FormRequest, output.Message.Type);
        Assert.True(output.Message.ExpectsReply);
        Assert.Equal("orchestrator/sample", output.ReturnPath!.QualifiedPath);
        Assert.Equal(expectedFormId, payload!.FormId);
        Assert.Equal(input.Message.MessageId, output.Message.InReplyTo);
    }

    [Fact]
    public async Task OrdinaryQuestion_CallsLlmAndPublishesAgentText()
    {
        var llm = new RecordingLlmClient("A small answer.");
        var sink = new RecordingSink();
        var adapter = new SampleOrchestrationAgentAdapter(llm);

        var input = CreateUserText("What is Yatra?");

        await adapter.HandleAsync(input, sink, CancellationToken.None);

        var output = Assert.Single(sink.Outputs);
        Assert.Equal("What is Yatra?", llm.LastUserText);
        Assert.Equal(YatraMessageTypes.AgentText, output.Message.Type);
        Assert.Equal(input.Message.MessageId, output.Message.InReplyTo);
        Assert.Contains("A small answer.", output.Message.Payload.GetRawText());
    }

    [Fact]
    public async Task LlmFailure_PublishesFriendlyRetryableError()
    {
        var sink = new RecordingSink();
        var adapter = new SampleOrchestrationAgentAdapter(new ThrowingLlmClient());

        var input = CreateUserText("ordinary question");

        await adapter.HandleAsync(input, sink, CancellationToken.None);

        var output = Assert.Single(sink.Outputs);
        Assert.Equal(YatraMessageTypes.Error, output.Message.Type);
        Assert.Equal(input.Message.MessageId, output.Message.InReplyTo);
        Assert.Contains("llm_unavailable", output.Message.Payload.GetRawText());
        Assert.Contains("\"retryable\": true", output.Message.Payload.GetRawText());
    }

    [Fact]
    public async Task MissingOpenAiKey_PublishesConfigurationError()
    {
        var sink = new RecordingSink();
        var adapter = new SampleOrchestrationAgentAdapter(new MissingOpenAiKeyLlmClient());

        var input = CreateUserText("ordinary question");

        await adapter.HandleAsync(input, sink, CancellationToken.None);

        var output = Assert.Single(sink.Outputs);
        Assert.Equal(YatraMessageTypes.Error, output.Message.Type);
        Assert.Equal(input.Message.MessageId, output.Message.InReplyTo);
        Assert.Contains("openai_api_key_missing", output.Message.Payload.GetRawText());
        Assert.Contains("\"retryable\": false", output.Message.Payload.GetRawText());
    }

    [Fact]
    public async Task LlmCancellation_IsNotConvertedToUnavailableError()
    {
        var sink = new RecordingSink();
        var adapter = new SampleOrchestrationAgentAdapter(new CancellingLlmClient());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => adapter.HandleAsync(CreateUserText("ordinary question"), sink, cts.Token));

        Assert.Empty(sink.Outputs);
    }

    [Theory]
    [InlineData("submit", "received")]
    [InlineData("cancel", "cancelled")]
    public async Task FormResponse_PublishesRoutedConfirmationWithoutSensitiveValues(
        string action,
        string expectedText)
    {
        var sink = new RecordingSink();
        var adapter = new SampleOrchestrationAgentAdapter(new RecordingLlmClient());

        var input = CreateFormResponse(action);

        await adapter.HandleAsync(input, sink, CancellationToken.None);

        var output = Assert.Single(sink.Outputs);
        var rawPayload = output.Message.Payload.GetRawText();

        Assert.Equal(YatraMessageTypes.AgentText, output.Message.Type);
        Assert.Equal(input.Message.MessageId, output.Message.InReplyTo);
        Assert.Contains(expectedText, rawPayload);
        Assert.DoesNotContain("Month-end reporting", rawPayload);
    }

    [Fact]
    public async Task SampleSkillResponse_PublishesProcessedSkillResult()
    {
        var sink = new RecordingSink();
        var adapter = new SampleOrchestrationAgentAdapter(new RecordingLlmClient());

        var input = CreateFormResponse(
            "submit",
            formId: "sample-skill",
            values: new
            {
                objective = "Prepare demo notes",
                estimatedHours = 4,
                priority = "high",
                notes = "Keep it short"
            });

        await adapter.HandleAsync(input, sink, CancellationToken.None);

        var output = Assert.Single(sink.Outputs);
        var rawPayload = output.Message.Payload.GetRawText();

        Assert.Equal(YatraMessageTypes.AgentText, output.Message.Type);
        Assert.Equal(input.Message.MessageId, output.Message.InReplyTo);
        Assert.Contains("Sample skill processed objective", rawPayload);
        Assert.Contains("Prepare demo notes", rawPayload);
        Assert.Contains("4 estimated hours", rawPayload);
        Assert.Contains("high priority", rawPayload);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("skills/customer-onboarding")]
    public async Task FormResponse_RejectsMissingOrDifferentResolvedReturnPath(string? qualifiedPath)
    {
        var sink = new RecordingSink();
        var adapter = new SampleOrchestrationAgentAdapter(new RecordingLlmClient());
        var input = CreateFormResponse("submit", qualifiedPath);

        await adapter.HandleAsync(input, sink, CancellationToken.None);

        var output = Assert.Single(sink.Outputs);
        Assert.Equal(YatraMessageTypes.Error, output.Message.Type);
        Assert.Equal(input.Message.MessageId, output.Message.InReplyTo);
        Assert.Contains("invalid_return_path", output.Message.Payload.GetRawText());
    }

    [Theory]
    [InlineData("unknown", "1.0", "submit")]
    [InlineData("access-request", "2.0", "submit")]
    [InlineData("access-request", "1.0", "submit_bad_option")]
    public async Task FormResponse_RejectsInvalidSubmittedForms(
        string formId,
        string formVersion,
        string action)
    {
        var sink = new RecordingSink();
        var adapter = new SampleOrchestrationAgentAdapter(new RecordingLlmClient());
        var input = CreateFormResponse(action, "orchestrator/sample", formId, formVersion);

        await adapter.HandleAsync(input, sink, CancellationToken.None);

        var output = Assert.Single(sink.Outputs);
        Assert.Equal(YatraMessageTypes.Error, output.Message.Type);
        Assert.Equal(input.Message.MessageId, output.Message.InReplyTo);
        Assert.Contains("invalid_form_response", output.Message.Payload.GetRawText());
    }

    private static AgentInput CreateUserText(string text) =>
        new(CreateMessage(
            YatraMessageTypes.UserText,
            JsonSerializer.SerializeToElement(new { text }, YatraJson.SerializerOptions)));

    private static AgentInput CreateFormResponse(
        string action,
        string? qualifiedPath = "orchestrator/sample",
        string formId = "access-request",
        string formVersion = "1.0",
        object? values = null) =>
        new(CreateMessage(
            YatraMessageTypes.FormResponse,
            CreateFormResponsePayload(formId, formVersion, action, values)) with
            {
                InReplyTo = YatraUlid.NewUlid()
            },
            qualifiedPath is null ? null : new ReturnPath(qualifiedPath));

    private static JsonElement CreateFormResponsePayload(
        string formId,
        string formVersion,
        string action,
        object? submittedValues = null)
    {
        object values = submittedValues ?? action switch
        {
            "cancel" => new { },
            "submit_bad_option" => new
            {
                application = "bad",
                role = "viewer",
                justification = "Month-end reporting",
                accessEndDate = "2026-10-01"
            },
            "submit" when formId == "access-request" && formVersion == "1.0" => new
            {
                application = "finance",
                role = "viewer",
                justification = "Month-end reporting",
                accessEndDate = "2026-10-01"
            },
            _ => new { }
        };

        return JsonSerializer.SerializeToElement(
            new
            {
                formId,
                formVersion,
                action = action == "submit_bad_option" ? "submit" : action,
                values
            },
            YatraJson.SerializerOptions);
    }

    private static YatraMessage CreateMessage(string type, JsonElement payload) =>
        new()
        {
            MessageId = YatraUlid.NewUlid(),
            ConversationId = YatraUlid.NewUlid(),
            Type = type,
            Source = type == YatraMessageTypes.UserText ? "ui:current-conversation" : "engine:routing",
            Destination = "orchestrator:sample",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            InReplyTo = null,
            ExpectsReply = false,
            Payload = payload
        };

    private sealed class RecordingSink : IAgentOutputSink
    {
        public List<AgentOutput> Outputs { get; } = [];

        public ValueTask PublishAsync(AgentOutput output, CancellationToken cancellationToken)
        {
            Outputs.Add(output);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingLlmClient : ILlmClient
    {
        private readonly string _answer;

        public RecordingLlmClient(string answer = "Fake answer.")
        {
            _answer = answer;
        }

        public string? LastUserText { get; private set; }

        public Task<string> AskAsync(
            string conversationId,
            string userText,
            CancellationToken cancellationToken)
        {
            LastUserText = userText;
            return Task.FromResult(_answer);
        }
    }

    private sealed class ThrowingLlmClient : ILlmClient
    {
        public Task<string> AskAsync(
            string conversationId,
            string userText,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("LLM unavailable.");
        }
    }

    private sealed class MissingOpenAiKeyLlmClient : ILlmClient
    {
        public Task<string> AskAsync(
            string conversationId,
            string userText,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("OpenAI API key is not configured.");
        }
    }

    private sealed class CancellingLlmClient : ILlmClient
    {
        public Task<string> AskAsync(
            string conversationId,
            string userText,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new OperationCanceledException(cancellationToken);
        }
    }
}
