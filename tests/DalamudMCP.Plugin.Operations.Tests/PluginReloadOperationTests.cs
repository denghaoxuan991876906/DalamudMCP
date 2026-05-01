using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DalamudMCP.Framework;
using NSubstitute;

namespace DalamudMCP.Plugin.Operations.Tests;

public sealed class PluginReloadOperationTests
{
    private static IDalamudPluginInterface CreatePluginInterface(
        IEnumerable<IExposedPlugin>? installedPlugins = null,
        string internalName = "DalamudMCP")
    {
        var pi = Substitute.For<IDalamudPluginInterface>();
        pi.InternalName.Returns(internalName);
        pi.InstalledPlugins.Returns(installedPlugins ?? []);
        return pi;
    }

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

    private static IExposedPlugin CreateExposedPlugin(string internalName)
    {
        var plugin = Substitute.For<IExposedPlugin>();
        plugin.InternalName.Returns(internalName);
        plugin.Name.Returns(internalName);
        return plugin;
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsReloadInitiated_WhenPluginFound()
    {
        var fakePlugin = CreateExposedPlugin("TargetPlugin");
        var pluginInterface = CreatePluginInterface([fakePlugin]);
        var framework = CreateFramework(isInFrameworkThread: true);
        var commandManager = CreateCommandManager();
        var operation = new PluginReloadOperation(pluginInterface, framework, commandManager);
        var request = new PluginReloadOperation.Request { PluginName = "TargetPlugin" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        Assert.Equal("reload_initiated", result.Status);
        Assert.Equal("TargetPlugin", result.PluginName);
        commandManager.Received(1).ProcessCommand("/xlreload TargetPlugin");
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsReloadInitiated_RunOnFrameworkThread()
    {
        var fakePlugin = CreateExposedPlugin("TargetPlugin");
        var pluginInterface = CreatePluginInterface([fakePlugin]);
        var framework = CreateFramework(isInFrameworkThread: false);
        var commandManager = CreateCommandManager();
        var operation = new PluginReloadOperation(pluginInterface, framework, commandManager);
        var request = new PluginReloadOperation.Request { PluginName = "TargetPlugin" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        Assert.Equal("reload_initiated", result.Status);
        await framework.Received(1).RunOnFrameworkThread(Arg.Any<Action>());
        commandManager.Received(1).ProcessCommand("/xlreload TargetPlugin");
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsPluginNotFound_WhenPluginNotInstalled()
    {
        var pluginInterface = CreatePluginInterface([]);
        var framework = CreateFramework();
        var commandManager = CreateCommandManager();
        var operation = new PluginReloadOperation(pluginInterface, framework, commandManager);
        var request = new PluginReloadOperation.Request { PluginName = "NonExistent" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.False(result.Success);
        Assert.Equal("plugin_not_found", result.Status);
        Assert.Contains("NonExistent", result.ErrorMessage);
        commandManager.DidNotReceive().ProcessCommand(Arg.Any<string>());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsReloadFailed_WhenCommandThrows()
    {
        var fakePlugin = CreateExposedPlugin("TargetPlugin");
        var pluginInterface = CreatePluginInterface([fakePlugin]);
        var framework = CreateFramework(isInFrameworkThread: true);
        var commandManager = CreateCommandManager();
        commandManager.When(c => c.ProcessCommand(Arg.Any<string>()))
            .Do(_ => throw new InvalidOperationException("Simulated command failure"));
        var operation = new PluginReloadOperation(pluginInterface, framework, commandManager);
        var request = new PluginReloadOperation.Request { PluginName = "TargetPlugin" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.False(result.Success);
        Assert.Equal("reload_failed", result.Status);
        Assert.Contains("Simulated command failure", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsSelfReloadBlocked_WhenRequestingOwnPlugin()
    {
        var pluginInterface = CreatePluginInterface([], "DalamudMCP");
        var framework = CreateFramework();
        var commandManager = CreateCommandManager();
        var operation = new PluginReloadOperation(pluginInterface, framework, commandManager);
        var request = new PluginReloadOperation.Request { PluginName = "DalamudMCP" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.False(result.Success);
        Assert.Equal("self_reload_blocked", result.Status);
        commandManager.DidNotReceive().ProcessCommand(Arg.Any<string>());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsSelfReloadBlocked_CaseInsensitive()
    {
        var pluginInterface = CreatePluginInterface([], "DalamudMCP");
        var framework = CreateFramework();
        var commandManager = CreateCommandManager();
        var operation = new PluginReloadOperation(pluginInterface, framework, commandManager);
        var request = new PluginReloadOperation.Request { PluginName = "dalamudmcp" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.False(result.Success);
        Assert.Equal("self_reload_blocked", result.Status);
    }

    [Fact]
    public async Task ExecuteAsync_MatchesPluginIgnoreCase()
    {
        var fakePlugin = CreateExposedPlugin("TargetPlugin");
        var pluginInterface = CreatePluginInterface([fakePlugin]);
        var framework = CreateFramework(isInFrameworkThread: true);
        var commandManager = CreateCommandManager();
        var operation = new PluginReloadOperation(pluginInterface, framework, commandManager);
        var request = new PluginReloadOperation.Request { PluginName = "targetplugin" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        Assert.Equal("reload_initiated", result.Status);
        Assert.Equal("targetplugin", result.PluginName);
    }

    [Fact]
    public void Constructor_RejectsNullPluginInterface()
    {
        var framework = CreateFramework();
        var commandManager = CreateCommandManager();

        var ex = Assert.Throws<ArgumentNullException>(
            () => new PluginReloadOperation(null!, framework, commandManager));
        Assert.Equal("pluginInterface", ex.ParamName);
    }

    [Fact]
    public void Constructor_RejectsNullFramework()
    {
        var pluginInterface = CreatePluginInterface();
        var commandManager = CreateCommandManager();

        var ex = Assert.Throws<ArgumentNullException>(
            () => new PluginReloadOperation(pluginInterface, null!, commandManager));
        Assert.Equal("framework", ex.ParamName);
    }

    [Fact]
    public void Constructor_RejectsNullCommandManager()
    {
        var pluginInterface = CreatePluginInterface();
        var framework = CreateFramework();

        var ex = Assert.Throws<ArgumentNullException>(
            () => new PluginReloadOperation(pluginInterface, framework, null!));
        Assert.Equal("commandManager", ex.ParamName);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsReloadFailed_FrameworkThreadThrows()
    {
        var fakePlugin = CreateExposedPlugin("TargetPlugin");
        var pluginInterface = CreatePluginInterface([fakePlugin]);
        var framework = CreateFramework(isInFrameworkThread: false);
        framework.RunOnFrameworkThread(Arg.Any<Action>())
            .Returns<Task>(_ => throw new InvalidOperationException("Simulated framework failure"));
        var commandManager = CreateCommandManager();
        var operation = new PluginReloadOperation(pluginInterface, framework, commandManager);
        var request = new PluginReloadOperation.Request { PluginName = "TargetPlugin" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.False(result.Success);
        Assert.Equal("reload_failed", result.Status);
        Assert.Contains("Simulated framework failure", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsArgumentException_WhenPluginNameIsEmpty()
    {
        var pluginInterface = CreatePluginInterface();
        var framework = CreateFramework();
        var commandManager = CreateCommandManager();
        var operation = new PluginReloadOperation(pluginInterface, framework, commandManager);
        var request = new PluginReloadOperation.Request { PluginName = "" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(
            () => operation.ExecuteAsync(request, context).AsTask());
    }
}
