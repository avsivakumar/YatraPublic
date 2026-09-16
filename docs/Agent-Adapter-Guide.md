# Agent Adapter Guide

## Dynamic form reliability

Queued input survives restart. Adapter outputs and successful processing checkpoints commit atomically in the engine's JSON state. A crash during an external side effect can cause the same `AgentInput.Message.MessageId` to execute again, so deduplicate side effects at the external boundary. See [Persistence](Persistence.md).

[Message Protocol](Message-Protocol.md) is the definitive request, response, field, acknowledgement, and retry contract. Use existing `form.request`/`form.response`, `formVersion`, field `id`, and `inReplyTo`. Version 1 adds datetime, multiselect, display, hidden, initial values, read-only/disabled state, help text, and type-specific constraints.

HTTP acceptance confirms queueing, not business success. Adapter validation remains authoritative. Retryable application errors can reopen acknowledged forms. Transport retries are deduplicated at HTTP acceptance. A revised response or new attempt after explicit asynchronous rejection has a new message ID and `replacesResponseId`. Never trust hidden browser values for routing or authorization.

An agent adapter connects Yatra.Engine to a concrete requester or orchestration system. The engine knows only the interfaces in `Yatra.AgentIntegration`; it must not reference concrete orchestration projects.

## Implement the Adapter

Create a class that implements `IOrchestrationAgentAdapter`:

```csharp
public sealed class MyAgentAdapter : IOrchestrationAgentAdapter
{
    public string AgentType => "orchestrator:my-agent";

    public async Task HandleAsync(
        AgentInput input,
        IAgentOutputSink output,
        CancellationToken cancellationToken)
    {
        // Read input.Message and publish zero or more AgentOutput messages.
    }
}
```

`AgentInput` contains:

- `Message`: the trusted internal `YatraMessage`.
- `ResolvedReturnPath`: set for claimed `form.response` messages.

`IAgentOutputSink` is the only supported way to publish messages from an adapter.

## Publish Text

Set `InReplyTo` to the triggering message ID:

```csharp
var message = new YatraMessage
{
    MessageId = YatraUlid.NewUlid(),
    ConversationId = input.Message.ConversationId,
    Type = YatraMessageTypes.AgentText,
    Source = "orchestrator:my-agent",
    Destination = "ui:current-conversation",
    CreatedAtUtc = DateTimeOffset.UtcNow,
    InReplyTo = input.Message.MessageId,
    ExpectsReply = false,
    Payload = JsonSerializer.SerializeToElement(new { text = "Hello" }, YatraJson.SerializerOptions)
};

await output.PublishAsync(new AgentOutput(message), cancellationToken);
```

## Request a Form

For a form that expects a browser reply:

1. Publish a `form.request`.
2. Set `ExpectsReply = true`.
3. Provide a nonblank `ReturnPath`.
4. Set `InReplyTo` to the inbound message that caused the form.

```csharp
await output.PublishAsync(
    new AgentOutput(formRequestMessage, new ReturnPath("orchestrator/my-agent")),
    cancellationToken);
```

The engine registers the pending reply before the message is queued to the browser.

## Handle Form Responses

When handling `form.response`, verify that the resolved return path belongs to your adapter:

```csharp
if (input.ResolvedReturnPath?.QualifiedPath != "orchestrator/my-agent")
{
    // Reject or ignore. This response was not routed to this requester.
}
```

The engine already validates and claims the pending reply before delivery, but the adapter should still validate domain-specific payload rules. The sample uses `IFormResponseValidator` and `SampleFormResponseValidator`.

Successful acceptance matters: the engine marks the pending reply completed or cancelled only after `HandleAsync` returns successfully.

## Validation

Use `IFormResponseValidator` for pluggable server-side validation:

```csharp
public sealed class MyFormResponseValidator : IFormResponseValidator
{
    public ValueTask<bool> IsValidAsync(
        FormResponsePayload response,
        PendingReply pendingReply,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(true);
    }
}
```

Validators should reject:

- Unknown form IDs.
- Unsupported form versions.
- Undefined fields.
- Missing required fields on submit.
- Wrong value types.
- Invalid length, range, pattern, or option values.

If a validator throws, the engine releases the pending-reply claim and publishes a retryable routing error when possible.

## Cancellation

Do not convert caller cancellation into normal application errors. Let `OperationCanceledException` flow when the provided cancellation token is cancelled.

Internal timeouts may be translated into retryable application errors if that is the adapter's intended behavior.

## Dependency Injection

Register the engine and one concrete adapter in the composition root:

```csharp
builder.Services.AddYatraEngine(builder.Configuration);
builder.Services.AddSingleton<IOrchestrationAgentAdapter, MyAgentAdapter>();
builder.Services.AddSingleton<IFormResponseValidator, MyFormResponseValidator>();
```

The included sample host instead calls:

```csharp
builder.Services.AddSampleYatraOrchestration();
```

Keep this registration in the app host or integration package. Do not add a project reference from `Yatra.Engine` to a concrete orchestration implementation.

## OpenAI Configuration

The sample orchestration package supports:

- `AiProvider = "Fake"`
- `AiProvider = "OpenAI"`

Edit the private `AiProvider`, `AiModel`, `AiApiKey`, and `AiTimeoutSeconds` constants in `SampleOrchestrationAgentAdapter.cs` for local work. Rebuild and restart after edits. Do not share or commit populated credentials, including compiled binaries. The engine remains provider-independent; tests inject an `ILlmClient` implementation.
