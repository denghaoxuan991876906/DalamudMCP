using Dalamud.Plugin.Services;
using DalamudMCP.Framework;
using NSubstitute;

namespace DalamudMCP.Plugin.Operations.Tests;

public sealed class SlashCommandOperationTests
{
    private static IFramework CreateFramework(bool isInFrameworkThread = false)
    {
        var framework = Substitute.For<IFramework>();
        framework.IsInFrameworkUpdateThread.Returns(isInFrameworkThread);
        framework.RunOnFrameworkThread(Arg.Any<Action>())
            .Returns(callInfo =>
            {
                ((Action)callInfo[0])();
                return Task.CompletedTask;
            });
        return framework;
    }

    private static ICommandManager CreateCommandManager()
    {
        return Substitute.For<ICommandManager>();
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsCommandSent_WhenCommandStartsWithSlash()
    {
        var framework = CreateFramework();
        var commandManager = CreateCommandManager();
        var operation = new SlashCommandOperation(framework, commandManager);
        var request = new SlashCommandOperation.Request { Command = "/echo hello" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        Assert.Equal("command_sent", result.Status);
        Assert.Contains("命令已发送", result.SummaryText);
        commandManager.Received(1).ProcessCommand("/echo hello");
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsValidationFailed_WhenCommandNotStartingWithSlash()
    {
        var framework = CreateFramework();
        var commandManager = CreateCommandManager();
        var operation = new SlashCommandOperation(framework, commandManager);
        var request = new SlashCommandOperation.Request { Command = "hello" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.False(result.Success);
        Assert.Equal("validation_failed", result.Status);
        commandManager.DidNotReceive().ProcessCommand(Arg.Any<string>());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsValidationFailed_WhenCommandEmpty()
    {
        var framework = CreateFramework();
        var commandManager = CreateCommandManager();
        var operation = new SlashCommandOperation(framework, commandManager);
        var request = new SlashCommandOperation.Request { Command = "" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.False(result.Success);
        Assert.Equal("validation_failed", result.Status);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsValidationFailed_WhenCommandExceedsMaxLength()
    {
        var framework = CreateFramework();
        var commandManager = CreateCommandManager();
        var operation = new SlashCommandOperation(framework, commandManager);
        var longCommand = "/" + new string('x', 256); // 257 chars total
        var request = new SlashCommandOperation.Request { Command = longCommand };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.False(result.Success);
        Assert.Equal("validation_failed", result.Status);
        commandManager.DidNotReceive().ProcessCommand(Arg.Any<string>());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsCommandSent_WhenCommandAtMaxLength()
    {
        var framework = CreateFramework();
        var commandManager = CreateCommandManager();
        var operation = new SlashCommandOperation(framework, commandManager);
        var maxCommand = "/" + new string('x', 255); // exactly 256 chars
        var request = new SlashCommandOperation.Request { Command = maxCommand };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        Assert.Equal("command_sent", result.Status);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsCommandSent_WhenCommandIsOnlySlash()
    {
        var framework = CreateFramework();
        var commandManager = CreateCommandManager();
        var operation = new SlashCommandOperation(framework, commandManager);
        var request = new SlashCommandOperation.Request { Command = "/" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        Assert.Equal("command_sent", result.Status);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsCommandSent_WithSpecialCharacters()
    {
        var framework = CreateFramework();
        var commandManager = CreateCommandManager();
        var operation = new SlashCommandOperation(framework, commandManager);
        var request = new SlashCommandOperation.Request { Command = "/echo\r\nhello" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        Assert.Equal("command_sent", result.Status);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsCommandSent_OnFrameworkThread()
    {
        var framework = CreateFramework(isInFrameworkThread: true);
        var commandManager = CreateCommandManager();
        var operation = new SlashCommandOperation(framework, commandManager);
        var request = new SlashCommandOperation.Request { Command = "/echo hello" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        Assert.Equal("command_sent", result.Status);
        commandManager.Received(1).ProcessCommand("/echo hello");
        await framework.DidNotReceive().RunOnFrameworkThread(Arg.Any<Action>());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsCommandSent_RunOnFrameworkThread()
    {
        var framework = CreateFramework(isInFrameworkThread: false);
        var commandManager = CreateCommandManager();
        var operation = new SlashCommandOperation(framework, commandManager);
        var request = new SlashCommandOperation.Request { Command = "/echo hello" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        Assert.Equal("command_sent", result.Status);
        await framework.Received(1).RunOnFrameworkThread(Arg.Any<Action>());
    }

    [Fact]
    public void Constructor_RejectsNullFramework()
    {
        var commandManager = CreateCommandManager();
        var ex = Assert.Throws<ArgumentNullException>(
            () => new SlashCommandOperation(null!, commandManager));
        Assert.Equal("framework", ex.ParamName);
    }

    [Fact]
    public void Constructor_RejectsNullCommandManager()
    {
        var framework = CreateFramework();
        var ex = Assert.Throws<ArgumentNullException>(
            () => new SlashCommandOperation(framework, null!));
        Assert.Equal("commandManager", ex.ParamName);
    }
}
