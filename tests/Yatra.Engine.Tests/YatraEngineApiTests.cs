using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Yatra.AgentIntegration;
using Yatra.Contracts.Messages;
using Yatra.Engine.Streaming;

namespace Yatra.Engine.Tests;

public sealed class YatraEngineApiTests
{
    [Fact]
    public async Task StartupProcessesPersistedInboxEvenWhenHistoryWasNotWritten()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Yatra.Inbox.Recovery.Tests", Guid.NewGuid().ToString("N"));
        var conversationId = YatraUlid.NewUlid();
        var message = CreateMessage(conversationId, YatraMessageTypes.UserText);
        using (var state = new Yatra.Engine.Persistence.FileEngineStateStore(
            Microsoft.Extensions.Options.Options.Create(new Yatra.Engine.YatraEngineOptions { StorageFolder = folder })))
        {
            await state.AcceptAsync(message, CancellationToken.None);
        }
        var adapter = new RecordingAdapter();
        await using var factory = CreateFactory(adapter, ("Yatra:StorageFolder", folder));
        using var client = factory.CreateClient();
        Assert.Equal(message.MessageId, (await adapter.WaitForInputAsync()).Message.MessageId);
        var history = await factory.Services.GetRequiredService<Yatra.Engine.Persistence.IConversationMessageStore>()
            .ReadConversationAsync(conversationId, CancellationToken.None);
        Assert.Contains(history, item => item.MessageId == message.MessageId);
        var replay = await client.PostAsJsonAsync($"/api/conversations/{conversationId}/messages", message, YatraJson.SerializerOptions);
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
        Assert.Equal(1, adapter.Count);
    }

    [Fact]
    public async Task PostMessage_AcceptsPublishedFormResponseExample()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Yatra.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var protocol = await File.ReadAllTextAsync(Path.Combine(root.FullName, "docs", "Message-Protocol.md"));
        var examples = System.Text.RegularExpressions.Regex.Matches(protocol, @"```json\s*([\s\S]*?)```")
            .Select(match => JsonSerializer.Deserialize<JsonElement>(match.Groups[1].Value));
        var payload = Assert.Single(examples, example => example.TryGetProperty("action", out _));
        await using var factory = CreateFactory(new RecordingAdapter());
        var conversationId = YatraUlid.NewUlid();
        var submission = new { messageId = YatraUlid.NewUlid(), type = "form.response", inReplyTo = YatraUlid.NewUlid(), payload };
        var response = await factory.CreateClient().PostAsJsonAsync($"/api/conversations/{conversationId}/messages", submission);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var ack = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(submission.messageId, ack.GetProperty("messageId").GetString());
    }

    [Fact]
    public async Task PostMessage_ConcurrentReplayEnqueuesOnceAndConflictingContentIsRejected()
    {
        var adapter = new RecordingAdapter();
        await using var factory = CreateFactory(adapter);
        var client = factory.CreateClient();
        var conversationId = YatraUlid.NewUlid();
        var message = CreateMessage(conversationId, YatraMessageTypes.UserText);
        var url = $"/api/conversations/{conversationId}/messages";
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.PostAsJsonAsync(url, message, YatraJson.SerializerOptions)));
        var acknowledgements = new List<JsonElement>();
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            acknowledgements.Add(await response.Content.ReadFromJsonAsync<JsonElement>());
        }
        Assert.Single(acknowledgements, ack => !ack.GetProperty("replayed").GetBoolean());
        Assert.All(acknowledgements, ack => Assert.Equal(message.MessageId, ack.GetProperty("messageId").GetString()));
        await adapter.WaitForInputAsync();
        Assert.Equal(1, adapter.Count);

        var conflict = message with { Payload = JsonSerializer.SerializeToElement(new { text = "changed" }) };
        var rejected = await client.PostAsJsonAsync(url, conflict, YatraJson.SerializerOptions);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(1, adapter.Count);
    }

    [Fact]
    public async Task EventsEndpoint_ResumesAfterKnownCursor()
    {
        await using var factory = CreateFactory(new RecordingAdapter());
        var client = factory.CreateClient();
        var conversationId = YatraUlid.NewUlid();
        var first = CreateMessage(conversationId, YatraMessageTypes.UserText);
        var second = CreateMessage(conversationId, YatraMessageTypes.UserText);
        var url = $"/api/conversations/{conversationId}/messages";
        await client.PostAsJsonAsync(url, first, YatraJson.SerializerOptions);
        await client.PostAsJsonAsync(url, second, YatraJson.SerializerOptions);
        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get,
            $"/api/conversations/{conversationId}/events?lastEventId={first.MessageId}"), HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        Assert.Equal($"id: {second.MessageId}", await ReadNextNonCommentLineWithTimeoutAsync(reader));
    }

    [Fact]
    public async Task PostMessage_AcceptsValidUserTextAndWorkerPassesItToAdapter()
    {
        var adapter = new RecordingAdapter();
        await using var factory = CreateFactory(adapter);
        var client = factory.CreateClient();
        var conversationId = YatraUlid.NewUlid();
        var message = CreateMessage(conversationId, YatraMessageTypes.UserText);

        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{conversationId}/messages",
            message,
            YatraJson.SerializerOptions);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var input = await adapter.WaitForInputAsync();
        Assert.Equal(message.MessageId, input.Message.MessageId);
        Assert.Null(input.ResolvedReturnPath);
    }

    [Fact]
    public async Task PostMessage_OverwritesBrowserControlledRoutingFields()
    {
        var adapter = new RecordingAdapter();
        await using var factory = CreateFactory(adapter);
        var client = factory.CreateClient();
        var conversationId = YatraUlid.NewUlid();
        var submission = new
        {
            messageId = YatraUlid.NewUlid(),
            conversationId = YatraUlid.NewUlid(),
            type = YatraMessageTypes.UserText,
            source = "orchestrator:sample",
            destination = "ui:current-conversation",
            createdAtUtc = DateTimeOffset.Parse("2001-01-01T00:00:00Z"),
            expectsReply = true,
            payload = new { text = "hello" }
        };

        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{conversationId}/messages",
            submission,
            YatraJson.SerializerOptions);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var input = await adapter.WaitForInputAsync();
        Assert.Equal(conversationId, input.Message.ConversationId);
        Assert.Equal("ui:current-conversation", input.Message.Source);
        Assert.Equal("engine:inbound", input.Message.Destination);
        Assert.False(input.Message.ExpectsReply);
        Assert.NotEqual(DateTimeOffset.Parse("2001-01-01T00:00:00Z"), input.Message.CreatedAtUtc);
        Assert.True(input.Message.CreatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task PostMessage_RejectsServerToBrowserMessageType()
    {
        await using var factory = CreateFactory(new RecordingAdapter());
        var client = factory.CreateClient();
        var conversationId = YatraUlid.NewUlid();
        var message = CreateMessage(conversationId, YatraMessageTypes.AgentText);

        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{conversationId}/messages",
            message,
            YatraJson.SerializerOptions);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Only user.text and form.response", body);
    }

    [Fact]
    public async Task EventsEndpoint_ReturnsEventStreamAndFormatsPublishedMessage()
    {
        await using var factory = CreateFactory(new RecordingAdapter());
        var client = factory.CreateClient();
        var conversationId = YatraUlid.NewUlid();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/conversations/{conversationId}/events");

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var hub = factory.Services.GetRequiredService<IConversationStreamHub>();
        var message = CreateMessage(conversationId, YatraMessageTypes.AgentText);
        Assert.True(await hub.PublishAsync(message, CancellationToken.None));

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        var idLine = await ReadNextNonCommentLineWithTimeoutAsync(reader);
        var eventLine = await ReadLineWithTimeoutAsync(reader);
        var dataLine = await ReadLineWithTimeoutAsync(reader);
        var blankLine = await ReadLineWithTimeoutAsync(reader);

        Assert.Equal($"id: {message.MessageId}", idLine);
        Assert.Equal($"event: {message.Type}", eventLine);
        Assert.StartsWith("data: ", dataLine);
        Assert.Contains(message.MessageId, dataLine);
        Assert.Equal(string.Empty, blankLine);
    }

    [Fact]
    public async Task EventsEndpoint_ReplaysPersistedConversationMessages()
    {
        var adapter = new RecordingAdapter();
        await using var factory = CreateFactory(adapter);
        var client = factory.CreateClient();
        var conversationId = YatraUlid.NewUlid();
        var message = CreateMessage(conversationId, YatraMessageTypes.UserText);

        var postResponse = await client.PostAsJsonAsync(
            $"/api/conversations/{conversationId}/messages",
            message,
            YatraJson.SerializerOptions);
        Assert.Equal(HttpStatusCode.Accepted, postResponse.StatusCode);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/conversations/{conversationId}/events");
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        var idLine = await ReadNextNonCommentLineWithTimeoutAsync(reader);
        var eventLine = await ReadLineWithTimeoutAsync(reader);
        var dataLine = await ReadLineWithTimeoutAsync(reader);

        Assert.Equal($"id: {message.MessageId}", idLine);
        Assert.Equal($"event: {YatraMessageTypes.UserText}", eventLine);
        Assert.Contains(message.MessageId, dataLine);
    }

    [Fact]
    public async Task EventsEndpoint_RejectsInvalidConversationUlid()
    {
        await using var factory = CreateFactory(new RecordingAdapter());
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/conversations/not-a-ulid/events");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_ulid", body);
    }

    [Fact]
    public async Task EventsEndpoint_HeartbeatUsesSseCommentFormat()
    {
        await using var factory = CreateFactory(
            new RecordingAdapter(),
            ("Yatra:SseHeartbeatSeconds", "1"));
        var client = factory.CreateClient();
        var conversationId = YatraUlid.NewUlid();
        using var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{conversationId}/events"),
            HttpCompletionOption.ResponseHeadersRead);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        var heartbeatLine = await ReadCommentContainingAsync(reader, "heartbeat", TimeSpan.FromSeconds(3));

        Assert.StartsWith(": heartbeat ", heartbeatLine);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        RecordingAdapter adapter,
        params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    "Yatra:StorageFolder",
                    Path.Combine(Path.GetTempPath(), "Yatra.Engine.Api.Tests", Guid.NewGuid().ToString("N")));

                foreach (var (key, value) in settings)
                {
                    builder.UseSetting(key, value);
                }

                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IOrchestrationAgentAdapter>(adapter);
                });
            });

    private static async Task<string?> ReadLineWithTimeoutAsync(StreamReader reader) =>
        await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));

    private static async Task<string?> ReadNextNonCommentLineWithTimeoutAsync(StreamReader reader)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var line = await ReadLineWithTimeoutAsync(reader);
            if (line is null)
            {
                return null;
            }

            if (line.Length == 0 || line.StartsWith(':'))
            {
                continue;
            }

            return line;
        }

        throw new TimeoutException("No SSE message line was received before the timeout.");
    }

    private static async Task<string> ReadCommentContainingAsync(
        StreamReader reader,
        string text,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
            if (line is not null && line.StartsWith(':') && line.Contains(text))
            {
                return line;
            }
        }

        throw new TimeoutException($"No SSE comment containing '{text}' was received.");
    }

    private static YatraMessage CreateMessage(string conversationId, string type) =>
        new()
        {
            MessageId = YatraUlid.NewUlid(),
            ConversationId = conversationId,
            Type = type,
            Source = type == YatraMessageTypes.UserText ? "ui:current-conversation" : "orchestrator:sample",
            Destination = type == YatraMessageTypes.UserText ? "engine:inbound" : "ui:current-conversation",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            InReplyTo = null,
            ExpectsReply = false,
            Payload = JsonSerializer.SerializeToElement(new { text = "hello" }, YatraJson.SerializerOptions)
        };

    private sealed class RecordingAdapter : IOrchestrationAgentAdapter
    {
        private int _count;
        public int Count => _count;
        private readonly TaskCompletionSource<AgentInput> _input = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string AgentType => "test";

        public Task HandleAsync(
            AgentInput input,
            IAgentOutputSink output,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            _input.TrySetResult(input);
            return Task.CompletedTask;
        }

        public async Task<AgentInput> WaitForInputAsync() =>
            await _input.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
