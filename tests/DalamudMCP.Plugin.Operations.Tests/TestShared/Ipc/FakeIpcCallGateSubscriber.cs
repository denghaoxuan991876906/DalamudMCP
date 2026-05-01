using DalamudMCP.Plugin.Ipc;

namespace DalamudMCP.Plugin.Operations.Tests.TestShared.Ipc;

/// <summary>
/// IPluginCallGateSubscriber 的公共测试桩，可供 Phase 12/14/15 测试复用。
/// </summary>
public sealed class FakeIpcCallGateSubscriber : IPluginCallGateSubscriber
{
    public FakeIpcCallGateSubscriber(bool hasFunction, object? result = null)
    {
        HasFunction = hasFunction;
        _result = result;
    }

    public bool HasFunction { get; }

    private readonly object? _result;

    public object? InvokeFunc(IReadOnlyList<object?> arguments)
    {
        _ = arguments;
        return _result;
    }
}
