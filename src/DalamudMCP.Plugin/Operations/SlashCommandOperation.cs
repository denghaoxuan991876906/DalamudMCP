using System.Runtime.Versioning;
using Dalamud.Plugin.Services;
using DalamudMCP.Framework;
using DalamudMCP.Protocol;
using MemoryPack;

namespace DalamudMCP.Plugin.Operations;

[Operation(
    "command.slash",
    Description = "发送 Dalamud 注册的斜杠命令到游戏中。仅支持 Dalamud 注册的命令（如 /xlreload、/ping）。游戏原生命令（/echo、/tell、/say、/shout 等）无法通过此工具执行，ICommandManager 不处理这些命令。对不支持的命令将返回 command_sent 但命令不会在游戏中生效。",
    Summary = "Sends a Dalamud slash command to the game.")]
[ResultFormatter(typeof(SlashCommandOperation.TextFormatter))]
[CliCommand("command", "slash")]
[McpTool("slash_command")]
public sealed partial class SlashCommandOperation
    : IOperation<SlashCommandOperation.Request, SlashCommandResult>
{
    private readonly Func<Request, CancellationToken, ValueTask<SlashCommandResult>> executor;

    [SupportedOSPlatform("windows")]
    public SlashCommandOperation(IFramework framework, ICommandManager commandManager)
    {
        ArgumentNullException.ThrowIfNull(framework);
        ArgumentNullException.ThrowIfNull(commandManager);
        executor = CreateDalamudExecutor(framework, commandManager);
    }

    internal SlashCommandOperation(Func<Request, CancellationToken, ValueTask<SlashCommandResult>> executor)
    {
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public ValueTask<SlashCommandResult> ExecuteAsync(Request request, OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        return executor(request, context.CancellationToken);
    }

    [MemoryPackable]
    [ProtocolOperation("command.slash")]
    public sealed partial class Request
    {
        [Option("command", Description = "要发送的斜杠命令（必须以 '/' 开头，最大 256 字符），示例：/xlreload TargetPlugin、/ping")]
        public string Command { get; init; } = string.Empty;
    }

    public sealed class TextFormatter : IResultFormatter<SlashCommandResult>
    {
        public string? FormatText(SlashCommandResult result, OperationContext context)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(context);
            return result.SummaryText;
        }
    }

    [SupportedOSPlatform("windows")]
    private static Func<Request, CancellationToken, ValueTask<SlashCommandResult>> CreateDalamudExecutor(
        IFramework framework,
        ICommandManager commandManager)
    {
        return async (request, ct) =>
        {
            if (!request.Command.StartsWith('/'))
            {
                return new SlashCommandResult(
                    Command: request.Command,
                    Success: false,
                    Status: "validation_failed",
                    SummaryText: "命令必须以 '/' 开头。");
            }

            if (request.Command.Length > 256)
            {
                return new SlashCommandResult(
                    Command: request.Command,
                    Success: false,
                    Status: "validation_failed",
                    SummaryText: "命令长度超过 256 字符上限。");
            }

            try
            {
                if (framework.IsInFrameworkUpdateThread)
                {
                    commandManager.ProcessCommand(request.Command);
                }
                else
                {
                    await framework.RunOnFrameworkThread(() =>
                    {
                        commandManager.ProcessCommand(request.Command);
                    }).ConfigureAwait(false);
                }

                return new SlashCommandResult(
                    Command: request.Command,
                    Success: true,
                    Status: "command_sent",
                    SummaryText: $"命令已发送: {request.Command}");
            }
            catch (Exception ex)
            {
                return new SlashCommandResult(
                    Command: request.Command,
                    Success: false,
                    Status: "command_sent",
                    SummaryText: $"命令已发送但执行异常: {ex.Message}");
            }
        };
    }
}

[MemoryPackable]
public sealed partial record SlashCommandResult(
    string Command,
    bool Success,
    string Status,
    string SummaryText);
