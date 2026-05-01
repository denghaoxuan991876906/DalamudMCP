using System.Runtime.Versioning;
using Dalamud.Plugin.Services;
using DalamudMCP.Framework;
using DalamudMCP.Protocol;
using DalamudMCP.Plugin.Relay;
using MemoryPack;

namespace DalamudMCP.Plugin.Operations;

[Operation(
    "plugin.data.unsubscribe",
    Description = "退订数据回传通道。注销 IPC 端点，关闭有界缓冲区，释放相关资源。退订后目标插件无法再向该通道推送数据，plugin_data_poll 将返回 channel_not_found。",
    Summary = "Unsubscribes from a plugin data relay channel.")]
[ResultFormatter(typeof(PluginDataUnsubscribeOperation.TextFormatter))]
[CliCommand("plugin", "data", "unsubscribe")]
[McpTool("plugin_data_unsubscribe")]
public sealed partial class PluginDataUnsubscribeOperation
    : IOperation<PluginDataUnsubscribeOperation.Request, PluginDataUnsubscribeResult>
{
    private readonly Func<Request, CancellationToken, ValueTask<PluginDataUnsubscribeResult>> executor;

    [SupportedOSPlatform("windows")]
    public PluginDataUnsubscribeOperation(
        IPluginDataRelayService relay,
        IFramework framework)
    {
        ArgumentNullException.ThrowIfNull(relay);
        ArgumentNullException.ThrowIfNull(framework);

        executor = CreateDalamudExecutor(relay, framework);
    }

    internal PluginDataUnsubscribeOperation(Func<Request, CancellationToken, ValueTask<PluginDataUnsubscribeResult>> executor)
    {
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public ValueTask<PluginDataUnsubscribeResult> ExecuteAsync(Request request, OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return executor(request, context.CancellationToken);
    }

    [MemoryPackable]
    [ProtocolOperation("plugin.data.unsubscribe")]
    public sealed partial class Request
    {
        [Option("channel", Description = "完整通道名（{plugin-name}.{channel}，如 'MyPlugin.status'），即订阅时返回的 full_channel_name。")]
        public string Channel { get; init; } = string.Empty;
    }

    public sealed class TextFormatter : IResultFormatter<PluginDataUnsubscribeResult>
    {
        public string? FormatText(PluginDataUnsubscribeResult result, OperationContext context)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(context);

            return result.SummaryText;
        }
    }

    [SupportedOSPlatform("windows")]
    private static Func<Request, CancellationToken, ValueTask<PluginDataUnsubscribeResult>> CreateDalamudExecutor(
        IPluginDataRelayService relay,
        IFramework framework)
    {
        return async (request, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Channel);

            if (framework.IsInFrameworkUpdateThread)
                return ExecuteUnsubscribe(relay, request);

            PluginDataUnsubscribeResult? result = null;
            await framework.RunOnFrameworkThread(() =>
            {
                result = ExecuteUnsubscribe(relay, request);
            }).ConfigureAwait(false);
            return result!;
        };
    }

    internal static PluginDataUnsubscribeResult ExecuteUnsubscribe(IPluginDataRelayService relay, Request request)
    {
        ArgumentNullException.ThrowIfNull(relay);
        ArgumentNullException.ThrowIfNull(request);

        string fullChannelName = request.Channel.Trim();

        try
        {
            if (relay.Unsubscribe(fullChannelName))
            {
                return new PluginDataUnsubscribeResult(
                    FullChannelName: fullChannelName,
                    Success: true,
                    Status: "unsubscribe_success",
                    ErrorMessage: null,
                    SummaryText: $"退订成功：通道 '{fullChannelName}' 已关闭并释放资源。");
            }

            return new PluginDataUnsubscribeResult(
                FullChannelName: fullChannelName,
                Success: true,
                Status: "not_subscribed",
                ErrorMessage: null,
                SummaryText: $"通道 '{fullChannelName}' 不存在或已退订。");
        }
        catch (Exception ex)
        {
            return new PluginDataUnsubscribeResult(
                FullChannelName: fullChannelName,
                Success: false,
                Status: "unsubscribe_failed",
                ErrorMessage: ex.Message,
                SummaryText: $"退订失败：{ex.Message}");
        }
    }
}

[MemoryPackable]
public sealed partial record PluginDataUnsubscribeResult(
    string FullChannelName,
    bool Success,
    string Status,
    string? ErrorMessage,
    string SummaryText);
