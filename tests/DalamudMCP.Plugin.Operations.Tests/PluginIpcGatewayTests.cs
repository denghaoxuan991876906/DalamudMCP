using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using DalamudMCP.Plugin.Ipc;
using NSubstitute;

namespace DalamudMCP.Plugin.Operations.Tests;

public sealed class PluginIpcGatewayTests
{
    [Fact]
    public void TryCreate_returns_false_when_callgate_is_unavailable()
    {
        IDalamudPluginInterface pluginInterface = Substitute.For<IDalamudPluginInterface>();
        // NSubstitute 默认对接口返回值自动创建 substitute，需显式配置返回 null 模拟无匹配 callgate。
        pluginInterface.GetIpcSubscriber<bool>(Arg.Any<string>())
            .Returns((ICallGateSubscriber<bool>?)null);
        PluginIpcGateway gateway = new(pluginInterface);

        bool result = gateway.TryCreate("NonExistent.Callgate", [typeof(bool)], out IPluginCallGateSubscriber? subscriber);

        Assert.False(result);
        Assert.Null(subscriber);
    }

    [Fact]
    public void TryCreate_returns_true_for_registered_callgate()
    {
        IDalamudPluginInterface pluginInterface = Substitute.For<IDalamudPluginInterface>();
        ICallGateSubscriber<bool> callGateSubscriber = Substitute.For<ICallGateSubscriber<bool>>();
        callGateSubscriber.HasFunction.Returns(true);
        pluginInterface.GetIpcSubscriber<bool>(Arg.Any<string>()).Returns(callGateSubscriber);
        PluginIpcGateway gateway = new(pluginInterface);

        bool result = gateway.TryCreate("Test.Callgate", [typeof(bool)], out IPluginCallGateSubscriber? subscriber);

        Assert.True(result);
        Assert.NotNull(subscriber);
        Assert.True(subscriber.HasFunction);
    }

    [Fact]
    public void TryCreate_throws_for_invalid_callgate()
    {
        IDalamudPluginInterface pluginInterface = Substitute.For<IDalamudPluginInterface>();
        PluginIpcGateway gateway = new(pluginInterface);

        // null callgate 抛出 ArgumentNullException
        Assert.Throws<ArgumentNullException>(() => gateway.TryCreate(null!, [typeof(object)], out _));
        // 空白 callgate 抛出 ArgumentException（ArgumentNullException 是其子类）
        Assert.Throws<ArgumentException>(() => gateway.TryCreate("  ", [typeof(object)], out _));
    }

    [Fact]
    public void TryCreate_throws_ArgumentNullException_for_null_typeArguments()
    {
        IDalamudPluginInterface pluginInterface = Substitute.For<IDalamudPluginInterface>();
        PluginIpcGateway gateway = new(pluginInterface);

        Assert.Throws<ArgumentNullException>(() => gateway.TryCreate("Test.Callgate", null!, out _));
    }

    [Fact]
    public void Constructor_throws_ArgumentNullException_for_null_pluginInterface()
    {
        Assert.Throws<ArgumentNullException>(() => new PluginIpcGateway(null!));
    }
}
