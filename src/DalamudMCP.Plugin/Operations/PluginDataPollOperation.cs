using System.Runtime.Versioning;
using Dalamud.Plugin.Services;
using DalamudMCP.Framework;
using DalamudMCP.Protocol;
using DalamudMCP.Plugin.Relay;
using MemoryPack;

namespace DalamudMCP.Plugin.Operations;

[Operation(
    "plugin.data.poll",
    Description = "轮询指定数据回传通道中已缓存的数据。非阻塞读取：返回通道中当前所有可用数据。max-items 参数可限制返回条目数（默认所有可用，上限 10000）。目标插件卸载时，对应通道自动退订，此时 poll 将返回 channel_not_found。",
    Summary = "Polls cached data from a plugin data relay channel.")]
[ResultFormatter(typeof(PluginDataPollOperation.TextFormatter))]
[CliCommand("plugin", "data", "poll")]
[McpTool("plugin_data_poll")]
public sealed partial class PluginDataPollOperation
    : IOperation<PluginDataPollOperation.Request, PluginDataPollResult>
{
    private const int MaxItemsUpperLimit = 10000;

    private readonly Func<Request, CancellationToken, ValueTask<PluginDataPollResult>> executor;

    [SupportedOSPlatform("windows")]
    public PluginDataPollOperation(
        IPluginDataRelayService relay,
        IFramework framework)
    {
        ArgumentNullException.ThrowIfNull(relay);
        ArgumentNullException.ThrowIfNull(framework);

        executor = CreateDalamudExecutor(relay, framework);
    }

    internal PluginDataPollOperation(Func<Request, CancellationToken, ValueTask<PluginDataPollResult>> executor)
    {
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public ValueTask<PluginDataPollResult> ExecuteAsync(Request request, OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return executor(request, context.CancellationToken);
    }

    [MemoryPackable]
    [ProtocolOperation("plugin.data.poll")]
    public sealed partial class Request
    {
        [Option("channel", Description = "完整通道名（{plugin-name}.{channel}，如 'MyPlugin.status'）。")]
        public string Channel { get; init; } = string.Empty;

        [Option("max-items", Description = "最大返回条目数（1-10000）。不指定时返回所有可用数据。", Required = false)]
        public int? MaxItems { get; init; }
    }

    public sealed class TextFormatter : IResultFormatter<PluginDataPollResult>
    {
        public string? FormatText(PluginDataPollResult result, OperationContext context)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(context);

            return result.SummaryText;
        }
    }

    [SupportedOSPlatform("windows")]
    private static Func<Request, CancellationToken, ValueTask<PluginDataPollResult>> CreateDalamudExecutor(
        IPluginDataRelayService relay,
        IFramework framework)
    {
        return async (request, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Channel);

            int? maxItems = request.MaxItems;
            if (maxItems.HasValue && maxItems.Value <= 0)
                throw new ArgumentException("max-items 必须大于 0。", nameof(request));
            if (maxItems.HasValue && maxItems.Value > MaxItemsUpperLimit)
                maxItems = MaxItemsUpperLimit;

            if (framework.IsInFrameworkUpdateThread)
                return ExecutePoll(relay, request, maxItems);

            PluginDataPollResult? result = null;
            await framework.RunOnFrameworkThread(() =>
            {
                result = ExecutePoll(relay, request, maxItems);
            }).ConfigureAwait(false);
            return result!;
        };
    }

    internal static PluginDataPollResult ExecutePoll(
        IPluginDataRelayService relay, Request request, int? maxItems)
    {
        ArgumentNullException.ThrowIfNull(relay);
        ArgumentNullException.ThrowIfNull(request);

        string fullChannelName = request.Channel.Trim();

        try
        {
            if (!relay.TryPoll(fullChannelName, out IReadOnlyList<string> data))
            {
                return new PluginDataPollResult(
                    FullChannelName: fullChannelName,
                    Success: true,
                    Status: "channel_not_found",
                    ItemCount: 0,
                    Items: [],
                    ErrorMessage: null,
                    SummaryText: $"通道 '{fullChannelName}' 不存在。请先使用 plugin_data_subscribe 订阅。");
            }

            if (data.Count == 0)
            {
                return new PluginDataPollResult(
                    FullChannelName: fullChannelName,
                    Success: true,
                    Status: "no_data",
                    ItemCount: 0,
                    Items: [],
                    ErrorMessage: null,
                    SummaryText: $"通道 '{fullChannelName}' 无新数据。");
            }

            string[] finalItems = maxItems.HasValue && maxItems.Value < data.Count
                ? data.Take(maxItems.Value).ToArray()
                : data.ToArray();

            return new PluginDataPollResult(
                FullChannelName: fullChannelName,
                Success: true,
                Status: "data_available",
                ItemCount: finalItems.Length,
                Items: finalItems,
                ErrorMessage: null,
                SummaryText: $"读取 {finalItems.Length} 条数据（共 {data.Count} 条可用）。");
        }
        catch (Exception ex)
        {
            return new PluginDataPollResult(
                FullChannelName: fullChannelName,
                Success: false,
                Status: "poll_failed",
                ItemCount: 0,
                Items: [],
                ErrorMessage: ex.Message,
                SummaryText: $"轮询失败：{ex.Message}");
        }
    }
}

[MemoryPackable]
public sealed partial record PluginDataPollResult(
    string FullChannelName,
    bool Success,
    string Status,
    int ItemCount,
    string[] Items,
    string? ErrorMessage,
    string SummaryText);
