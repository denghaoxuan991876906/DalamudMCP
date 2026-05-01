using Dalamud.Plugin.Services;
using DalamudMCP.Framework;
using DalamudMCP.Plugin.Operations.Tests.TestShared.Relay;
using NSubstitute;

namespace DalamudMCP.Plugin.Operations.Tests;

public sealed class PluginDataUnsubscribeOperationTests
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

    private static PluginDataUnsubscribeOperation CreateOperation(FakePluginDataRelayService relay, IFramework framework)
    {
        return new PluginDataUnsubscribeOperation(relay, framework);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsUnsubscribeSuccess_WhenChannelExists()
    {
        var relay = new FakePluginDataRelayService();
        relay.Subscribe("MyPlugin", "status");
        var framework = CreateFramework();
        var operation = CreateOperation(relay, framework);
        var request = new PluginDataUnsubscribeOperation.Request { Channel = "MyPlugin.status" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        Assert.Equal("unsubscribe_success", result.Status);
        Assert.Contains("退订成功", result.SummaryText);
        Assert.False(relay.IsSubscribed("MyPlugin.status"));
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNotSubscribed_WhenChannelNotFound()
    {
        var relay = new FakePluginDataRelayService();
        var framework = CreateFramework();
        var operation = CreateOperation(relay, framework);
        var request = new PluginDataUnsubscribeOperation.Request { Channel = "NonexistentPlugin.data" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        Assert.Equal("not_subscribed", result.Status);
        Assert.Contains("不存在", result.SummaryText);
    }

    [Fact]
    public void Constructor_RejectsNullRelayService()
    {
        var framework = CreateFramework();
        var ex = Assert.Throws<ArgumentNullException>(
            () => new PluginDataUnsubscribeOperation(null!, framework));
        Assert.Equal("relay", ex.ParamName);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsArgumentException_WhenChannelEmpty()
    {
        var relay = new FakePluginDataRelayService();
        var framework = CreateFramework();
        var operation = CreateOperation(relay, framework);
        var request = new PluginDataUnsubscribeOperation.Request { Channel = "" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => operation.ExecuteAsync(request, context).AsTask());
        Assert.Equal("request.Channel", ex.ParamName);
    }

    [Fact]
    public async Task ExecuteAsync_UnsubscribeClearsData()
    {
        var relay = new FakePluginDataRelayService();
        relay.Subscribe("MyPlugin", "status");
        relay.WriteData("MyPlugin.status", "test-data");
        var framework = CreateFramework();
        var operation = CreateOperation(relay, framework);
        var request = new PluginDataUnsubscribeOperation.Request { Channel = "MyPlugin.status" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        Assert.Equal("unsubscribe_success", result.Status);

        // Verify channel is gone - TryPoll should return false
        bool pollSuccess = relay.TryPoll("MyPlugin.status", out var data);
        Assert.False(pollSuccess);
        Assert.Empty(data);
    }
}
