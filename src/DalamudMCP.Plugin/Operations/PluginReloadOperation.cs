using System.Runtime.Versioning;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DalamudMCP.Framework;
using DalamudMCP.Protocol;
using MemoryPack;

namespace DalamudMCP.Plugin.Operations;

[Operation(
    "plugin.reload",
    Description = "触发指定 Dalamud 插件的卸载→重载流程。重载在 Framework 线程上执行（不阻塞游戏主线程）。重载后 IPC 通道通常需要 1-3 秒恢复，建议使用 invoke_plugin_ipc 或 unsafe_invoke_plugin_ipc 轮询确认插件 IPC 是否就绪。",
    Summary = "Reloads a specified Dalamud plugin.")]
[ResultFormatter(typeof(PluginReloadOperation.TextFormatter))]
[CliCommand("plugin", "reload")]
[McpTool("reload_plugin")]
public sealed partial class PluginReloadOperation
    : IOperation<PluginReloadOperation.Request, PluginReloadResult>
{
    private readonly Func<Request, CancellationToken, ValueTask<PluginReloadResult>> executor;

    [SupportedOSPlatform("windows")]
    public PluginReloadOperation(
        IDalamudPluginInterface pluginInterface,
        IFramework framework,
        ICommandManager commandManager)
    {
        ArgumentNullException.ThrowIfNull(pluginInterface);
        ArgumentNullException.ThrowIfNull(framework);
        ArgumentNullException.ThrowIfNull(commandManager);

        executor = CreateDalamudExecutor(pluginInterface, framework, commandManager);
    }

    internal PluginReloadOperation(Func<Request, CancellationToken, ValueTask<PluginReloadResult>> executor)
    {
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public ValueTask<PluginReloadResult> ExecuteAsync(Request request, OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return executor(request, context.CancellationToken);
    }

    [MemoryPackable]
    [ProtocolOperation("plugin.reload")]
    public sealed partial class Request
    {
        [Option("plugin-name", Description = "目标插件的内部名称（InternalName），大小写不敏感。")]
        public string PluginName { get; init; } = string.Empty;
    }

    public sealed class TextFormatter : IResultFormatter<PluginReloadResult>
    {
        public string? FormatText(PluginReloadResult result, OperationContext context)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(context);

            return result.SummaryText;
        }
    }

    [SupportedOSPlatform("windows")]
    private static Func<Request, CancellationToken, ValueTask<PluginReloadResult>> CreateDalamudExecutor(
        IDalamudPluginInterface pluginInterface,
        IFramework framework,
        ICommandManager commandManager)
    {
        return async (request, ct) =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.PluginName);

            var pluginName = request.PluginName.Trim();

            // Self-reload guard
            if (string.Equals(pluginName, pluginInterface.InternalName, StringComparison.OrdinalIgnoreCase))
            {
                return new PluginReloadResult(
                    PluginName: pluginName,
                    Success: false,
                    Status: "self_reload_blocked",
                    ErrorMessage: "Cannot reload DalamudMCP itself. Use /xlreload instead.",
                    SummaryText: $"Self-reload blocked: {pluginName} is the current plugin.");
            }

            // Verify target plugin exists
            var target = pluginInterface.InstalledPlugins
                .FirstOrDefault(p => string.Equals(p.InternalName, pluginName, StringComparison.OrdinalIgnoreCase));

            if (target is null)
            {
                return new PluginReloadResult(
                    PluginName: pluginName,
                    Success: false,
                    Status: "plugin_not_found",
                    ErrorMessage: $"Plugin '{pluginName}' not found in installed plugins.",
                    SummaryText: $"Plugin not found: {pluginName}.");
            }

            // Execute reload via ICommandManager on Framework thread
            try
            {
                var reloadCommand = $"/xlreload {pluginName}";
                if (framework.IsInFrameworkUpdateThread)
                {
                    commandManager.ProcessCommand(reloadCommand);
                }
                else
                {
                    await framework.RunOnFrameworkThread(() =>
                    {
                        commandManager.ProcessCommand(reloadCommand);
                    }).ConfigureAwait(false);
                }

                return new PluginReloadResult(
                    PluginName: pluginName,
                    Success: true,
                    Status: "reload_initiated",
                    ErrorMessage: null,
                    SummaryText: $"Reload initiated for plugin: {pluginName}.");
            }
            catch (Exception ex)
            {
                return new PluginReloadResult(
                    PluginName: pluginName,
                    Success: false,
                    Status: "reload_failed",
                    ErrorMessage: ex.Message,
                    SummaryText: $"Reload failed for plugin: {pluginName}. Error: {ex.Message}");
            }
        };
    }
}

[MemoryPackable]
public sealed partial record PluginReloadResult(
    string PluginName,
    bool Success,
    string Status,
    string? ErrorMessage,
    string SummaryText);
