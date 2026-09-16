using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yatra.Contracts.Messages;

namespace Yatra.Engine.Persistence;

public sealed class FileConversationMessageStore : IConversationMessageStore
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new(StringComparer.Ordinal);
    private readonly string _conversationFolder;
    private readonly ILogger<FileConversationMessageStore> _logger;

    public FileConversationMessageStore(
        IOptions<YatraEngineOptions> options,
        ILogger<FileConversationMessageStore> logger)
    {
        var storageFolder = string.IsNullOrWhiteSpace(options.Value.StorageFolder)
            ? Path.Combine(AppContext.BaseDirectory, "library")
            : options.Value.StorageFolder;

        _conversationFolder = Path.Combine(storageFolder, "conversations");
        _logger = logger;
    }

    public async ValueTask AppendAsync(YatraMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        var path = GetConversationPath(message.ConversationId);
        var fileLock = _fileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));

        await fileLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_conversationFolder);

            var messages = await ReadConversationFileAsync(path, cancellationToken);
            if (messages.Any(candidate => candidate.MessageId == message.MessageId))
            {
                return;
            }

            messages.Add(message);
            await WriteConversationFileAsync(path, messages, cancellationToken);
        }
        finally
        {
            fileLock.Release();
        }
    }

    public async ValueTask<IReadOnlyList<YatraMessage>> ReadConversationAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        cancellationToken.ThrowIfCancellationRequested();

        var path = GetConversationPath(conversationId);
        var fileLock = _fileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));

        await fileLock.WaitAsync(cancellationToken);
        try
        {
            return await ReadConversationFileAsync(path, cancellationToken);
        }
        finally
        {
            fileLock.Release();
        }
    }

    private string GetConversationPath(string conversationId)
    {
        if (!YatraUlid.IsValid(conversationId))
        {
            throw new ArgumentException("Conversation ID must be a valid ULID.", nameof(conversationId));
        }

        return Path.Combine(_conversationFolder, $"{conversationId}.messages.json");
    }

    private async Task<List<YatraMessage>> ReadConversationFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        var messages = await JsonSerializer.DeserializeAsync<List<YatraMessage>>(
            stream,
            YatraJson.SerializerOptions,
            cancellationToken);

        return messages ?? [];
    }

    private async Task WriteConversationFileAsync(
        string path,
        IReadOnlyList<YatraMessage> messages,
        CancellationToken cancellationToken)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    messages,
                    YatraJson.SerializerOptions,
                    cancellationToken);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    private void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to remove temporary conversation store file {TempPath}.", tempPath);
        }
    }
}
