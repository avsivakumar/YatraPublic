using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Yatra.AgentIntegration;
using Yatra.Contracts.Forms;
using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;
using Yatra.Engine.Routing;

namespace Yatra.Engine.Persistence;

// One atomic snapshot owns queue acceptance, routing metadata, and completion checkpoints.
public sealed class FileEngineStateStore : IPendingReplyRegistry, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly FileStream _ownership;
    private readonly string _path;
    private readonly YatraEngineOptions _options;
    private EngineState _state;
    private readonly HashSet<string> _inboundLeases = [];
    private readonly HashSet<string> _outboundLeases = [];

    public FileEngineStateStore(IOptions<YatraEngineOptions> options)
    {
        _options = options.Value;
        var folder = string.IsNullOrWhiteSpace(_options.StorageFolder)
            ? Path.Combine(AppContext.BaseDirectory, "library") : _options.StorageFolder;
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "engine-state.json");
        _ownership = new FileStream(Path.Combine(folder, "engine-state.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            _state = File.Exists(_path)
                ? JsonSerializer.Deserialize<EngineState>(File.ReadAllText(_path), YatraJson.SerializerOptions)
                    ?? throw new InvalidDataException("Engine state cannot be null.")
                : new EngineState();
            if (_state.Version != 1 || _state.Inbound is null || _state.Outbound is null || _state.Replies is null)
                throw new InvalidDataException("Unsupported or invalid engine state.");
            if (_state.Inbound.Any(pair => pair.Value?.Message is null || pair.Key != pair.Value.Message.MessageId)
                || _state.Outbound.Any(pair => pair.Value?.Message?.Message is null || pair.Key != pair.Value.Message.Message.MessageId)
                || _state.Replies.Any(pair => pair.Value?.ReturnPath is null || pair.Key != pair.Value.RequestMessageId))
                throw new InvalidDataException("Engine state contains invalid records.");
            foreach (var (id, reply) in _state.Replies)
                if (reply.Status == PendingReplyStatus.Processing)
                    _state.Replies[id] = reply with { Status = PendingReplyStatus.Pending };
            Save(_state);
        }
        catch { _ownership.Dispose(); throw; }
    }

    public async Task<bool> AcceptAsync(YatraMessage message, CancellationToken token) =>
        await ChangeAsync(state =>
        {
            if (state.Inbound.TryGetValue(message.MessageId, out var previous))
            {
                if (!SameMessage(previous.Message, message)) throw new SubmissionConflictException();
                return true;
            }
            state.Inbound.Add(message.MessageId, new InboxEntry(message));
            return false;
        }, token);

    public Task PublishAsync(AgentOutput output, CancellationToken token) => ChangeAsync(state =>
    {
        AddOutput(state, output);
        return true;
    }, token);

    private void AddOutput(EngineState state, AgentOutput output)
    {
        if (state.Outbound.TryGetValue(output.Message.MessageId, out var previous))
        {
            if (!SameMessage(previous.Message.Message, output.Message) || previous.Message.ReturnPath != output.ReturnPath)
                throw new SubmissionConflictException();
            return;
        }
        if (output.Message.ExpectsReply)
        {
            if (output.ReturnPath is null) throw new InvalidOperationException("A reply-expected message requires a return path.");
            var form = output.Message.Type == YatraMessageTypes.FormRequest
                ? output.Message.Payload.Deserialize<FormRequestPayload>(YatraJson.SerializerOptions) : null;
            var now = DateTimeOffset.UtcNow;
            state.Replies.Add(output.Message.MessageId, new PendingReply(output.Message.MessageId,
                output.Message.ConversationId, output.ReturnPath, now,
                now.AddMinutes(_options.PendingReplyLifetimeMinutes), PendingReplyStatus.Pending,
                form?.FormId, form?.FormVersion, form?.FormId));
        }
        state.Outbound.Add(output.Message.MessageId, new OutboxEntry(new RoutableMessage(output.Message, output.ReturnPath)));
    }

    public Task CommitInboundAsync(YatraMessage input, IReadOnlyList<AgentOutput> outputs,
        FormResponseAction? action, CancellationToken token) => ChangeAsync(state =>
    {
        foreach (var output in outputs) AddOutput(state, output);
        if (action is not null && input.InReplyTo is not null)
        {
            var reply = state.Replies[input.InReplyTo];
            if (reply.Status != PendingReplyStatus.Processing) throw new InvalidOperationException("The pending reply claim was lost.");
            state.Replies[input.InReplyTo] = reply with
            {
                Status = action == FormResponseAction.Cancel ? PendingReplyStatus.Cancelled : PendingReplyStatus.Completed
            };
        }
        state.Inbound[input.MessageId] = state.Inbound[input.MessageId] with { Completed = true };
        return true;
    }, token);

    public async IAsyncEnumerable<YatraMessage> ReadInboundAsync([EnumeratorCancellation] CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            YatraMessage? next;
            await _gate.WaitAsync(token);
            try
            {
                next = _state.Inbound.Values.FirstOrDefault(entry => !entry.Completed && !_inboundLeases.Contains(entry.Message.MessageId))?.Message;
                if (next is not null) _inboundLeases.Add(next.MessageId);
            }
            finally { _gate.Release(); }
            if (next is null) await Task.Delay(100, token);
            else yield return next;
        }
    }

    public async IAsyncEnumerable<RoutableMessage> ReadOutboundAsync([EnumeratorCancellation] CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            RoutableMessage? next;
            await _gate.WaitAsync(token);
            try
            {
                next = _state.Outbound.Values.FirstOrDefault(entry => !entry.Delivered && !_outboundLeases.Contains(entry.Message.Message.MessageId))?.Message;
                if (next is not null) _outboundLeases.Add(next.Message.MessageId);
            }
            finally { _gate.Release(); }
            if (next is null) await Task.Delay(100, token);
            else yield return next;
        }
    }

    public Task CompleteInboundAsync(string id, CancellationToken token) => ChangeAsync(state =>
    {
        state.Inbound[id] = state.Inbound[id] with { Completed = true };
        return true;
    }, token);

    public Task CompleteOutboundAsync(string id, CancellationToken token) => ChangeAsync(state =>
    {
        state.Outbound[id] = state.Outbound[id] with { Delivered = true };
        return true;
    }, token);

    public async Task RetryAsync(string id, bool inbound, CancellationToken token)
    {
        await Task.Delay(1000, token);
        await _gate.WaitAsync(token);
        try { (inbound ? _inboundLeases : _outboundLeases).Remove(id); }
        finally { _gate.Release(); }
    }

    public Task RegisterAsync(PendingReply reply, CancellationToken cancellationToken = default) => ChangeAsync(state =>
    {
        if (!YatraUlid.IsValid(reply.RequestMessageId) || !YatraUlid.IsValid(reply.ConversationId) || reply.ExpiresAtUtc <= reply.CreatedAtUtc)
            throw new ArgumentException("Invalid pending reply.", nameof(reply));
        state.Replies.Add(reply.RequestMessageId, reply);
        return true;
    }, cancellationToken);

    public Task<PendingReply?> ResolveAsync(string id, string conversationId, CancellationToken cancellationToken = default) =>
        ChangeAsync(state =>
        {
            if (!state.Replies.TryGetValue(id, out var reply) || reply.ConversationId != conversationId || reply.Status != PendingReplyStatus.Pending) return null;
            if (reply.ExpiresAtUtc > DateTimeOffset.UtcNow) return reply;
            state.Replies[id] = reply with { Status = PendingReplyStatus.Expired };
            return null;
        }, cancellationToken);

    public Task<PendingReplyResolution> ClaimForResponseAsync(string id, string conversationId, CancellationToken cancellationToken = default) =>
        ChangeAsync(state =>
        {
            if (!state.Replies.TryGetValue(id, out var reply)) return PendingReplyResolution.NotFound;
            var status = reply.ConversationId != conversationId ? PendingReplyResolutionStatus.ConversationMismatch : reply.Status switch
            {
                PendingReplyStatus.Processing => PendingReplyResolutionStatus.AlreadyProcessing,
                PendingReplyStatus.Completed => PendingReplyResolutionStatus.AlreadyCompleted,
                PendingReplyStatus.Cancelled => PendingReplyResolutionStatus.Cancelled,
                PendingReplyStatus.Expired => PendingReplyResolutionStatus.Expired,
                _ => PendingReplyResolutionStatus.Resolved
            };
            if (status == PendingReplyResolutionStatus.Resolved)
            {
                status = reply.ExpiresAtUtc <= DateTimeOffset.UtcNow ? PendingReplyResolutionStatus.Expired : status;
                reply = reply with { Status = status == PendingReplyResolutionStatus.Expired ? PendingReplyStatus.Expired : PendingReplyStatus.Processing };
                state.Replies[id] = reply;
            }
            return new PendingReplyResolution(status, reply);
        }, cancellationToken);

    public Task<bool> CompleteAsync(string id, CancellationToken cancellationToken = default) => SetStatusAsync(id, PendingReplyStatus.Completed, cancellationToken);
    public Task<bool> CancelAsync(string id, CancellationToken cancellationToken = default) => SetStatusAsync(id, PendingReplyStatus.Cancelled, cancellationToken);
    public Task<bool> ReleaseClaimAsync(string id, CancellationToken cancellationToken = default) => SetStatusAsync(id, PendingReplyStatus.Pending, cancellationToken);
    private Task<bool> SetStatusAsync(string id, PendingReplyStatus status, CancellationToken token) => ChangeAsync(state =>
    {
        if (!state.Replies.TryGetValue(id, out var reply) || reply.Status != PendingReplyStatus.Processing) return false;
        state.Replies[id] = reply with { Status = status };
        return true;
    }, token);

    public Task<IReadOnlyList<PendingReply>> ExpireAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
        ChangeAsync<IReadOnlyList<PendingReply>>(state =>
        {
            var expired = state.Replies.Values.Where(reply => reply.Status == PendingReplyStatus.Pending && reply.ExpiresAtUtc <= now)
                .Select(reply => reply with { Status = PendingReplyStatus.Expired }).ToArray();
            foreach (var reply in expired) state.Replies[reply.RequestMessageId] = reply;
            return expired;
        }, cancellationToken);

    // Terminal records remain durable so a historical form cannot become actionable again.
    public Task<int> RemoveTerminalAsync(DateTimeOffset terminalBeforeUtc, CancellationToken cancellationToken = default) => Task.FromResult(0);

    private async Task<T> ChangeAsync<T>(Func<EngineState, T> change, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var next = JsonSerializer.Deserialize<EngineState>(JsonSerializer.Serialize(_state, YatraJson.SerializerOptions), YatraJson.SerializerOptions)!;
            var result = change(next);
            Save(next);
            _state = next;
            return result;
        }
        finally { _gate.Release(); }
    }

    private void Save(EngineState state)
    {
        var temporary = _path + ".tmp";
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, state, YatraJson.SerializerOptions);
            file.Flush(flushToDisk: true);
        }
        File.Move(temporary, _path, overwrite: true);
    }

    private static bool SameMessage(YatraMessage left, YatraMessage right) =>
        left.ConversationId == right.ConversationId && left.Type == right.Type && left.InReplyTo == right.InReplyTo
        && JsonNode.DeepEquals(JsonNode.Parse(left.Payload.GetRawText()), JsonNode.Parse(right.Payload.GetRawText()));

    public void Dispose() { _ownership.Dispose(); _gate.Dispose(); }

    public sealed class SubmissionConflictException : Exception;
    public sealed record InboxEntry(YatraMessage Message, bool Completed = false);
    public sealed record OutboxEntry(RoutableMessage Message, bool Delivered = false);
    public sealed class EngineState
    {
        [JsonRequired]
        public int Version { get; set; } = 1;
        [JsonRequired]
        public Dictionary<string, InboxEntry> Inbound { get; set; } = [];
        [JsonRequired]
        public Dictionary<string, OutboxEntry> Outbound { get; set; } = [];
        [JsonRequired]
        public Dictionary<string, PendingReply> Replies { get; set; } = [];
    }
}
