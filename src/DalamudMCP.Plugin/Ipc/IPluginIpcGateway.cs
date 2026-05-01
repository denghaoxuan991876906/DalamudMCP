namespace DalamudMCP.Plugin.Ipc;

/// <summary>
/// IPC 网关抽象——通过 Dalamud 的 <c>IDalamudPluginInterface</c> 反射订阅其他插件的 IPC CallGate。
/// </summary>
public interface IPluginIpcGateway
{
    /// <summary>
    /// 尝试为指定 CallGate 创建 IPC 订阅者。
    /// </summary>
    /// <param name="callgate">IPC CallGate 名称。</param>
    /// <param name="typeArguments">泛型类型参数列表（参数类型 + 返回类型）。</param>
    /// <param name="subscriber">成功时返回封装了反射调用能力的订阅者；失败时为 <c>null</c>。</param>
    /// <returns>订阅成功返回 <c>true</c>；否则返回 <c>false</c>。</returns>
    public bool TryCreate(string callgate, IReadOnlyList<Type> typeArguments, out IPluginCallGateSubscriber? subscriber);
}
