namespace DalamudMCP.Plugin.Ipc;

/// <summary>
/// 封装任意 IPC CallGate 订阅者的反射调用能力。
/// </summary>
public interface IPluginCallGateSubscriber
{
    /// <summary>
    /// 获取目标 IPC 函数是否已注册。
    /// </summary>
    public bool HasFunction { get; }

    public object? InvokeFunc(IReadOnlyList<object?> arguments);
}
