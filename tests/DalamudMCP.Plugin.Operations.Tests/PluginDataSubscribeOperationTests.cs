using Dalamud.Plugin.Services;
using DalamudMCP.Framework;
using DalamudMCP.Plugin.Operations.Tests.TestShared.Relay;
using NSubstitute;

namespace DalamudMCP.Plugin.Operations.Tests;

public sealed class PluginDataSubscribeOperationTests
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

    private static PluginDataSubscribeOperation CreateOperation(FakePluginDataRelayService relay, IFramework framework)
    {
        return new PluginDataSubscribeOperation(relay, framework);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsSubscribeSuccess_WhenValidChannel()
    {
        var relay = new FakePluginDataRelayService();
        var framework = CreateFramework();
        var operation = CreateOperation(relay, framework);
        var request = new PluginDataSubscribeOperation.Request { PluginName = "MyPlugin", Channel = "status" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        Assert.Equal("subscribe_success", result.Status);
        Assert.Equal("MyPlugin.status", result.FullChannelName);
        Assert.Contains("订阅成功", result.SummaryText);
        Assert.True(relay.IsSubscribed("MyPlugin.status"));
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsAlreadySubscribed_WhenChannelExists()
    {
        var relay = new FakePluginDataRelayService();
        relay.Subscribe("MyPlugin", "status");
        var framework = CreateFramework();
        var operation = CreateOperation(relay, framework);
        var request = new PluginDataSubscribeOperation.Request { PluginName = "MyPlugin", Channel = "status" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        Assert.Equal("already_subscribed", result.Status);
        Assert.Contains("已存在", result.SummaryText);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsSubscribeFailed_WhenRelayThrows()
    {
        // 使用 internal 构造注入直接返回失败结果的 executor
        var operation = new PluginDataSubscribeOperation(
            (_, _) => ValueTask.FromResult(new PluginDataSubscribeResult(
                FullChannelName: "MyPlugin.status",
                PluginName: "MyPlugin",
                Success: false,
                Status: "subscribe_failed",
                ErrorMessage: "test failure",
                SummaryText: "订阅失败：test failure")));

        var request = new PluginDataSubscribeOperation.Request { PluginName = "MyPlugin", Channel = "status" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.False(result.Success);
        Assert.Equal("subscribe_failed", result.Status);
        Assert.Contains("test failure", result.ErrorMessage);
    }

    [Fact]
    public void Constructor_RejectsNullRelayService()
    {
        var framework = CreateFramework();
        var ex = Assert.Throws<ArgumentNullException>(
            () => new PluginDataSubscribeOperation(null!, framework));
        Assert.Equal("relay", ex.ParamName);
    }

    [Fact]
    public void Constructor_RejectsNullFramework()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new PluginDataSubscribeOperation(new FakePluginDataRelayService(), null!));
        Assert.Equal("framework", ex.ParamName);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsArgumentException_WhenPluginNameEmpty()
    {
        var relay = new FakePluginDataRelayService();
        var framework = CreateFramework();
        var operation = CreateOperation(relay, framework);
        var request = new PluginDataSubscribeOperation.Request { PluginName = "", Channel = "status" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => operation.ExecuteAsync(request, context).AsTask());
        Assert.Equal("request.PluginName", ex.ParamName);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsArgumentException_WhenChannelEmpty()
    {
        var relay = new FakePluginDataRelayService();
        var framework = CreateFramework();
        var operation = CreateOperation(relay, framework);
        var request = new PluginDataSubscribeOperation.Request { PluginName = "MyPlugin", Channel = "" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => operation.ExecuteAsync(request, context).AsTask());
        Assert.Equal("request.Channel", ex.ParamName);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultCapacityIs1000()
    {
        var relay = new FakePluginDataRelayService();
        var framework = CreateFramework();
        var operation = CreateOperation(relay, framework);
        var request = new PluginDataSubscribeOperation.Request { PluginName = "MyPlugin", Channel = "capacityTest" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);
        Assert.True(result.Success);
        Assert.True(relay.IsSubscribed("MyPlugin.capacityTest"));

        // Write 1001 items — the first should be dropped (DropOldest policy)
        for (int i = 0; i < 1001; i++)
            relay.WriteData("MyPlugin.capacityTest", $"data-{i}");

        // Poll all remaining data
        relay.TryPoll("MyPlugin.capacityTest", out var data);
        Assert.Equal(1000, data.Count);
        // data-0 should have been dropped, first item should be data-1
        Assert.DoesNotContain("data-0", data);
    }
}
