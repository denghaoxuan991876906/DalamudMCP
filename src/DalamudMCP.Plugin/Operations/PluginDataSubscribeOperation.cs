using System.Runtime.Versioning;
using Dalamud.Plugin.Services;
using DalamudMCP.Framework;
using DalamudMCP.Protocol;
using DalamudMCP.Plugin.Relay;
using MemoryPack;

namespace DalamudMCP.Plugin.Operations;

[Operation(
    "plugin.data.subscribe",
    Description = "订阅目标插件的数据回传通道。注册 IPC 端点使目标插件可通过 Dalamud IPC 向 DalamudMCP 推送数据。通道命名约定：IPC CallGate = DalamudMCP.Relay.{plugin-name}.{channel}。目标插件使用 GetIpcSubscriber<string,object>(callGate).InvokeAction(jsonData) 推送 JSON 字符串数据。成功订阅后，使用 plugin_data_poll 轮询获取数据。",
    Summary = "Subscribes to a plugin data relay channel.")]
[ResultFormatter(typeof(PluginDataSubscribeOperation.TextFormatter))]
[CliCommand("plugin", "data", "subscribe")]
[McpTool("plugin_data_subscribe")]
public sealed partial class PluginDataSubscribeOperation
    : IOperation<PluginDataSubscribeOperation.Request, PluginDataSubscribeResult>
{
    private readonly Func<Request, CancellationToken, ValueTask<PluginDataSubscribeResult>> executor;

    [SupportedOSPlatform("windows")]
    public PluginDataSubscribeOperation(
        IPluginDataRelayService relay,
        IFramework framework)
    {
        ArgumentNullException.ThrowIfNull(relay);
        ArgumentNullException.ThrowIfNull(framework);

        executor = CreateDalamudExecutor(relay, framework);
    }

    internal PluginDataSubscribeOperation(Func<Request, CancellationToken, ValueTask<PluginDataSubscribeResult>> executor)
    {
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public ValueTask<PluginDataSubscribeResult> ExecuteAsync(Request request, OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return executor(request, context.CancellationToken);
    }

    [MemoryPackable]
    [ProtocolOperation("plugin.data.subscribe")]
    public sealed partial class Request
    {
        [Option("plugin-name", Description = "目标插件的内部名称（InternalName），用于构造 IPC CallGate。")]
        public string PluginName { get; init; } = string.Empty;

        [Option("channel", Description = "回传通道名称（不含插件名前缀）。完整通道名将为 {plugin-name}.{channel}。")]
        public string Channel { get; init; } = string.Empty;
    }

    public sealed class TextFormatter : IResultFormatter<PluginDataSubscribeResult>
    {
        public string? FormatText(PluginDataSubscribeResult result, OperationContext context)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(context);

            return result.SummaryText;
        }
    }

    [SupportedOSPlatform("windows")]
    private static Func<Request, CancellationToken, ValueTask<PluginDataSubscribeResult>> CreateDalamudExecutor(
        IPluginDataRelayService relay,
        IFramework framework)
    {
        return async (request, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(request.PluginName);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Channel);

            if (framework.IsInFrameworkUpdateThread)
                return ExecuteSubscribe(relay, request);

            PluginDataSubscribeResult? result = null;
            await framework.RunOnFrameworkThread(() =>
            {
                result = ExecuteSubscribe(relay, request);
            }).ConfigureAwait(false);
            return result!;
        };
    }

    internal static PluginDataSubscribeResult ExecuteSubscribe(IPluginDataRelayService relay, Request request)
    {
        ArgumentNullException.ThrowIfNull(relay);
        ArgumentNullException.ThrowIfNull(request);

        string pluginName = request.PluginName.Trim();
        string channel = request.Channel.Trim();
        string fullChannelName = $"{pluginName}.{channel}";

        try
        {
            if (relay.Subscribe(pluginName, channel, capacity: 1000))
            {
                return new PluginDataSubscribeResult(
                    FullChannelName: fullChannelName,
                    PluginName: pluginName,
                    Success: true,
                    Status: "subscribe_success",
                    ErrorMessage: null,
                    SummaryText: $"订阅成功：通道 '{fullChannelName}' 已创建。目标插件可通过 IPC CallGate 'DalamudMCP.Relay.{fullChannelName}' 推送 JSON 数据。");
            }

            return new PluginDataSubscribeResult(
                FullChannelName: fullChannelName,
                PluginName: pluginName,
                Success: true,
                Status: "already_subscribed",
                ErrorMessage: null,
                SummaryText: $"通道 '{fullChannelName}' 已存在订阅。");
        }
        catch (Exception ex)
        {
            return new PluginDataSubscribeResult(
                FullChannelName: fullChannelName,
                PluginName: pluginName,
                Success: false,
                Status: "subscribe_failed",
                ErrorMessage: ex.Message,
                SummaryText: $"订阅失败：{ex.Message}");
        }
    }
}

[MemoryPackable]
public sealed partial record PluginDataSubscribeResult(
    string FullChannelName,
    string PluginName,
    bool Success,
    string Status,
    string? ErrorMessage,
    string SummaryText);
