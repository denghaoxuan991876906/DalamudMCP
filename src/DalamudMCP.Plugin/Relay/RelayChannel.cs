using System.Threading.Channels;
using Dalamud.Plugin.Ipc;

namespace DalamudMCP.Plugin.Relay;

/// <summary>
/// 封装一个数据回传通道的全部资源。
/// </summary>
/// <param name="Channel">有界 Channel，用于暂存目标插件推送的 JSON 数据</param>
/// <param name="IpcProvider">IPC Provider——调用 UnregisterAction() 即可注销 IPC 端点</param>
/// <param name="PluginName">目标插件内部名称（用于自动清理检测）</param>
/// <param name="FullChannelName">完整通道名（{PluginName}.{ChannelName}）</param>
/// <param name="CreatedAt">创建时间戳</param>
internal sealed record RelayChannel(
    Channel<string> Channel,
    ICallGateProvider<string, object> IpcProvider,
    string PluginName,
    string FullChannelName,
    DateTime CreatedAt);
