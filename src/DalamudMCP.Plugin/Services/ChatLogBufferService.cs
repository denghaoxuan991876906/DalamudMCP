using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Text;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using MemoryPack;

namespace DalamudMCP.Plugin.Services;

[MemoryPackable]
public sealed partial record ChatLogEntry(
    Guid Id,
    DateTimeOffset Timestamp,
    XivChatType Type,
    string ChannelName,
    uint SenderId,
    string? SenderName,
    string Message,
    XivChatRelationKind SourceKind,
    XivChatRelationKind TargetKind);

[SupportedOSPlatform("windows")]
public sealed class ChatLogBufferService : IDisposable
{
    private const int DefaultCapacity = 1000;
    private const int DefaultMaxCount = 100;
    private const int MaxAllowedMaxCount = 500;

    private readonly IChatGui chatGui;
    private readonly ConcurrentQueue<ChatLogEntry> entries = new();
    private readonly int maxCapacity;
    private volatile bool disposed;
    private long totalEnqueued;

    public ChatLogBufferService(IChatGui chatGui, int maxCapacity = DefaultCapacity)
    {
        this.chatGui = chatGui ?? throw new ArgumentNullException(nameof(chatGui));
        this.maxCapacity = maxCapacity > 0 ? maxCapacity : DefaultCapacity;
        chatGui.ChatMessage += OnChatMessage;
    }

    public int Count => entries.Count;

    public IReadOnlyList<ChatLogEntry> GetRecent(
        XivChatType[]? channels = null,
        DateTimeOffset? since = null,
        int maxCount = DefaultMaxCount)
    {
        if (maxCount <= 0) maxCount = DefaultMaxCount;
        maxCount = Math.Min(maxCount, MaxAllowedMaxCount);

        // Snapshot the buffer for thread-safe iteration
        ChatLogEntry[] snapshot = entries.ToArray();

        IEnumerable<ChatLogEntry> query = snapshot.AsEnumerable();

        if (channels is { Length: > 0 })
        {
            HashSet<XivChatType> channelSet = new(channels);
            query = query.Where(e => channelSet.Contains(e.Type));
        }

        if (since.HasValue)
            query = query.Where(e => e.Timestamp >= since.Value);

        return query
            .OrderByDescending(static e => e.Timestamp)
            .Take(maxCount)
            .ToArray();
    }

    private void OnChatMessage(
        XivChatType type,
        uint senderId,
        ref SeString? sender,
        ref SeString? originalSender,
        ref bool isHandled,
        XivChatRelationKind sourceKind,
        XivChatRelationKind targetKind)
    {
        // Extract sender name from SeString if available
        string? senderName = sender?.TextValue;
        string? messageText = originalSender?.TextValue;

        var entry = new ChatLogEntry(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            type,
            type.ToString(),
            senderId,
            senderName,
            messageText ?? string.Empty,
            sourceKind,
            targetKind);

        entries.Enqueue(entry);
        Interlocked.Increment(ref totalEnqueued);

        // Trim oldest entries if over capacity
        while (entries.Count > maxCapacity)
        {
            entries.TryDequeue(out _);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        chatGui.ChatMessage -= OnChatMessage;
    }
}
