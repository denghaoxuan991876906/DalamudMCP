using System.Runtime.Versioning;
using Dalamud.Game.Text;
using DalamudMCP.Framework;
using DalamudMCP.Plugin.Readers;
using DalamudMCP.Plugin.Services;
using DalamudMCP.Protocol;
using MemoryPack;

namespace DalamudMCP.Plugin.Operations;

[Operation(
    "chat.read",
    Description = "Reads recent chat/combat/system log entries.",
    Summary = "Gets recent chat log entries.")]
[ResultFormatter(typeof(ChatLogReadOperation.TextFormatter))]
[CliCommand("chat", "read")]
[McpTool("get_chat_log")]
public sealed partial class ChatLogReadOperation
    : IOperation<ChatLogReadOperation.Request, ChatLogSnapshot>, IPluginReaderStatus
{
    private readonly Func<Request, CancellationToken, ValueTask<ChatLogSnapshot>> executor;
    private readonly Func<bool>? isReadyProvider;
    private readonly Func<string>? detailProvider;
    private readonly string unavailableDetail;

    [SupportedOSPlatform("windows")]
    public ChatLogReadOperation(ChatLogBufferService logBuffer)
    {
        ArgumentNullException.ThrowIfNull(logBuffer);

        executor = CreateExecutor(logBuffer);
        isReadyProvider = () => true; // Buffer exists = ready
        detailProvider = () => "ready";
        unavailableDetail = "ready";
    }

    internal ChatLogReadOperation(
        Func<Request, CancellationToken, ValueTask<ChatLogSnapshot>> executor,
        bool isReady = true,
        string detail = "ready")
    {
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        isReadyProvider = () => isReady;
        detailProvider = () => string.IsNullOrWhiteSpace(detail) ? "ready" : detail;
        unavailableDetail = string.IsNullOrWhiteSpace(detail) ? "ready" : detail;
    }

    public string ReaderKey => "chat.read";

    public bool IsReady => isReadyProvider?.Invoke() ?? false;

    public string Detail => detailProvider?.Invoke() ?? unavailableDetail;

    public ValueTask<ChatLogSnapshot> ExecuteAsync(Request request, OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return executor(request, context.CancellationToken);
    }

    [MemoryPackable]
    [ProtocolOperation("chat.read")]
    [LegacyBridgeRequest("ReadChatLog")]
    public sealed partial class Request
    {
        [Option("channels", Description = "Chat channels to filter by (e.g., Say,Party,System). Empty = all.", Required = false)]
        public string[]? Channels { get; init; }

        [Option("since", Description = "Only return entries after this UTC timestamp (ISO 8601).", Required = false)]
        public DateTimeOffset? Since { get; init; }

        [Option("max-count", Description = "Maximum number of entries to return.", Required = false)]
        public int? MaxCount { get; init; }
    }

    public sealed class TextFormatter : IResultFormatter<ChatLogSnapshot>
    {
        public string? FormatText(ChatLogSnapshot result, OperationContext context)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(context);

            return result.SummaryText;
        }
    }

    [SupportedOSPlatform("windows")]
    private static Func<Request, CancellationToken, ValueTask<ChatLogSnapshot>> CreateExecutor(
        ChatLogBufferService logBuffer)
    {
        return (request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            XivChatType[]? channelFilter = null;
            if (request.Channels is { Length: > 0 })
            {
                var types = new List<XivChatType>(request.Channels.Length);
                foreach (string channelName in request.Channels)
                {
                    if (Enum.TryParse<XivChatType>(channelName, ignoreCase: true, out var parsed))
                        types.Add(parsed);
                }
                channelFilter = types.Count > 0 ? types.ToArray() : null;
            }

            DateTimeOffset? since = request.Since;
            int maxCount = request.MaxCount is > 0 ? Math.Min(request.MaxCount.Value, 500) : 100;

            IReadOnlyList<ChatLogEntry> entries = logBuffer.GetRecent(channelFilter, since, maxCount);

            return ValueTask.FromResult(new ChatLogSnapshot(
                DateTimeOffset.UtcNow,
                [.. entries],
                entries.Count,
                $"{entries.Count} log entries returned."));
        };
    }
}

[MemoryPackable]
public sealed partial record ChatLogSnapshot(
    DateTimeOffset CapturedAt,
    ChatLogEntry[] Entries,
    int TotalFilteredCount,
    string SummaryText);
