using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Yatra.AgentIntegration;
using Yatra.Contracts.Errors;
using Yatra.Contracts.Forms;
using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;
using Yatra.Orchestration.Commands;
using Yatra.Orchestration.Forms;
using Yatra.Orchestration.Llm;

namespace Yatra.Orchestration;

public sealed class SampleOrchestrationAgentAdapter : IOrchestrationAgentAdapter
{
    // Keep Fake as the distributable default. For private OpenAI testing, store the
    // real secret outside shared source, archives, and binaries; never commit it.
    private const string AiProvider = "Fake"; // Fake or OpenAI for this sample
    private const string AiModel = "gpt-4.1-mini";

    // Do not place a real API key in source code. Load it from a secure secrets
    // store, such as .NET User Secrets for local development or a managed vault
    // in deployed environments. Never commit, archive, log, or share the key.
    private const string AiApiKey = "<your-apikey-here>";
    private const int AiTimeoutSeconds = 30;

    internal static ILlmClient CreateLocalLlmClient()
    {
        var options = new OrchestrationOptions
        {
            Provider = AiProvider, Model = AiModel, TimeoutSeconds = AiTimeoutSeconds
        };
        var validation = new OrchestrationOptionsValidator().Validate(null, options);
        if (validation.Failed)
            throw new InvalidOperationException(string.Join(" ", validation.Failures));
        return string.Equals(AiProvider, "OpenAI", StringComparison.OrdinalIgnoreCase)
            ? new OpenAiLlmClient(options, AiApiKey)
            : new FakeLlmClient();
    }

    private const string AgentSource = "orchestrator:sample";
    private const string AgentReturnPath = "orchestrator/sample";
    private const string UiDestination = "ui:current-conversation";
    private readonly ILlmClient _llmClient;
    private readonly ILogger<SampleOrchestrationAgentAdapter> _logger;

    public SampleOrchestrationAgentAdapter(
        ILlmClient llmClient,
        ILogger<SampleOrchestrationAgentAdapter>? logger = null)
    {
        _llmClient = llmClient;
        _logger = logger ?? NullLogger<SampleOrchestrationAgentAdapter>.Instance;
    }

    public string AgentType => AgentSource;

    public async Task HandleAsync(
        AgentInput input,
        IAgentOutputSink output,
        CancellationToken cancellationToken)
    {
        switch (input.Message.Type)
        {
            case YatraMessageTypes.UserText:
                await HandleUserTextAsync(input.Message, output, cancellationToken);
                break;
            case YatraMessageTypes.FormResponse:
                await HandleFormResponseAsync(input, output, cancellationToken);
                break;
        }
    }

    private async Task HandleUserTextAsync(
        YatraMessage message,
        IAgentOutputSink output,
        CancellationToken cancellationToken)
    {
        var userText = ReadText(message);
        switch (SampleCommandMatcher.Match(userText))
        {
            case SampleCommand.ContactForm:
                await PublishFormAsync(message.ConversationId, message.MessageId, SampleFormFactory.CreateContactForm(), output, cancellationToken);
                break;
            case SampleCommand.AccessRequestForm:
                await PublishFormAsync(message.ConversationId, message.MessageId, SampleFormFactory.CreateAccessRequestForm(), output, cancellationToken);
                break;
            case SampleCommand.ConfirmationForm:
                await PublishFormAsync(message.ConversationId, message.MessageId, SampleFormFactory.CreateConfirmationForm(), output, cancellationToken);
                break;
            case SampleCommand.SampleSkill:
                await PublishFormAsync(message.ConversationId, message.MessageId, SampleFormFactory.CreateSampleSkillForm(), output, cancellationToken);
                break;
            default:
                await PublishLlmAnswerAsync(message.ConversationId, message.MessageId, userText, output, cancellationToken);
                break;
        }
    }

    private static async Task HandleFormResponseAsync(
        AgentInput input,
        IAgentOutputSink output,
        CancellationToken cancellationToken)
    {
        var message = input.Message;
        if (input.ResolvedReturnPath?.QualifiedPath != AgentReturnPath)
        {
            await output.PublishAsync(
                new AgentOutput(CreateErrorMessage(
                    message.ConversationId,
                    message.MessageId,
                    "invalid_return_path",
                    "The form response was not routed to the sample orchestrator.",
                    retryable: false)),
                cancellationToken);
            return;
        }

        var response = message.Payload.Deserialize<FormResponsePayload>(YatraJson.SerializerOptions);
        var validation = SampleFormResponseValidator.Validate(response);
        if (!validation.IsValid)
        {
            await output.PublishAsync(
                new AgentOutput(CreateErrorMessage(
                    message.ConversationId,
                    message.MessageId,
                    "invalid_form_response",
                    "The submitted form response is invalid.",
                    retryable: false)),
                cancellationToken);
            return;
        }

        var formId = string.IsNullOrWhiteSpace(response?.FormId) ? "form" : response.FormId;
        var text = response is { FormId: "sample-skill" }
            ? CreateSampleSkillResultText(response)
            : CreateFormConfirmationText(response, formId);

        await output.PublishAsync(
            new AgentOutput(CreateTextMessage(
                message.ConversationId,
                message.MessageId,
                text)),
            cancellationToken);
    }

    private static string CreateFormConfirmationText(FormResponsePayload? response, string formId)
    {
        var actionText = response?.Action == FormResponseAction.Cancel ? "cancelled" : "received";
        return $"Your {formId} response was {actionText} and routed back to the original requester.";
    }

    private static string CreateSampleSkillResultText(FormResponsePayload response)
    {
        if (response.Action == FormResponseAction.Cancel)
        {
            return "Sample skill was cancelled.";
        }

        var objective = ReadStringValue(response, "objective");
        var estimatedHours = ReadNumberValue(response, "estimatedHours");
        var priority = ReadStringValue(response, "priority");
        var notes = ReadStringValue(response, "notes");

        return string.IsNullOrWhiteSpace(notes)
            ? $"Sample skill processed objective '{objective}' with {priority} priority and {estimatedHours} estimated hours."
            : $"Sample skill processed objective '{objective}' with {priority} priority and {estimatedHours} estimated hours. Notes: {notes}";
    }

    private static string ReadStringValue(FormResponsePayload response, string fieldId)
    {
        if (!response.Values.TryGetValue(fieldId, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return value.GetString() ?? string.Empty;
    }

    private static string ReadNumberValue(FormResponsePayload response, string fieldId)
    {
        if (!response.Values.TryGetValue(fieldId, out var value) || !value.TryGetDecimal(out var number))
        {
            return "0";
        }

        return number.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task PublishLlmAnswerAsync(
        string conversationId,
        string inReplyTo,
        string userText,
        IAgentOutputSink output,
        CancellationToken cancellationToken)
    {
        try
        {
            var answer = await _llmClient.AskAsync(conversationId, userText, cancellationToken);
            await output.PublishAsync(
                new AgentOutput(CreateTextMessage(conversationId, inReplyTo, answer)),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains("OpenAI API key", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                exception,
                "OpenAI API key is not configured for message {MessageId} in conversation {ConversationId}.",
                inReplyTo,
                conversationId);

            await output.PublishAsync(
                new AgentOutput(CreateErrorMessage(
                    conversationId,
                    inReplyTo,
                    "openai_api_key_missing",
                    "OpenAI API key is not configured on the server.",
                    retryable: false)),
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to get LLM answer for message {MessageId} in conversation {ConversationId}.",
                inReplyTo,
                conversationId);

            await output.PublishAsync(
                new AgentOutput(CreateErrorMessage(
                    conversationId,
                    inReplyTo,
                    "llm_unavailable",
                    "The language model is unavailable right now.",
                    retryable: true)),
                cancellationToken);
        }
    }

    private static async Task PublishFormAsync(
        string conversationId,
        string inReplyTo,
        FormRequestPayload payload,
        IAgentOutputSink output,
        CancellationToken cancellationToken)
    {
        var message = new YatraMessage
        {
            MessageId = YatraUlid.NewUlid(),
            ConversationId = conversationId,
            Type = YatraMessageTypes.FormRequest,
            Source = AgentSource,
            Destination = UiDestination,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            InReplyTo = inReplyTo,
            ExpectsReply = true,
            Payload = JsonSerializer.SerializeToElement(payload, YatraJson.SerializerOptions)
        };

        await output.PublishAsync(
            new AgentOutput(message, new ReturnPath(AgentReturnPath)),
            cancellationToken);
    }

    private static YatraMessage CreateTextMessage(string conversationId, string inReplyTo, string text) =>
        new()
        {
            MessageId = YatraUlid.NewUlid(),
            ConversationId = conversationId,
            Type = YatraMessageTypes.AgentText,
            Source = AgentSource,
            Destination = UiDestination,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            InReplyTo = inReplyTo,
            ExpectsReply = false,
            Payload = JsonSerializer.SerializeToElement(new { text }, YatraJson.SerializerOptions)
        };

    private static YatraMessage CreateErrorMessage(
        string conversationId,
        string inReplyTo,
        string code,
        string message,
        bool retryable) =>
        new()
        {
            MessageId = YatraUlid.NewUlid(),
            ConversationId = conversationId,
            Type = YatraMessageTypes.Error,
            Source = AgentSource,
            Destination = UiDestination,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            InReplyTo = inReplyTo,
            ExpectsReply = false,
            Payload = JsonSerializer.SerializeToElement(
                new YatraErrorPayload
                {
                    Code = code,
                    Message = message,
                    Retryable = retryable
                },
                YatraJson.SerializerOptions)
        };

    private static string ReadText(YatraMessage message)
    {
        if (message.Payload.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
        {
            return text.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}
