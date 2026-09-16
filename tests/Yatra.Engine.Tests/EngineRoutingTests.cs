using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Yatra.AgentIntegration;
using Yatra.Contracts.Forms;
using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;
using Yatra.Engine.Queues;
using Yatra.Engine.Routing;
using Yatra.Engine.Workers;

namespace Yatra.Engine.Tests;

public sealed class EngineRoutingTests
{
    [Fact]
    public async Task OutputSink_RegistersPendingReplyBeforeOutboundQueueing()
    {
        var outboundQueue = new ChannelOutboundMessageQueue();
        var registry = new InMemoryPendingReplyRegistry();
        var sink = new EngineAgentOutputSink(
            outboundQueue,
            registry,
            Options.Create(new YatraEngineOptions { PendingReplyLifetimeMinutes = 30 }));
        var message = CreateMessage(YatraMessageTypes.FormRequest, expectsReply: true);
        var returnPath = new ReturnPath("orchestrator/sample");

        await sink.PublishAsync(new AgentOutput(message, returnPath), CancellationToken.None);

        var pendingReply = await registry.ResolveAsync(message.MessageId, message.ConversationId);
        Assert.NotNull(pendingReply);
        Assert.Equal(returnPath, pendingReply.ReturnPath);
        Assert.Equal("contact", pendingReply.ExpectedFormId);
        Assert.Equal("1.0", pendingReply.ExpectedFormVersion);
        Assert.Equal("contact", pendingReply.ValidatorKey);

        var outbound = await ReadOutboundAsync(outboundQueue, count: 1);
        Assert.Equal(message.MessageId, outbound[0].Message.MessageId);
    }

    [Fact]
    public async Task OutputSink_RejectsReplyExpectedMessageWithoutReturnPath()
    {
        var sink = new EngineAgentOutputSink(
            new ChannelOutboundMessageQueue(),
            new InMemoryPendingReplyRegistry(),
            Options.Create(new YatraEngineOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sink.PublishAsync(
                new AgentOutput(CreateMessage(YatraMessageTypes.FormRequest, expectsReply: true)),
                CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task InboundWorker_RoutesFormResponseWithResolvedReturnPath()
    {
        var inboundQueue = new ChannelInboundMessageQueue();
        var outboundQueue = new ChannelOutboundMessageQueue();
        var registry = new InMemoryPendingReplyRegistry();
        var adapter = new RecordingAdapter();
        var sink = new EngineAgentOutputSink(outboundQueue, registry, Options.Create(new YatraEngineOptions()));
        var worker = new InboundMessageWorker(
            inboundQueue,
            registry,
            adapter,
            Array.Empty<IFormResponseValidator>(),
            sink,
            NullLogger<InboundMessageWorker>.Instance);
        var conversationId = YatraUlid.NewUlid();
        var requestMessageId = YatraUlid.NewUlid();
        var returnPath = new ReturnPath("orchestrator/sample");

        await registry.RegisterAsync(new PendingReply(
            requestMessageId,
            conversationId,
            returnPath,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(30),
            PendingReplyStatus.Pending));

        await worker.StartAsync(CancellationToken.None);
        await inboundQueue.WriteAsync(CreateFormResponse(conversationId, requestMessageId), CancellationToken.None);

        var input = await adapter.WaitForInputAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(requestMessageId, input.Message.InReplyTo);
        Assert.Equal(returnPath, input.ResolvedReturnPath);
        Assert.Equal(
            PendingReplyResolutionStatus.AlreadyCompleted,
            (await registry.ClaimForResponseAsync(requestMessageId, conversationId)).Status);
    }

    [Fact]
    public async Task InboundWorker_RejectsInvalidFormResponseBeforeClaimingPendingReply()
    {
        var inboundQueue = new ChannelInboundMessageQueue();
        var outboundQueue = new ChannelOutboundMessageQueue();
        var registry = new InMemoryPendingReplyRegistry();
        var adapter = new RecordingAdapter();
        var sink = new EngineAgentOutputSink(outboundQueue, registry, Options.Create(new YatraEngineOptions()));
        var worker = new InboundMessageWorker(
            inboundQueue,
            registry,
            adapter,
            [new RejectingFormResponseValidator()],
            sink,
            NullLogger<InboundMessageWorker>.Instance);
        var conversationId = YatraUlid.NewUlid();
        var requestMessageId = YatraUlid.NewUlid();

        await registry.RegisterAsync(new PendingReply(
            requestMessageId,
            conversationId,
            new ReturnPath("orchestrator/sample"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(30),
            PendingReplyStatus.Pending));

        await worker.StartAsync(CancellationToken.None);
        await inboundQueue.WriteAsync(CreateFormResponse(conversationId, requestMessageId), CancellationToken.None);

        var errors = await ReadOutboundAsync(outboundQueue, count: 1);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(YatraMessageTypes.Error, errors[0].Message.Type);
        Assert.Contains("invalid_form_response", errors[0].Message.Payload.GetRawText());
        Assert.True(errors[0].Message.Payload.GetProperty("retryable").GetBoolean());
        Assert.False(adapter.HasInput);
        Assert.NotNull(await registry.ResolveAsync(requestMessageId, conversationId));
    }

    [Fact]
    public async Task InboundWorker_RejectsResponseWhoseFormDoesNotMatchPendingReplyMetadata()
    {
        var inboundQueue = new ChannelInboundMessageQueue();
        var outboundQueue = new ChannelOutboundMessageQueue();
        var registry = new InMemoryPendingReplyRegistry();
        var adapter = new RecordingAdapter();
        var sink = new EngineAgentOutputSink(outboundQueue, registry, Options.Create(new YatraEngineOptions()));
        var worker = new InboundMessageWorker(
            inboundQueue,
            registry,
            adapter,
            Array.Empty<IFormResponseValidator>(),
            sink,
            NullLogger<InboundMessageWorker>.Instance);
        var conversationId = YatraUlid.NewUlid();
        var requestMessageId = YatraUlid.NewUlid();

        await registry.RegisterAsync(new PendingReply(
            requestMessageId,
            conversationId,
            new ReturnPath("orchestrator/sample"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(30),
            PendingReplyStatus.Pending,
            ExpectedFormId: "contact",
            ExpectedFormVersion: "1.0",
            ValidatorKey: "contact"));

        await worker.StartAsync(CancellationToken.None);
        await inboundQueue.WriteAsync(CreateFormResponse(
            conversationId,
            requestMessageId,
            formId: "access-request",
            formVersion: "1.0"), CancellationToken.None);

        var errors = await ReadOutboundAsync(outboundQueue, count: 1);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(YatraMessageTypes.Error, errors[0].Message.Type);
        Assert.Contains("invalid_form_response", errors[0].Message.Payload.GetRawText());
        Assert.False(adapter.HasInput);
        Assert.NotNull(await registry.ResolveAsync(requestMessageId, conversationId));
    }

    [Fact]
    public async Task InboundWorker_MarksCancelledWhenFormResponseActionIsCancel()
    {
        var inboundQueue = new ChannelInboundMessageQueue();
        var outboundQueue = new ChannelOutboundMessageQueue();
        var registry = new InMemoryPendingReplyRegistry();
        var adapter = new RecordingAdapter();
        var sink = new EngineAgentOutputSink(outboundQueue, registry, Options.Create(new YatraEngineOptions()));
        var worker = new InboundMessageWorker(
            inboundQueue,
            registry,
            adapter,
            Array.Empty<IFormResponseValidator>(),
            sink,
            NullLogger<InboundMessageWorker>.Instance);
        var conversationId = YatraUlid.NewUlid();
        var requestMessageId = YatraUlid.NewUlid();

        await registry.RegisterAsync(new PendingReply(
            requestMessageId,
            conversationId,
            new ReturnPath("orchestrator/sample"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(30),
            PendingReplyStatus.Pending));

        await worker.StartAsync(CancellationToken.None);
        await inboundQueue.WriteAsync(CreateFormResponse(conversationId, requestMessageId, "cancel"), CancellationToken.None);

        _ = await adapter.WaitForInputAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(
            PendingReplyResolutionStatus.Cancelled,
            (await registry.ClaimForResponseAsync(requestMessageId, conversationId)).Status);
    }

    [Fact]
    public async Task InboundWorker_ReleasesClaimWhenAdapterThrows()
    {
        var inboundQueue = new ChannelInboundMessageQueue();
        var outboundQueue = new ChannelOutboundMessageQueue();
        var registry = new InMemoryPendingReplyRegistry();
        var adapter = new ThrowingAdapter();
        var sink = new EngineAgentOutputSink(outboundQueue, registry, Options.Create(new YatraEngineOptions()));
        var worker = new InboundMessageWorker(
            inboundQueue,
            registry,
            adapter,
            Array.Empty<IFormResponseValidator>(),
            sink,
            NullLogger<InboundMessageWorker>.Instance);
        var conversationId = YatraUlid.NewUlid();
        var requestMessageId = YatraUlid.NewUlid();

        await registry.RegisterAsync(new PendingReply(
            requestMessageId,
            conversationId,
            new ReturnPath("orchestrator/sample"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(30),
            PendingReplyStatus.Pending));

        await worker.StartAsync(CancellationToken.None);
        await inboundQueue.WriteAsync(CreateFormResponse(conversationId, requestMessageId), CancellationToken.None);

        await adapter.WaitForCallAsync();
        var errors = await ReadOutboundAsync(outboundQueue, count: 1);
        await worker.StopAsync(CancellationToken.None);

        var retryClaim = await registry.ClaimForResponseAsync(requestMessageId, conversationId);
        Assert.Equal(PendingReplyResolutionStatus.Resolved, retryClaim.Status);
        Assert.Equal(YatraMessageTypes.Error, errors[0].Message.Type);
        Assert.Contains("requester_unavailable", errors[0].Message.Payload.GetRawText());
        Assert.Contains("\"retryable\": true", errors[0].Message.Payload.GetRawText());
    }

    [Fact]
    public async Task InboundWorker_ReleasesClaimWhenFormResponseValidatorThrows()
    {
        var inboundQueue = new ChannelInboundMessageQueue();
        var outboundQueue = new ChannelOutboundMessageQueue();
        var registry = new InMemoryPendingReplyRegistry();
        var adapter = new RecordingAdapter();
        var sink = new EngineAgentOutputSink(outboundQueue, registry, Options.Create(new YatraEngineOptions()));
        var worker = new InboundMessageWorker(
            inboundQueue,
            registry,
            adapter,
            [new ThrowingFormResponseValidator()],
            sink,
            NullLogger<InboundMessageWorker>.Instance);
        var conversationId = YatraUlid.NewUlid();
        var requestMessageId = YatraUlid.NewUlid();

        await registry.RegisterAsync(new PendingReply(
            requestMessageId,
            conversationId,
            new ReturnPath("orchestrator/sample"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(30),
            PendingReplyStatus.Pending));

        await worker.StartAsync(CancellationToken.None);
        await inboundQueue.WriteAsync(CreateFormResponse(conversationId, requestMessageId), CancellationToken.None);

        var errors = await ReadOutboundAsync(outboundQueue, count: 1);
        await worker.StopAsync(CancellationToken.None);

        var retryClaim = await registry.ClaimForResponseAsync(requestMessageId, conversationId);
        Assert.Equal(PendingReplyResolutionStatus.Resolved, retryClaim.Status);
        Assert.Equal(YatraMessageTypes.Error, errors[0].Message.Type);
        Assert.Contains("requester_unavailable", errors[0].Message.Payload.GetRawText());
        Assert.Contains("\"retryable\": true", errors[0].Message.Payload.GetRawText());
        Assert.False(adapter.HasInput);
    }

    [Fact]
    public async Task InboundWorker_PublishesErrorForUnknownFormResponse()
    {
        var inboundQueue = new ChannelInboundMessageQueue();
        var outboundQueue = new ChannelOutboundMessageQueue();
        var registry = new InMemoryPendingReplyRegistry();
        var adapter = new RecordingAdapter();
        var sink = new EngineAgentOutputSink(outboundQueue, registry, Options.Create(new YatraEngineOptions()));
        var worker = new InboundMessageWorker(
            inboundQueue,
            registry,
            adapter,
            Array.Empty<IFormResponseValidator>(),
            sink,
            NullLogger<InboundMessageWorker>.Instance);
        var conversationId = YatraUlid.NewUlid();
        var requestMessageId = YatraUlid.NewUlid();

        await worker.StartAsync(CancellationToken.None);
        await inboundQueue.WriteAsync(CreateFormResponse(conversationId, requestMessageId), CancellationToken.None);

        var errors = await ReadOutboundAsync(outboundQueue, count: 1);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(YatraMessageTypes.Error, errors[0].Message.Type);
        Assert.Contains("pending_reply_not_found", errors[0].Message.Payload.GetRawText());
        Assert.False(adapter.HasInput);
    }

    [Fact]
    public async Task InboundWorker_PublishesErrorForDuplicateFormResponse()
    {
        var inboundQueue = new ChannelInboundMessageQueue();
        var outboundQueue = new ChannelOutboundMessageQueue();
        var registry = new InMemoryPendingReplyRegistry();
        var adapter = new RecordingAdapter();
        var sink = new EngineAgentOutputSink(outboundQueue, registry, Options.Create(new YatraEngineOptions()));
        var worker = new InboundMessageWorker(
            inboundQueue,
            registry,
            adapter,
            Array.Empty<IFormResponseValidator>(),
            sink,
            NullLogger<InboundMessageWorker>.Instance);
        var conversationId = YatraUlid.NewUlid();
        var requestMessageId = YatraUlid.NewUlid();

        await registry.RegisterAsync(new PendingReply(
            requestMessageId,
            conversationId,
            new ReturnPath("orchestrator/sample"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(30),
            PendingReplyStatus.Pending));
        await registry.ClaimForResponseAsync(requestMessageId, conversationId);
        await registry.CompleteAsync(requestMessageId);

        await worker.StartAsync(CancellationToken.None);
        await inboundQueue.WriteAsync(CreateFormResponse(conversationId, requestMessageId), CancellationToken.None);

        var errors = await ReadOutboundAsync(outboundQueue, count: 1);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(YatraMessageTypes.Error, errors[0].Message.Type);
        Assert.Contains("duplicate_response", errors[0].Message.Payload.GetRawText());
        Assert.False(adapter.HasInput);
    }

    [Fact]
    public async Task InboundWorker_PublishesErrorForCrossConversationFormResponse()
    {
        var inboundQueue = new ChannelInboundMessageQueue();
        var outboundQueue = new ChannelOutboundMessageQueue();
        var registry = new InMemoryPendingReplyRegistry();
        var adapter = new RecordingAdapter();
        var sink = new EngineAgentOutputSink(outboundQueue, registry, Options.Create(new YatraEngineOptions()));
        var worker = new InboundMessageWorker(
            inboundQueue,
            registry,
            adapter,
            Array.Empty<IFormResponseValidator>(),
            sink,
            NullLogger<InboundMessageWorker>.Instance);
        var requestMessageId = YatraUlid.NewUlid();

        await registry.RegisterAsync(new PendingReply(
            requestMessageId,
            YatraUlid.NewUlid(),
            new ReturnPath("orchestrator/sample"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(30),
            PendingReplyStatus.Pending));

        await worker.StartAsync(CancellationToken.None);
        await inboundQueue.WriteAsync(CreateFormResponse(YatraUlid.NewUlid(), requestMessageId), CancellationToken.None);

        var errors = await ReadOutboundAsync(outboundQueue, count: 1);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(YatraMessageTypes.Error, errors[0].Message.Type);
        Assert.Contains("conversation_mismatch", errors[0].Message.Payload.GetRawText());
        Assert.False(adapter.HasInput);
    }

    [Fact]
    public async Task InboundWorker_PublishesErrorForExpiredFormResponse()
    {
        var inboundQueue = new ChannelInboundMessageQueue();
        var outboundQueue = new ChannelOutboundMessageQueue();
        var registry = new InMemoryPendingReplyRegistry();
        var adapter = new RecordingAdapter();
        var sink = new EngineAgentOutputSink(outboundQueue, registry, Options.Create(new YatraEngineOptions()));
        var worker = new InboundMessageWorker(
            inboundQueue,
            registry,
            adapter,
            Array.Empty<IFormResponseValidator>(),
            sink,
            NullLogger<InboundMessageWorker>.Instance);
        var conversationId = YatraUlid.NewUlid();
        var requestMessageId = YatraUlid.NewUlid();

        await registry.RegisterAsync(new PendingReply(
            requestMessageId,
            conversationId,
            new ReturnPath("orchestrator/sample"),
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow.AddMinutes(-1),
            PendingReplyStatus.Pending));

        await worker.StartAsync(CancellationToken.None);
        await inboundQueue.WriteAsync(CreateFormResponse(conversationId, requestMessageId), CancellationToken.None);

        var errors = await ReadOutboundAsync(outboundQueue, count: 1);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(YatraMessageTypes.Error, errors[0].Message.Type);
        Assert.Contains("pending_reply_expired", errors[0].Message.Payload.GetRawText());
        Assert.False(adapter.HasInput);
    }

    private static async Task<List<RoutableMessage>> ReadOutboundAsync(
        IOutboundMessageQueue queue,
        int count)
    {
        var received = new List<RoutableMessage>();

        await foreach (var message in queue.ReadAllAsync(CancellationToken.None))
        {
            received.Add(message);
            if (received.Count == count)
            {
                break;
            }
        }

        return received;
    }

    private static YatraMessage CreateFormResponse(
        string conversationId,
        string inReplyTo,
        string action = "submit",
        string formId = "contact",
        string formVersion = "1.0") =>
        new()
        {
            MessageId = YatraUlid.NewUlid(),
            ConversationId = conversationId,
            Type = YatraMessageTypes.FormResponse,
            Source = "ui:current-conversation",
            Destination = "engine:inbound",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            InReplyTo = inReplyTo,
            ExpectsReply = false,
            Payload = JsonSerializer.SerializeToElement(
                new
                {
                    formId,
                    formVersion,
                    action,
                    values = new { name = "Asha" }
                },
                YatraJson.SerializerOptions)
        };

    private static YatraMessage CreateMessage(string type, bool expectsReply = false) =>
        new()
        {
            MessageId = YatraUlid.NewUlid(),
            ConversationId = YatraUlid.NewUlid(),
            Type = type,
            Source = "orchestrator:sample",
            Destination = "ui:current-conversation",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            InReplyTo = null,
            ExpectsReply = expectsReply,
            Payload = type == YatraMessageTypes.FormRequest
                ? JsonSerializer.SerializeToElement(
                    new FormRequestPayload
                    {
                        FormId = "contact",
                        FormVersion = "1.0",
                        Title = "Contact",
                        SubmitLabel = "Send",
                        CancelLabel = "Cancel",
                        Fields =
                        [
                            new FormFieldDefinition
                            {
                                Id = "name",
                                Type = FormFieldType.Text,
                                Label = "Name",
                                Required = true
                            }
                        ]
                    },
                    YatraJson.SerializerOptions)
                : JsonSerializer.SerializeToElement(new { text = "hello" }, YatraJson.SerializerOptions)
        };

    private sealed class RecordingAdapter : IOrchestrationAgentAdapter
    {
        private readonly TaskCompletionSource<AgentInput> _input = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string AgentType => "test";

        public bool HasInput => _input.Task.IsCompletedSuccessfully;

        public Task HandleAsync(
            AgentInput input,
            IAgentOutputSink output,
            CancellationToken cancellationToken)
        {
            _input.TrySetResult(input);
            return Task.CompletedTask;
        }

        public async Task<AgentInput> WaitForInputAsync() =>
            await _input.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class RejectingFormResponseValidator : IFormResponseValidator
    {
        public ValueTask<bool> IsValidAsync(
            Yatra.Contracts.Forms.FormResponsePayload response,
            PendingReply pendingReply,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(false);
        }
    }

    private sealed class ThrowingFormResponseValidator : IFormResponseValidator
    {
        public ValueTask<bool> IsValidAsync(
            Yatra.Contracts.Forms.FormResponsePayload response,
            PendingReply pendingReply,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Validator failed.");
        }
    }

    private sealed class ThrowingAdapter : IOrchestrationAgentAdapter
    {
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string AgentType => "test";

        public Task HandleAsync(
            AgentInput input,
            IAgentOutputSink output,
            CancellationToken cancellationToken)
        {
            _called.TrySetResult();
            throw new InvalidOperationException("Adapter failed.");
        }

        public async Task WaitForCallAsync() =>
            await _called.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
