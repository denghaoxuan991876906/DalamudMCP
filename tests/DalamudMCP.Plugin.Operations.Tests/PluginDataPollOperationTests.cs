using Dalamud.Plugin.Services;
using DalamudMCP.Framework;
using DalamudMCP.Plugin.Operations.Tests.TestShared.Relay;
using NSubstitute;

namespace DalamudMCP.Plugin.Operations.Tests;

public sealed class PluginDataPollOperationTests
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

    private static PluginDataPollOperation CreateOperation(FakePluginDataRelayService relay, IFramework framework)
    {
        return new PluginDataPollOperation(relay, framework);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsDataAvailable_WhenChannelHasItems()
    {
        var relay = new FakePluginDataRelayService();
        relay.Subscribe("MyPlugin", "status");
        relay.WriteData("MyPlugin.status", "data-1");
        relay.WriteData("MyPlugin.status", "data-2");
        var framework = CreateFramework();
        var operation = CreateOperation(relay, framework);
        var request = new PluginDataPollOperation.Request { Channel = "MyPlugin.status" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        Assert.Equal("data_available", result.Status);
        Assert.Equal(2, result.ItemCount);
        Assert.Equal(["data-1", "data-2"], result.Items);
        Assert.Contains("读取 2 条", result.SummaryText);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNoData_WhenChannelIsEmpty()
    {
        var relay = new FakePluginDataRelayService();
        relay.Subscribe("MyPlugin", "status");
        var framework = CreateFramework();
        var operation = CreateOperation(relay, framework);
        var request = new PluginDataPollOperation.Request { Channel = "MyPlugin.status" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        Assert.Equal("no_data", result.Status);
        Assert.Equal(0, result.ItemCount);
        Assert.Empty(result.Items);
        Assert.Contains("无新数据", result.SummaryText);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsChannelNotFound_WhenNotSubscribed()
    {
        var relay = new FakePluginDataRelayService();
        var framework = CreateFramework();
        var operation = CreateOperation(relay, framework);
        var request = new PluginDataPollOperation.Request { Channel = "NonexistentPlugin.data" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        Assert.Equal("channel_not_found", result.Status);
        Assert.Equal(0, result.ItemCount);
        Assert.Contains("不存在", result.SummaryText);
    }

    [Fact]
    public async Task ExecuteAsync_RespectsMaxItemsParameter()
    {
        var relay = new FakePluginDataRelayService();
        relay.Subscribe("MyPlugin", "status");
        for (int i = 0; i < 5; i++)
            relay.WriteData("MyPlugin.status", $"data-{i}");
        var framework = CreateFramework();
        var operation = CreateOperation(relay, framework);
        var request = new PluginDataPollOperation.Request { Channel = "MyPlugin.status", MaxItems = 3 };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        Assert.Equal("data_available", result.Status);
        Assert.Equal(3, result.ItemCount);
        Assert.Equal(["data-0", "data-1", "data-2"], result.Items);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsAllItems_WhenMaxItemsExceedsAvailable()
    {
        var relay = new FakePluginDataRelayService();
        relay.Subscribe("MyPlugin", "status");
        relay.WriteData("MyPlugin.status", "data-a");
        relay.WriteData("MyPlugin.status", "data-b");
        var framework = CreateFramework();
        var operation = CreateOperation(relay, framework);
        var request = new PluginDataPollOperation.Request { Channel = "MyPlugin.status", MaxItems = 10 };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        Assert.Equal("data_available", result.Status);
        Assert.Equal(2, result.ItemCount);
    }

    [Fact]
    public void Constructor_RejectsNullRelayService()
    {
        var framework = CreateFramework();
        var ex = Assert.Throws<ArgumentNullException>(
            () => new PluginDataPollOperation(null!, framework));
        Assert.Equal("relay", ex.ParamName);
    }

    [Fact]
    public void Constructor_RejectsNullFramework()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new PluginDataPollOperation(new FakePluginDataRelayService(), null!));
        Assert.Equal("framework", ex.ParamName);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsArgumentException_WhenChannelEmpty()
    {
        var relay = new FakePluginDataRelayService();
        var framework = CreateFramework();
        var operation = CreateOperation(relay, framework);
        var request = new PluginDataPollOperation.Request { Channel = "" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => operation.ExecuteAsync(request, context).AsTask());
        Assert.Equal("request.Channel", ex.ParamName);
    }
}
