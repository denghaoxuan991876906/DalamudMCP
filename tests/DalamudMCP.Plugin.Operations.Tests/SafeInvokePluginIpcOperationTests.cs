using Dalamud.Plugin.Services;
using DalamudMCP.Framework;
using DalamudMCP.Plugin.Ipc;
using DalamudMCP.Plugin.Operations.Tests.TestShared.Ipc;
using NSubstitute;

namespace DalamudMCP.Plugin.Operations.Tests;

public sealed class SafeInvokePluginIpcOperationTests
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

    private static SafeInvokePluginIpcOperation CreateOperation(
        Func<SafeInvokePluginIpcOperation.Request, CancellationToken, ValueTask<SafeInvokePluginIpcResult>> executor)
    {
        return new SafeInvokePluginIpcOperation(executor);
    }

    private static SafeInvokePluginIpcOperation CreateOperation(IPluginIpcGateway gateway, IFramework framework)
    {
        return new SafeInvokePluginIpcOperation(gateway, framework);
    }

    // ── 成功路径：无参数调用 ──

    [Fact]
    public void InvokeSafeIpc_ReturnsSuccess_WithNoArguments()
    {
        var gateway = new FakeIpcGateway(("TestPlugin.MCP.Ping", new FakeIpcCallGateSubscriber(true, true)));
        var request = new SafeInvokePluginIpcOperation.Request
        {
            PluginName = "TestPlugin",
            Method = "Ping",
            ArgumentsJson = null
        };

        var result = SafeInvokePluginIpcOperation.InvokeSafeIpc(gateway, request);

        Assert.True(result.Success);
        Assert.Equal("ipc_success", result.Status);
        Assert.Equal("true", result.ReturnValue);
        Assert.Equal("Ping", result.Method);
        Assert.Equal("TestPlugin", result.PluginName);
        Assert.Null(result.ErrorMessage);
        Assert.Contains("succeeded", result.SummaryText);
    }

    [Fact]
    public void InvokeSafeIpc_ReturnsSuccess_WithSingleIntArgument()
    {
        var gateway = new FakeIpcGateway(("TestPlugin.MCP.AddOne", new FakeIpcCallGateSubscriber(true, 43)));
        var request = new SafeInvokePluginIpcOperation.Request
        {
            PluginName = "TestPlugin",
            Method = "AddOne",
            ArgumentsJson = "[42]"
        };

        var result = SafeInvokePluginIpcOperation.InvokeSafeIpc(gateway, request);

        Assert.True(result.Success);
        Assert.Equal("ipc_success", result.Status);
        Assert.Equal("43", result.ReturnValue);
    }

    [Fact]
    public void InvokeSafeIpc_ReturnsSuccess_WithMultipleArguments()
    {
        var gateway = new FakeIpcGateway(("TestPlugin.MCP.Process", new FakeIpcCallGateSubscriber(true, "done")));
        var request = new SafeInvokePluginIpcOperation.Request
        {
            PluginName = "TestPlugin",
            Method = "Process",
            ArgumentsJson = "[42,\"hello\",true]"
        };

        var result = SafeInvokePluginIpcOperation.InvokeSafeIpc(gateway, request);

        Assert.True(result.Success);
        Assert.Equal("ipc_success", result.Status);
    }

    [Fact]
    public void InvokeSafeIpc_ReturnsSuccess_WithJsonEnvelopeArgument()
    {
        var gateway = new FakeIpcGateway(("TestPlugin.MCP.PassObject", new FakeIpcCallGateSubscriber(true, "processed")));
        var request = new SafeInvokePluginIpcOperation.Request
        {
            PluginName = "TestPlugin",
            Method = "PassObject",
            ArgumentsJson = "[{\"key\":\"value\"}]"
        };

        var result = SafeInvokePluginIpcOperation.InvokeSafeIpc(gateway, request);

        Assert.True(result.Success);
        Assert.Equal("ipc_success", result.Status);
        Assert.NotNull(result.ReturnValue);
    }

    // ── 成功路径：类型推断 ──

    [Fact]
    public void InvokeSafeIpc_InfersIntType_ForIntegerJsonNumber()
    {
        var gateway = new FakeIpcGateway(("TestPlugin.MCP.IntTest", new FakeIpcCallGateSubscriber(true, 99)));
        var request = new SafeInvokePluginIpcOperation.Request
        {
            PluginName = "TestPlugin",
            Method = "IntTest",
            ArgumentsJson = "[42]"
        };

        var result = SafeInvokePluginIpcOperation.InvokeSafeIpc(gateway, request);

        Assert.True(result.Success);
        Assert.Equal("ipc_success", result.Status);
    }

    [Fact]
    public void InvokeSafeIpc_InfersDoubleType_ForDecimalJsonNumber()
    {
        var gateway = new FakeIpcGateway(("TestPlugin.MCP.DoubleTest", new FakeIpcCallGateSubscriber(true, 6.28)));
        var request = new SafeInvokePluginIpcOperation.Request
        {
            PluginName = "TestPlugin",
            Method = "DoubleTest",
            ArgumentsJson = "[3.14]"
        };

        var result = SafeInvokePluginIpcOperation.InvokeSafeIpc(gateway, request);

        Assert.True(result.Success);
        Assert.Equal("ipc_success", result.Status);
    }

    [Fact]
    public void InvokeSafeIpc_InfersObjectType_ForNullArgument()
    {
        var gateway = new FakeIpcGateway(("TestPlugin.MCP.NullTest", new FakeIpcCallGateSubscriber(true, null)));
        var request = new SafeInvokePluginIpcOperation.Request
        {
            PluginName = "TestPlugin",
            Method = "NullTest",
            ArgumentsJson = "[null]"
        };

        var result = SafeInvokePluginIpcOperation.InvokeSafeIpc(gateway, request);

        Assert.True(result.Success);
        Assert.Equal("ipc_success", result.Status);
    }

    // ── 成功路径：布尔值 + 变长参数 ──

    [Fact]
    public void InvokeSafeIpc_ReturnsSuccess_WithBoolArgument()
    {
        var gateway = new FakeIpcGateway(("TestPlugin.MCP.BoolTest", new FakeIpcCallGateSubscriber(true, false)));
        var request = new SafeInvokePluginIpcOperation.Request
        {
            PluginName = "TestPlugin",
            Method = "BoolTest",
            ArgumentsJson = "[true]"
        };

        var result = SafeInvokePluginIpcOperation.InvokeSafeIpc(gateway, request);

        Assert.True(result.Success);
        Assert.Equal("false", result.ReturnValue);
    }

    [Fact]
    public void InvokeSafeIpc_ReturnsSuccess_WithFalseBoolArgument()
    {
        var gateway = new FakeIpcGateway(("TestPlugin.MCP.BoolTest", new FakeIpcCallGateSubscriber(true, "ok")));
        var request = new SafeInvokePluginIpcOperation.Request
        {
            PluginName = "TestPlugin",
            Method = "BoolTest",
            ArgumentsJson = "[false]"
        };

        var result = SafeInvokePluginIpcOperation.InvokeSafeIpc(gateway, request);

        Assert.True(result.Success);
        Assert.Equal("ipc_success", result.Status);
    }

    [Fact]
    public void InvokeSafeIpc_ReturnsSuccess_WithStringArgument()
    {
        var gateway = new FakeIpcGateway(("TestPlugin.MCP.StringTest", new FakeIpcCallGateSubscriber(true, "response")));
        var request = new SafeInvokePluginIpcOperation.Request
        {
            PluginName = "TestPlugin",
            Method = "StringTest",
            ArgumentsJson = "[\"test\"]"
        };

        var result = SafeInvokePluginIpcOperation.InvokeSafeIpc(gateway, request);

        Assert.True(result.Success);
        Assert.Contains("response", result.ReturnValue);
    }

    // ── 错误路径：ipc_missing ──

    [Fact]
    public void InvokeSafeIpc_ReturnsIpcMissing_WhenGatewayHasNoMatchingCallgate()
    {
        var gateway = new FakeIpcGateway();
        var request = new SafeInvokePluginIpcOperation.Request
        {
            PluginName = "Missing",
            Method = "Action"
        };

        var result = SafeInvokePluginIpcOperation.InvokeSafeIpc(gateway, request);

        Assert.False(result.Success);
        Assert.Equal("ipc_missing", result.Status);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("ipc_missing", result.SummaryText);
    }

    [Fact]
    public void InvokeSafeIpc_ReturnsIpcMissing_WhenCallgateDifferent()
    {
        var gateway = new FakeIpcGateway(("Other.MCP.X", new FakeIpcCallGateSubscriber(true)));
        var request = new SafeInvokePluginIpcOperation.Request
        {
            PluginName = "TestPlugin",
            Method = "Ping"
        };

        var result = SafeInvokePluginIpcOperation.InvokeSafeIpc(gateway, request);

        Assert.Equal("ipc_missing", result.Status);
    }

    // ── 错误路径：ipc_not_ready ──

    [Fact]
    public void InvokeSafeIpc_ReturnsIpcNotReady_WhenHasFunctionIsFalse()
    {
        var gateway = new FakeIpcGateway(("TestPlugin.MCP.Ping", new FakeIpcCallGateSubscriber(false)));
        var request = new SafeInvokePluginIpcOperation.Request
        {
            PluginName = "TestPlugin",
            Method = "Ping"
        };

        var result = SafeInvokePluginIpcOperation.InvokeSafeIpc(gateway, request);

        Assert.False(result.Success);
        Assert.Equal("ipc_not_ready", result.Status);
        Assert.Contains("not registered", result.ErrorMessage);
        Assert.Contains("ipc_not_ready", result.SummaryText);
    }

    // ── 错误路径：ipc_type_mismatch ──

    [Fact]
    public void InvokeSafeIpc_ReturnsIpcTypeMismatch_WhenInvalidCastExceptionThrown()
    {
        var invalidCastSub = Substitute.For<IPluginCallGateSubscriber>();
        invalidCastSub.HasFunction.Returns(true);
        invalidCastSub.InvokeFunc(Arg.Any<IReadOnlyList<object?>>())
            .Returns(x => throw new InvalidCastException("Type mismatch"));

        var gateway = new FakeIpcGateway(("TestPlugin.MCP.Ping", invalidCastSub));
        var request = new SafeInvokePluginIpcOperation.Request
        {
            PluginName = "TestPlugin",
            Method = "Ping"
        };

        var result = SafeInvokePluginIpcOperation.InvokeSafeIpc(gateway, request);

        Assert.Equal("ipc_type_mismatch", result.Status);
        Assert.Contains("Type mismatch", result.ErrorMessage);
        Assert.Contains("type mismatch", result.SummaryText);

        // Also verify TargetInvocationException wrapping InvalidCastException
        var wrappedCastSub = Substitute.For<IPluginCallGateSubscriber>();
        wrappedCastSub.HasFunction.Returns(true);
        wrappedCastSub.InvokeFunc(Arg.Any<IReadOnlyList<object?>>())
            .Returns(x => throw new TargetInvocationException(new InvalidCastException("Type mismatch wrapped")));

        var gateway2 = new FakeIpcGateway(("TestPlugin.MCP.Ping", wrappedCastSub));

        var result2 = SafeInvokePluginIpcOperation.InvokeSafeIpc(gateway2, request);

        Assert.Equal("ipc_type_mismatch", result2.Status);
        Assert.Contains("Type mismatch", result2.ErrorMessage);
    }

    // ── 错误路径：ipc_plugin_error ──

    [Fact]
    public void InvokeSafeIpc_ReturnsIpcPluginError_WhenTargetPluginThrows()
    {
        var sub = Substitute.For<IPluginCallGateSubscriber>();
        sub.HasFunction.Returns(true);
        sub.InvokeFunc(Arg.Any<IReadOnlyList<object?>>())
            .Returns(x => throw new InvalidOperationException("Plugin logic error"));

        var gateway = new FakeIpcGateway(("TestPlugin.MCP.Ping", sub));
        var request = new SafeInvokePluginIpcOperation.Request
        {
            PluginName = "TestPlugin",
            Method = "Ping"
        };

        var result = SafeInvokePluginIpcOperation.InvokeSafeIpc(gateway, request);

        Assert.Equal("ipc_plugin_error", result.Status);
        Assert.Contains("Plugin logic error", result.ErrorMessage);
        Assert.Contains("plugin error", result.SummaryText);
    }

    [Fact]
    public void InvokeSafeIpc_ReturnsIpcPluginError_WhenTargetInvocationExceptionWithoutInvalidCast()
    {
        var sub = Substitute.For<IPluginCallGateSubscriber>();
        sub.HasFunction.Returns(true);
        sub.InvokeFunc(Arg.Any<IReadOnlyList<object?>>())
            .Returns(x => throw new TargetInvocationException(new InvalidOperationException("Inner error")));

        var gateway = new FakeIpcGateway(("TestPlugin.MCP.Ping", sub));
        var request = new SafeInvokePluginIpcOperation.Request
        {
            PluginName = "TestPlugin",
            Method = "Ping"
        };

        var result = SafeInvokePluginIpcOperation.InvokeSafeIpc(gateway, request);

        Assert.Equal("ipc_plugin_error", result.Status);
        Assert.Contains("Inner error", result.ErrorMessage);
    }

    [Fact]
    public async Task InvokeSafeIpc_ReturnsIpcPluginError_WhenArgumentsJsonIsInvalid()
    {
        var gateway = new FakeIpcGateway(("TestPlugin.MCP.Ping", new FakeIpcCallGateSubscriber(true, "ok")));
        var framework = CreateFramework(isInFrameworkThread: true);
        var operation = CreateOperation(gateway, framework);
        var request = new SafeInvokePluginIpcOperation.Request
        {
            PluginName = "TestPlugin",
            Method = "Ping",
            ArgumentsJson = "not json"
        };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.Equal("ipc_plugin_error", result.Status);
    }

    // ── Framework 线程编排 ──

    [Fact]
    public async Task ExecuteAsync_CallsInvokeDirectly_WhenAlreadyOnFrameworkThread()
    {
        var framework = CreateFramework(isInFrameworkThread: true);
        var gateway = new FakeIpcGateway(("TestPlugin.MCP.Ping", new FakeIpcCallGateSubscriber(true, "ok")));
        var operation = CreateOperation(gateway, framework);
        var request = new SafeInvokePluginIpcOperation.Request
        {
            PluginName = "TestPlugin",
            Method = "Ping"
        };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        await framework.DidNotReceive().RunOnFrameworkThread(Arg.Any<Action>());
    }

    [Fact]
    public async Task ExecuteAsync_CallsRunOnFrameworkThread_WhenNotOnFrameworkThread()
    {
        var framework = CreateFramework(isInFrameworkThread: false);
        var gateway = new FakeIpcGateway(("TestPlugin.MCP.Ping", new FakeIpcCallGateSubscriber(true, "ok")));
        var operation = CreateOperation(gateway, framework);
        var request = new SafeInvokePluginIpcOperation.Request
        {
            PluginName = "TestPlugin",
            Method = "Ping"
        };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        var result = await operation.ExecuteAsync(request, context);

        Assert.True(result.Success);
        await framework.Received(1).RunOnFrameworkThread(Arg.Any<Action>());
    }

    // ── 构造函数验证 ──

    [Fact]
    public void Constructor_RejectsNullGateway()
    {
        var framework = CreateFramework();

        var ex = Assert.Throws<ArgumentNullException>(
            () => new SafeInvokePluginIpcOperation(null!, framework));
        Assert.Equal("gateway", ex.ParamName);
    }

    [Fact]
    public void Constructor_RejectsNullFramework()
    {
        var gateway = new FakeIpcGateway();

        var ex = Assert.Throws<ArgumentNullException>(
            () => new SafeInvokePluginIpcOperation(gateway, null!));
        Assert.Equal("framework", ex.ParamName);
    }

    [Fact]
    public void ExecuteAsync_RejectsNullRequest()
    {
        var operation = CreateOperation((r, ct) => ValueTask.FromResult(
            new SafeInvokePluginIpcResult("", "", true, "ipc_success", null, null, "")));

        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

#pragma warning disable CA2012
        var ex = Assert.Throws<ArgumentNullException>(
            () => operation.ExecuteAsync(null!, context));
#pragma warning restore CA2012
        Assert.Equal("request", ex.ParamName);
    }

    // ── 输入验证 ──

    [Fact]
    public async Task ExecuteAsync_ThrowsArgumentException_WhenPluginNameIsEmpty()
    {
        var framework = CreateFramework(isInFrameworkThread: true);
        var gateway = new FakeIpcGateway(("TestPlugin.MCP.Ping", new FakeIpcCallGateSubscriber(true, "ok")));
        var operation = CreateOperation(gateway, framework);
        var request = new SafeInvokePluginIpcOperation.Request { PluginName = "", Method = "Ping" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(
            () => operation.ExecuteAsync(request, context).AsTask());
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsArgumentException_WhenMethodIsEmpty()
    {
        var framework = CreateFramework(isInFrameworkThread: true);
        var gateway = new FakeIpcGateway(("TestPlugin.MCP.Ping", new FakeIpcCallGateSubscriber(true, "ok")));
        var operation = CreateOperation(gateway, framework);
        var request = new SafeInvokePluginIpcOperation.Request { PluginName = "Test", Method = "" };
        var context = OperationContext.ForMcp(cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(
            () => operation.ExecuteAsync(request, context).AsTask());
    }
}
