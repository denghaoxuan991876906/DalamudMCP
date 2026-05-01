using DalamudMCP.Plugin.Ipc;

namespace DalamudMCP.Plugin.Operations.Tests.TestShared.Ipc;

/// <summary>
/// IPluginIpcGateway 的公共测试桩，可供 Phase 12/14/15 测试复用。
/// </summary>
public sealed class FakeIpcGateway : IPluginIpcGateway
{
    private readonly Dictionary<string, IPluginCallGateSubscriber> subscribers;

    public FakeIpcGateway(params (string Callgate, IPluginCallGateSubscriber Subscriber)[] entries)
    {
        subscribers = entries.ToDictionary(
            static entry => entry.Callgate,
            static entry => entry.Subscriber,
            StringComparer.Ordinal);
    }

    public bool TryCreate(string callgate, IReadOnlyList<Type> typeArguments, out IPluginCallGateSubscriber? subscriber)
    {
        _ = typeArguments;
        return subscribers.TryGetValue(callgate, out subscriber);
    }
}
