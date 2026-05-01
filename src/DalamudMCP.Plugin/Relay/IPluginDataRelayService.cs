namespace DalamudMCP.Plugin.Relay;

/// <summary>
/// 管理目标插件通过 IPC 向 DalamudMCP 推送数据的服务。
/// 每个订阅创建一个有界 Channel + IPC Provider，AI 通过 MCP 工具轮询数据。
/// </summary>
public interface IPluginDataRelayService
{
    /// <summary>
    /// 订阅数据回传通道——注册 IPC Provider 并创建有界 Channel。
    /// </summary>
    /// <param name="pluginName">目标插件的内部名称（InternalName）</param>
    /// <param name="channelName">通道名（不含插件名前缀）</param>
    /// <param name="capacity">Channel 容量，默认 1000</param>
    /// <returns>订阅成功返回 true；通道已存在返回 false（幂等——不覆盖已有通道）</returns>
    public bool Subscribe(string pluginName, string channelName, int capacity = 1000);

    /// <summary>
    /// 退订数据回传通道——注销 IPC Provider 并关闭 Channel。
    /// </summary>
    /// <param name="fullChannelName">完整通道名（{PluginName}.{ChannelName}，如 "MyPlugin.status"）</param>
    /// <returns>退订成功返回 true；通道不存在返回 false</returns>
    public bool Unsubscribe(string fullChannelName);

    /// <summary>
    /// 非阻塞轮询通道中的所有可用数据。
    /// </summary>
    /// <param name="fullChannelName">完整通道名（{PluginName}.{ChannelName}）</param>
    /// <param name="data">输出参数：所有可用的数据项（JSON 字符串列表）。通道不存在或为空时为空列表。</param>
    /// <returns>通道存在返回 true；不存在返回 false</returns>
    public bool TryPoll(string fullChannelName, out IReadOnlyList<string> data);

    /// <summary>
    /// 检查通道是否已订阅。
    /// </summary>
    public bool IsSubscribed(string fullChannelName);

    /// <summary>
    /// 获取所有活跃通道的完整名称。
    /// </summary>
    public IReadOnlyCollection<string> ActiveChannels { get; }
}
