using System.Collections.Concurrent;
using System.Threading.Channels;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace DalamudMCP.Plugin.Relay;

internal sealed class PluginDataRelayService : IPluginDataRelayService, IDisposable
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IFramework framework;
    private readonly ConcurrentDictionary<string, RelayChannel> channels = new(StringComparer.OrdinalIgnoreCase);
    private int frameCounter;
    private bool disposed;

    public PluginDataRelayService(IDalamudPluginInterface pluginInterface, IFramework framework)
    {
        this.pluginInterface = pluginInterface ?? throw new ArgumentNullException(nameof(pluginInterface));
        this.framework = framework ?? throw new ArgumentNullException(nameof(framework));
        framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        framework.Update -= OnFrameworkUpdate;

        foreach (RelayChannel channel in channels.Values)
        {
            channel.IpcProvider.UnregisterAction();
            channel.Channel.Writer.TryComplete();
        }
        channels.Clear();
    }

    public bool Subscribe(string pluginName, string channelName, int capacity = 1000)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        ArgumentException.ThrowIfNullOrWhiteSpace(channelName);

        string fullName = $"{pluginName}.{channelName}";

        // 幂等检查——通道已存在时返回 false（不覆盖）
        if (channels.ContainsKey(fullName))
            return false;

        string callGate = $"DalamudMCP.Relay.{pluginName}.{channelName}";

        // 创建有界 Channel
        BoundedChannelOptions options = new(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        };
        Channel<string> channel = Channel.CreateBounded<string>(options);

        // 注册 IPC Provider
        // [ASSUMED: IDalamudPluginInterface.GetIpcProvider<string, object>() API 存在于 Dalamud SDK 15.0.0]
        var provider = pluginInterface.GetIpcProvider<string, object>(callGate);
        provider.RegisterAction(jsonData =>
        {
            // IPC 回调——将目标插件推送的 JSON 数据写入 Channel
            // Channel<T>.Writer.TryWrite() 是线程安全的，无需额外同步
            channel.Writer.TryWrite(jsonData);
        });

        RelayChannel entry = new(channel, provider, pluginName, fullName, DateTime.UtcNow);
        return channels.TryAdd(fullName, entry);
    }

    public bool Unsubscribe(string fullChannelName)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullChannelName);

        if (!channels.TryRemove(fullChannelName, out RelayChannel? entry))
            return false;

        // 先注销 IPC Provider（阻止新数据进入）
        entry.IpcProvider.UnregisterAction();
        // 再关闭 Channel Writer（使后续 TryWrite 安全失败）
        // TryComplete 不会抛异常，即使 Channel 已有数据或已被读取完毕
        entry.Channel.Writer.TryComplete();

        return true;
    }

    public bool TryPoll(string fullChannelName, out IReadOnlyList<string> data)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullChannelName);

        if (!channels.TryGetValue(fullChannelName, out RelayChannel? entry))
        {
            data = [];
            return false;
        }

        List<string> items = [];
        while (entry.Channel.Reader.TryRead(out string? item))
        {
            items.Add(item);
        }

        data = items;
        return true;
    }

    public bool IsSubscribed(string fullChannelName)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return channels.ContainsKey(fullChannelName);
    }

    public IReadOnlyCollection<string> ActiveChannels
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return channels.Keys.ToArray();
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        // 节流：每 60 帧检测一次（≈每秒一次，基于 ~60fps）
        if (++frameCounter % 60 != 0)
            return;

        // 仅在存在活跃通道时执行
        if (channels.IsEmpty)
            return;

        // 获取当前已安装插件列表
        // [ASSUMED: IDalamudPluginInterface.InstalledPlugins 属性存在于 Dalamud SDK 15.0.0]
        HashSet<string> installedNames = pluginInterface.InstalledPlugins
            .Select(static plugin => plugin.InternalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 检测已卸载的插件并自动退订
        foreach (KeyValuePair<string, RelayChannel> kvp in channels)
        {
            if (!installedNames.Contains(kvp.Value.PluginName))
            {
                Unsubscribe(kvp.Key);
            }
        }
    }
}
