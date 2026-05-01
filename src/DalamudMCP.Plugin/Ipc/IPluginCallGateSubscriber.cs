namespace DalamudMCP.Plugin.Ipc;

/// <summary>
/// 封装任意 IPC CallGate 订阅者的反射调用能力。
/// </summary>
public interface IPluginCallGateSubscriber
{
    /// <summary>
    /// 获取目标 IPC 函数是否已注册。
    /// </summary>
    bool HasFunction { get; }

    /// <summary>
    /// 通过反射调用目标 IPC 函数并返回结果。
    /// </summary>
    /// <param name="arguments">IPC 函数的实参列表。</param>
    /// <returns>IPC 函数的返回值；若无返回值则为 <c>null</c>。</returns>
    object? InvokeFunc(IReadOnlyList<object?> arguments);
}
