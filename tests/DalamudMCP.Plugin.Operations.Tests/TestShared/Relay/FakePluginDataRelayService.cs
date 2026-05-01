using System.Collections.Concurrent;
using System.Threading.Channels;
using DalamudMCP.Plugin.Relay;

namespace DalamudMCP.Plugin.Operations.Tests.TestShared.Relay;

/// <summary>
/// IPluginDataRelayService 的可控测试桩，使用 ConcurrentDictionary + Channel(Of String) 实现纯内存通道管理。
/// 提供 WriteData 测试辅助方法，用于模拟目标插件通过 IPC 推送数据。
/// </summary>
public sealed class FakePluginDataRelayService : IPluginDataRelayService
{
    private readonly ConcurrentDictionary<string, (Channel<string> Channel, bool Subscribed)> channels = new(StringComparer.OrdinalIgnoreCase);

    public bool Subscribe(string pluginName, string channelName, int capacity = 1000)
    {
        string fullName = $"{pluginName}.{channelName}";
        // 幂等——已存在返回 false（不覆盖）
        if (channels.ContainsKey(fullName))
            return false;

        BoundedChannelOptions options = new(capacity) { FullMode = BoundedChannelFullMode.DropOldest };
        Channel<string> channel = Channel.CreateBounded<string>(options);
        channels[fullName] = (channel, true);
        return true;
    }

    public bool Unsubscribe(string fullChannelName)
    {
        if (!channels.TryRemove(fullChannelName, out var entry))
            return false;
        entry.Channel.Writer.TryComplete();
        return true;
    }

    public bool TryPoll(string fullChannelName, out IReadOnlyList<string> data)
    {
        if (!channels.TryGetValue(fullChannelName, out var entry))
        {
            data = [];
            return false;
        }

        List<string> items = [];
        while (entry.Channel.Reader.TryRead(out string? item))
            items.Add(item);

        data = items;
        return true;
    }

    public bool IsSubscribed(string fullChannelName)
    {
        return channels.ContainsKey(fullChannelName);
    }

    public IReadOnlyCollection<string> ActiveChannels => channels.Keys.ToArray();

    /// <summary>
    /// 测试辅助方法——向指定通道直接写入数据（模拟目标插件通过 IPC 推送）。
    /// 使用 TryWrite 确保非阻塞。
    /// </summary>
    public bool WriteData(string fullChannelName, string jsonData)
    {
        if (!channels.TryGetValue(fullChannelName, out var entry))
            return false;
        return entry.Channel.Writer.TryWrite(jsonData);
    }

    /// <summary>
    /// 测试辅助方法——获取通道当前缓冲数据条数（不消费）。
    /// </summary>
    public int GetBufferedCount(string fullChannelName)
    {
        if (!channels.TryGetValue(fullChannelName, out var entry))
            return 0;
        return entry.Channel.Reader.Count;
    }
}
