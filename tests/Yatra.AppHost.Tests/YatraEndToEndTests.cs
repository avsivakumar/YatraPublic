extern alias AppHost;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Yatra.Contracts.Forms;
using Yatra.Contracts.Messages;

namespace Yatra.AppHost.Tests;

public sealed class YatraEndToEndTests
{
    [Fact]
    public async Task UserQuestion_ReceivesFakeLlmAnswer()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var conversationId = YatraUlid.NewUlid();
        await using var stream = await SseConversation.OpenAsync(client, conversationId);

        var userMessageId = await PostUserTextAsync(client, conversationId, "what can you do?");
        var answer = await stream.ReadNextMessageAsync(YatraMessageTypes.AgentText);

        Assert.Equal(conversationId, answer.ConversationId);
        Assert.Equal(userMessageId, answer.InReplyTo);
        Assert.Contains("Fake LLM response", answer.Payload.GetProperty("text").GetString());
    }

    [Fact]
    public async Task ContactForm_SubmitsAndReceivesRequesterConfirmation()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var conversationId = YatraUlid.NewUlid();
        await using var stream = await SseConversation.OpenAsync(client, conversationId);

        var requestTextId = await PostUserTextAsync(client, conversationId, "show contact form");
        var form = await stream.ReadNextMessageAsync(YatraMessageTypes.FormRequest);
        var responseMessageId = await PostContactResponseAsync(client, conversationId, form.MessageId);
        var confirmation = await stream.ReadNextMessageAsync(YatraMessageTypes.AgentText);

        Assert.Equal(requestTextId, form.InReplyTo);
        Assert.Equal(responseMessageId, confirmation.InReplyTo);
        Assert.Contains("contact response was received", confirmation.Payload.GetProperty("text").GetString());
    }

    [Fact]
    public async Task MultipleForms_SubmittedInReverseOrder_ReturnToOriginalRequester()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var conversationId = YatraUlid.NewUlid();
        await using var stream = await SseConversation.OpenAsync(client, conversationId);

        await PostUserTextAsync(client, conversationId, "show contact form");
        var contactForm = await stream.ReadNextMessageAsync(YatraMessageTypes.FormRequest);
        await PostUserTextAsync(client, conversationId, "request application access");
        var accessForm = await stream.ReadNextMessageAsync(YatraMessageTypes.FormRequest);

        var accessResponseId = await PostAccessResponseAsync(client, conversationId, accessForm.MessageId);
        var accessConfirmation = await stream.ReadNextMessageAsync(YatraMessageTypes.AgentText);
        var contactResponseId = await PostContactResponseAsync(client, conversationId, contactForm.MessageId);
        var contactConfirmation = await stream.ReadNextMessageAsync(YatraMessageTypes.AgentText);

        Assert.Equal(accessResponseId, accessConfirmation.InReplyTo);
        Assert.Contains("access-request response was received", accessConfirmation.Payload.GetProperty("text").GetString());
        Assert.Equal(contactResponseId, contactConfirmation.InReplyTo);
        Assert.Contains("contact response was received", contactConfirmation.Payload.GetProperty("text").GetString());
    }

    [Fact]
    public async Task DuplicateFormResponse_IsRejected()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var conversationId = YatraUlid.NewUlid();
        await using var stream = await SseConversation.OpenAsync(client, conversationId);

        await PostUserTextAsync(client, conversationId, "show contact form");
        var form = await stream.ReadNextMessageAsync(YatraMessageTypes.FormRequest);
        await PostContactResponseAsync(client, conversationId, form.MessageId);
        _ = await stream.ReadNextMessageAsync(YatraMessageTypes.AgentText);

        await PostContactResponseAsync(client, conversationId, form.MessageId);
        var error = await stream.ReadNextMessageAsync(YatraMessageTypes.Error);

        Assert.Equal("duplicate_response", error.Payload.GetProperty("code").GetString());
        Assert.Equal(form.MessageId, error.Payload.GetProperty("relatedMessageId").GetString());
    }

    [Fact]
    public async Task CrossConversationFormResponse_IsRejected()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var sourceConversationId = YatraUlid.NewUlid();
        var otherConversationId = YatraUlid.NewUlid();
        await using var sourceStream = await SseConversation.OpenAsync(client, sourceConversationId);
        await using var otherStream = await SseConversation.OpenAsync(client, otherConversationId);

        await PostUserTextAsync(client, sourceConversationId, "show contact form");
        var sourceForm = await sourceStream.ReadNextMessageAsync(YatraMessageTypes.FormRequest);
        await PostContactResponseAsync(client, otherConversationId, sourceForm.MessageId);
        var error = await otherStream.ReadNextMessageAsync(YatraMessageTypes.Error);

        Assert.Equal("conversation_mismatch", error.Payload.GetProperty("code").GetString());
        Assert.Equal(sourceForm.MessageId, error.Payload.GetProperty("relatedMessageId").GetString());
    }

    [Fact]
    public async Task ReconnectedStream_ReceivesFutureMessages()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var conversationId = YatraUlid.NewUlid();
        await using (await SseConversation.OpenAsync(client, conversationId))
        {
        }

        await using var reconnectedStream = await SseConversation.OpenAsync(client, conversationId);
        var userMessageId = await PostUserTextAsync(client, conversationId, "confirm an action");
        var form = await reconnectedStream.ReadNextMessageAsync(YatraMessageTypes.FormRequest);

        Assert.Equal(userMessageId, form.InReplyTo);
        Assert.Equal("confirmation", form.Payload.GetProperty("formId").GetString());
    }

    [Fact]
    public async Task PendingFormSurvivesHostRestartAndCompletedResponseRemainsDeduplicated()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Yatra.Restart.Tests", Guid.NewGuid().ToString("N"));
        var conversationId = YatraUlid.NewUlid();
        string requestId;
        await using (var first = CreateFactory(folder))
        {
            using var client = first.CreateClient();
            await using var stream = await SseConversation.OpenAsync(client, conversationId);
            await PostUserTextAsync(client, conversationId, "show contact form");
            requestId = (await stream.ReadNextMessageAsync(YatraMessageTypes.FormRequest)).MessageId;
        }
        string responseId;
        await using (var second = CreateFactory(folder))
        {
            using var client = second.CreateClient();
            await using var stream = await SseConversation.OpenAsync(client, conversationId);
            responseId = await PostContactResponseAsync(client, conversationId, requestId);
            var confirmation = await stream.ReadNextMessageAsync(YatraMessageTypes.AgentText);
            Assert.Equal(responseId, confirmation.InReplyTo);
        }
        await using (var third = CreateFactory(folder))
        {
            using var client = third.CreateClient();
            await using var stream = await SseConversation.OpenAsync(client, conversationId);
            _ = await stream.ReadNextMessageAsync(YatraMessageTypes.AgentText);
            await PostContactResponseAsync(client, conversationId, requestId);
            var error = await stream.ReadNextMessageAsync(YatraMessageTypes.Error);
            Assert.Equal("duplicate_response", error.Payload.GetProperty("code").GetString());
        }
    }

    private static WebApplicationFactory<AppHost::Program> CreateFactory(string? folder = null) =>
        new WebApplicationFactory<AppHost::Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                    Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<Yatra.Orchestration.Llm.ILlmClient, Yatra.Orchestration.Llm.FakeLlmClient>(services));
                builder.UseSetting("Yatra:SseHeartbeatSeconds", "1");
                builder.UseSetting("Yatra:StorageFolder", folder ?? Path.Combine(Path.GetTempPath(), "Yatra.AppHost.Tests", Guid.NewGuid().ToString("N")));
            });

    private static async Task<string> PostUserTextAsync(
        HttpClient client,
        string conversationId,
        string text)
    {
        var messageId = YatraUlid.NewUlid();
        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{conversationId}/messages",
            new
            {
                messageId,
                type = YatraMessageTypes.UserText,
                payload = new { text }
            },
            YatraJson.SerializerOptions);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return messageId;
    }

    private static async Task<string> PostContactResponseAsync(
        HttpClient client,
        string conversationId,
        string inReplyTo)
    {
        return await PostFormResponseAsync(
            client,
            conversationId,
            inReplyTo,
            "contact",
            new
            {
                name = "Siva",
                email = "siva@example.com",
                subject = "Hello",
                message = "Need help"
            });
    }

    private static async Task<string> PostAccessResponseAsync(
        HttpClient client,
        string conversationId,
        string inReplyTo)
    {
        return await PostFormResponseAsync(
            client,
            conversationId,
            inReplyTo,
            "access-request",
            new
            {
                application = "finance",
                role = "viewer",
                justification = "Need read access for monthly review.",
                accessEndDate = "2026-12-31"
            });
    }

    private static async Task<string> PostFormResponseAsync(
        HttpClient client,
        string conversationId,
        string inReplyTo,
        string formId,
        object values)
    {
        var messageId = YatraUlid.NewUlid();
        var response = await client.PostAsJsonAsync(
            $"/api/conversations/{conversationId}/messages",
            new
            {
                messageId,
                type = YatraMessageTypes.FormResponse,
                inReplyTo,
                payload = new
                {
                    formId,
                    formVersion = "1.0",
                    action = FormResponseAction.Submit,
                    values
                }
            },
            YatraJson.SerializerOptions);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return messageId;
    }

    private sealed class SseConversation : IAsyncDisposable
    {
        private readonly HttpResponseMessage _response;
        private readonly StreamReader _reader;

        private SseConversation(HttpResponseMessage response, Stream stream)
        {
            _response = response;
            _reader = new StreamReader(stream);
        }

        public static async Task<SseConversation> OpenAsync(HttpClient client, string conversationId)
        {
            var response = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{conversationId}/events"),
                HttpCompletionOption.ResponseHeadersRead);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

            var stream = await response.Content.ReadAsStreamAsync();
            return new SseConversation(response, stream);
        }

        public async Task<YatraMessage> ReadNextMessageAsync(string expectedType)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
            while (DateTimeOffset.UtcNow < deadline)
            {
                var message = await ReadNextMessageAsync(deadline);
                if (message.Type == expectedType)
                {
                    return message;
                }
            }

            throw new TimeoutException($"No SSE message of type '{expectedType}' was received.");
        }

        public async ValueTask DisposeAsync()
        {
            _reader.Dispose();
            _response.Dispose();
            await Task.CompletedTask;
        }

        private async Task<YatraMessage> ReadNextMessageAsync(DateTimeOffset deadline)
        {
            string? data = null;

            while (DateTimeOffset.UtcNow < deadline)
            {
                var line = await _reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
                if (line is null)
                {
                    continue;
                }

                if (line.Length == 0)
                {
                    if (data is null)
                    {
                        continue;
                    }

                    return JsonSerializer.Deserialize<YatraMessage>(
                        data,
                        YatraJson.SerializerOptions)
                        ?? throw new InvalidOperationException("SSE data could not be deserialized.");
                }

                if (line.StartsWith(':'))
                {
                    continue;
                }

                if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    data = line["data: ".Length..];
                }
            }

            throw new TimeoutException("No SSE data event was received.");
        }
    }
}
