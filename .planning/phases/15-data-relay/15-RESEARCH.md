# Phase 15: 数据回传 — 技术研究

**研究日期：** 2026-05-01
**阶段目标：** 目标插件通过 IPC 向 DalamudMCP 推送结构化数据，缓存于有界 Channel，AI 通过 MCP 工具轮询获取
**需求：** RELAY-01
**依赖：** Phase 14（安全 IPC 调用）
**置信度：** HIGH — 核心 IPC 基础设施、操作模式、测试桩、DI 注册模式均基于已完成的 Phase 11/14，可直接验证

---

<phase_requirements>
## Phase Requirements

| ID | 描述 | 研究支持 |
|----|------|---------|
| RELAY-01 | 目标插件能够通过 Dalamud IPC 向 DalamudMCP 发送结构化数据，DalamudMCP 缓存数据供 AI 通过 MCP 操作轮询获取 | §2（网关集成）、§3（Channel 设计）、§4（操作设计）、§5（MCP 工具）完整覆盖 |
</phase_requirements>

---

## 摘要

Phase 15 建立了一条**单向数据回传通道**：目标插件（被测插件）通过标准 Dalamud IPC 向 DalamudMCP 推送数据，DalamudMCP 内部将有界 `Channel<T>` 作为缓存，AI 通过 MCP 工具轮询缓存数据。

与 Phase 14 的关键区别：Phase 14 是 **DalamudMCP → 目标插件**（调用方向），Phase 15 是 **目标插件 → DalamudMCP**（推送方向）。这意味着 DalamudMCP 需要**注册 IPC 提供者（Provider）**而非创建订阅者（Subscriber）。现有 `IPluginIpcGateway` 专为创建订阅者设计，因此 Phase 15 需要一个**独立的中继服务**来管理 IPC 提供者的注册/注销。

**主要建议：** 创建 `PluginDataRelayService` 单例服务，内部管理 `ConcurrentDictionary<string, RelayChannel>`（通道名→有界 Channel + IPC Provider）。目标插件通过约定式 IPC CallGate `DalamudMCP.Relay.{plugin_name}.{channel}` 推送 JSON 字符串数据。三个新的 MCP 操作类（`plugin_data_subscribe`、`plugin_data_unsubscribe`、`plugin_data_poll`）遵循与 Phase 14 完全相同的代码模式：`[Operation]` + `[McpTool]` + `MemoryPack` 序列化的 Request/Result record + DI 构造注入 + Framework 线程编排。中继服务在 `PluginServiceCollectionExtensions` 中注册为单例，与 `IPluginIpcGateway` 同级。

---

## 架构责任映射

| 能力 | 主要层级 | 次要层级 | 理由 |
|------|---------|---------|------|
| IPC 提供者注册/注销 | API/Backend (DalamudMCP Plugin) | — | 使用 `IDalamudPluginInterface.GetIpcProvider<string, object>()` 在 Plugin 端注册，目标插件通过 Dalud IPC 调用 |
| 有界 Channel 缓存 | API/Backend (DalamudMCP Plugin) | — | `System.Threading.Channels.Channel<string>` 在内存中管理，`BoundedChannelFullMode.DropOldest` 策略 |
| 订阅生命周期管理 | API/Backend (DalamudMCP Plugin) | — | `PluginDataRelayService` 单例管理所有活跃通道 |
| 自动清理（插件卸载检测） | API/Backend (DalamudMCP Plugin) | — | 通过 `IFramework.Update` 事件或轮询时检测 `IDalamudPluginInterface.InstalledPlugins` |
| MCP 工具暴露 | Frontend Server (CLI) | API/Backend | MCP 工具描述由属性定义，CLI 端序列化到 MCP 协议；实际执行在 Plugin 端 |
| 数据序列化 | Database/Storage | API/Backend | JSON 字符串格式传输（目标插件自行序列化），MemoryPack 用于 Operation Request/Result |

---

## 标准技术栈

### 核心
| 库/API | 版本/来源 | 用途 | 为何标准 |
|---------|----------|------|---------|
| `System.Threading.Channels` | .NET 10 BCL | 有界 Channel 缓存，线程安全的生产者-消费者模式 | .NET 原生提供，零依赖，高性能无锁队列 |
| `IDalamudPluginInterface.GetIpcProvider<T1, TRet>()` | Dalamud SDK 15.0.0 | 注册 IPC 提供者，供其他插件订阅和调用 | Dalamud 原生 IPC API，目标插件零 SDK 依赖 [ASSUMED: 基于 Dalamud API 15 标准 IPC 模式] |
| `IFramework.Update` | Dalamud SDK 15.0.0 | 周期性检测插件卸载事件，触发自动清理 | Dalamud 标准游戏帧循环事件 [VERIFIED: src/DalamudMCP.Plugin/Operations/SafeInvokePluginIpcOperation.cs:85—使用 `IsInFrameworkUpdateThread`] |
| MemoryPack | 1.21.4 | Operation Request/Result 序列化 | 项目已使用，Phase 12/13/14 一致采用 [VERIFIED: src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj:11] |

### 支撑
| 库/API | 用途 | 何时使用 |
|---------|------|---------|
| `ConcurrentDictionary<string, RelayChannel>` | 线程安全的通道注册表 | 管理活跃订阅 |
| `BoundedChannelFullMode.DropOldest` | 有界队列溢出策略 | 配置 Channel 选项 |
| `IPluginIpcGateway`（现有） | 不直接使用 | Phase 15 需要 Provider 方向，现有 Gateway 仅支持 Subscriber 方向 |

### 考虑过的替代方案
| 已选定 | 可替代 | 权衡 |
|-------|-------|------|
| `Channel<string>` + `DropOldest` | `Channel<byte[]>` + `DropOldest` | `byte[]` 更通用但目标插件需自行序列化；`string` (JSON) 对目标插件更友好，与项目现有 JSON 模式一致 |
| `PluginDataRelayService` 单例服务 | 扩展 `IPluginIpcGateway` 添加 Provider 方法 | 后者污染已有两届阶段使用的稳定接口；独立服务职责清晰、零回归风险 |
| `IFramework.Update` 自动清理 | 仅在 `Poll` 时检查 | 后者可能导致死订阅长时间残留；前者主动清理更符合 SC4 要求 |
| `1000` 默认容量 | `100` / `10000` | `1000` 是合理的中间值，可配置化；Phase 14 无类似参数但有先例可参照 |

**安装：** 无额外 NuGet 包——`System.Threading.Channels` 是 .NET 10 BCL 的一部分，`MemoryPack` 已存在。

**版本验证：**
- .NET 10 BCL `System.Threading.Channels`: 随 .NET 10 SDK 提供 [VERIFIED: .NET 10 为当前框架版本]
- `MemoryPack` 1.21.4: [VERIFIED: src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj:11]

---

## 架构模式

### 系统架构图

```
┌──────────────────┐                      ┌─────────────────────────────────┐                      ┌──────────┐
│   目标插件        │   IPC InvokeAction   │     DalamudMCP Plugin             │   MCP poll/sub     │   AI     │
│  (TargetPlugin)   │ ────────────────────>│                                   │ <─────────────────>│  Client  │
│                   │   string (JSON)      │  ┌─────────────────────────────┐ │                    │          │
│  零 SDK 依赖      │                      │  │  PluginDataRelayService      │ │  plugin_data_poll  │          │
│  仅按约定调用 IPC  │     CallGate:        │  │  (Singleton)                 │ │  plugin_data_sub   │          │
│                   │     DalamudMCP.      │  │                              │ │  plugin_data_unsub │          │
│                   │     Relay.{name}     │  │  ConcurrentDictionary<       │ │                    │          │
│                   │                      │  │    string, RelayChannel>      │ │                    │          │
│                   │                      │  │                              │ │                    │          │
│                   │                      │  │  RelayChannel:               │ │                    │          │
│                   │                      │  │  ├─ Channel<string> (有界)    │ │                    │          │
│                   │                      │  │  ├─ ICallGateProvider (IPC)   │ │                    │          │
│                   │                      │  │  └─ PluginName (用于自动清理) │ │                    │          │
│                   │                      │  └─────────────────────────────┘ │                    │          │
│                   │                      │              ↑                    │                    │          │
│                   │                      │  IFramework.Update (自动清理检测)  │                    │          │
└──────────────────┘                      └─────────────────────────────────┘                      └──────────┘

数据流向：
  1. AI 发送 plugin_data_subscribe("MyPlugin", "status")  →  DalamudMCP 注册 IPC Provider + 创建 Channel
  2. 目标插件调用 DalamudMCP.Relay.MyPlugin.status → InvokeAction(jsonData)  →  Channel.Writer.TryWrite(jsonData)
  3. AI 发送 plugin_data_poll("MyPlugin.status")  →  Channel.Reader 批量读取所有可用数据
  4. AI 发送 plugin_data_unsubscribe("MyPlugin.status")  →  注销 IPC Provider + Dispose Channel
  5. 目标插件卸载时  →  IFramework.Update 检测 → 自动执行 unsubscribe 清理
```

### 推荐项目结构

```
src/DalamudMCP.Plugin/
├── Relay/                                        # 新目录：数据回传
│   ├── IPluginDataRelayService.cs               # 公开接口
│   ├── PluginDataRelayService.cs                # 实现：管理 Channel + IPC Provider
│   └── RelayChannel.cs                          # 内部 record：封装 Channel + Provider + 元数据
├── Operations/
│   ├── PluginDataPollOperation.cs               # 新文件：plugin_data_poll 操作
│   ├── PluginDataSubscribeOperation.cs          # 新文件：plugin_data_subscribe 操作
│   └── PluginDataUnsubscribeOperation.cs        # 新文件：plugin_data_unsubscribe 操作
├── Hosting/
│   ├── PluginServiceCollectionExtensions.cs     # 修改：注册 PluginDataRelayService 单例
│   └── PluginOperationExposurePolicy.cs         # 修改：将 relay 操作加入 UnsafeOperationIds

tests/DalamudMCP.Plugin.Operations.Tests/
├── TestShared/
│   └── Relay/
│       └── FakePluginDataRelayService.cs        # 新文件：测试桩
├── PluginDataPollOperationTests.cs              # 新文件：轮询操作测试
├── PluginDataSubscribeOperationTests.cs         # 新文件：订阅操作测试
└── PluginDataUnsubscribeOperationTests.cs       # 新文件：退订操作测试
```

### 模式 1: 操作类设计模式

**来源：** Phase 14 SafeInvokePluginIpcOperation.cs [VERIFIED: src/DalamudMCP.Plugin/Operations/SafeInvokePluginIpcOperation.cs:12-21]

**描述：** 每个 MCP 工具是一个实现了 `IOperation<TRequest, TResult>` 的 `sealed partial class`。关键组件：

1. **`[Operation("id")]` 属性** — 定义操作 ID、描述和摘要
2. **`[McpTool("name")]` 属性** — 定义 MCP 工具名称
3. **`[ResultFormatter(typeof(...))]` 属性** — 定义文本格式化器
4. **`Request` 嵌套类** — 用 `[MemoryPackable]` 和 `[ProtocolOperation("id")]` 属性标注，请求参数用 `[Option]` 标注
5. **`Result` record** — 用 `[MemoryPackable]` 标注，包含状态码和摘要文本
6. **构造注入** — 通过 DI 注入所需服务（`IPluginDataRelayService`、`IFramework`、`IDalamudPluginInterface`）
7. **内部测试构造器** — 接受 `Func<Request, CancellationToken, ValueTask<TResult>>` 用于单元测试
8. **Framework 线程编排** — 在 `IFramework.RunOnFrameworkThread` 上执行 Dalamud API 调用

**示例模式（捕获自 Phase 14）：**

```csharp
// 来源: SafeInvokePluginIpcOperation.cs — Phase 14 建立的完整操作模式
// [VERIFIED: src/DalamudMCP.Plugin/Operations/SafeInvokePluginIpcOperation.cs:12-63]

[Operation(
    "plugin.data.subscribe",
    Description = "订阅目标插件的数据回传通道...",
    Summary = "Subscribes to plugin data relay channel.")]
[ResultFormatter(typeof(PluginDataSubscribeOperation.TextFormatter))]
[CliCommand("plugin", "data", "subscribe")]
[McpTool("plugin_data_subscribe")]
public sealed partial class PluginDataSubscribeOperation
    : IOperation<PluginDataSubscribeOperation.Request, PluginDataSubscribeResult>
{
    private readonly Func<Request, CancellationToken, ValueTask<PluginDataSubscribeResult>> executor;

    [SupportedOSPlatform("windows")]
    public PluginDataSubscribeOperation(IPluginDataRelayService relay, IFramework framework, ...) { ... }
    internal PluginDataSubscribeOperation(Func<...> executor) { ... }

    public ValueTask<PluginDataSubscribeResult> ExecuteAsync(Request request, OperationContext context) { ... }

    [MemoryPackable]
    [ProtocolOperation("plugin.data.subscribe")]
    public sealed partial class Request
    {
        [Option("plugin-name", Description = "目标插件内部名称")]
        public string PluginName { get; init; } = string.Empty;

        [Option("channel", Description = "回传通道名称")]
        public string Channel { get; init; } = string.Empty;
    }

    public sealed class TextFormatter : IResultFormatter<PluginDataSubscribeResult> { ... }
}

[MemoryPackable]
public sealed partial record PluginDataSubscribeResult(
    string FullChannelName,
    string PluginName,
    bool Success,
    string Status,
    string? ErrorMessage,
    string SummaryText);
```

### 模式 2: IPC 提供者注册模式

**描述：** DalamudMCP 需要使用 `IDalamudPluginInterface.GetIpcProvider<T1, TRet>()` 注册 IPC 端点，目标插件通过标准 Dalamud IPC 订阅和调用。这是 Phase 14 方向的**逆向**。

```csharp
// DalamudMCP 注册 Provider（服务端）
// [ASSUMED: 基于 Dalamud API 15 标准 IPC 模式]
var provider = pluginInterface.GetIpcProvider<string, object>("DalamudMCP.Relay.MyPlugin.status");
provider.RegisterAction(jsonData =>
{
    // 在 Framework 线程调用——将数据写入 Channel
    channel.Writer.TryWrite(jsonData);
});

// 目标插件调用（客户端——零 SDK 依赖，纯 Dalamud 原生 API）
// [ASSUMED: 基于 Dalamud API 15 标准 IPC 模式]
var sub = pluginInterface.GetIpcSubscriber<string, object>("DalamudMCP.Relay.MyPlugin.status");
sub.InvokeAction("{\"value\": 42}");
```

**关键点：**
- CallGate 命名约定：`DalamudMCP.Relay.{PluginName}.{ChannelName}`（全小写，句点分隔）
- Provider 类型：`<string, object>`（参数为 JSON 字符串，无返回值）
- 使用 `RegisterAction`（非 `RegisterFunc`），因为这是 fire-and-forget 推送
- Provider 是 `IDisposable`——调用 `.Dispose()` 即可注销 IPC 端点

### 模式 3: 有界 Channel 与溢出策略

**描述：** `System.Threading.Channels.Channel<T>` 提供线程安全的生产者-消费者队列。

```csharp
// [CITED: Microsoft官方文档 System.Threading.Channels]
var options = new BoundedChannelOptions(capacity: 1000)
{
    FullMode = BoundedChannelFullMode.DropOldest  // 溢出时丢弃最旧数据
};
var channel = Channel.CreateBounded<string>(options);

// 生产者（IPC Provider action 中）
channel.Writer.TryWrite(jsonData);

// 消费者（Poll 操作中）
var items = new List<string>();
while (channel.Reader.TryRead(out string? item))
{
    items.Add(item);
}
```

**关键点：**
- `TryWrite` 非阻塞——当队列满时返回 `false`（配合 `DropOldest` 总是成功）
- `TryRead` 非阻塞——立即返回 false 如果队列为空
- `DropOldest` vs `DropNewest` vs `DropWrite`：选择 `DropOldest` 因为旧数据价值更低，符合 SC5 要求

### 反模式避免

- **反模式：在 IPluginIpcGateway 上添加 Provider 方法** — 会破坏 Phase 12/14 使用的稳定接口，带来回归风险。使用独立服务。
- **反模式：手动管理线程安全** — 不要使用 `lock` + `Queue<T>`。`Channel<T>` 和 `ConcurrentDictionary` 已内置线程安全。
- **反模式：在非 Framework 线程注册 IPC Provider** — 在 `PluginDataRelayService` 初始化时注册，或在 Framework 线程上延迟注册。
- **反模式：AI 直接管理 IPC Provider** — AI 不知道 Dalamud IPC 细节。AI 通过简单的 MCP 工具（subscribe/unsubscribe/poll）操作，服务层处理所有 IPC 细节。

---

## 不要自己造轮子

| 问题 | 不要自己构建 | 使用现有方案 | 原因 |
|------|------------|------------|------|
| 线程安全的有界队列 | 手动 `lock` + `Queue<T>` + 容量检查 | `System.Threading.Channels.Channel.CreateBounded<T>(options)` | Channel 内置无锁算法、边界检查、溢出策略，性能远超手写 |
| 并发字典 | 手动 `Dictionary` + `lock` | `ConcurrentDictionary<string, RelayChannel>` | .NET 内置无锁并发字典，无需手动同步 |
| IPC 提供者注册 | 自定义反射/RPC 层 | `IDalamudPluginInterface.GetIpcProvider<string, object>()` | Dalamud 原生 IPC 机制，目标插件零依赖 |
| 插件安装检测 | 自定义扫描/缓存方案 | `IDalamudPluginInterface.InstalledPlugins` 属性 | Dalamud 原生维护的已安装插件列表 |
| 操作类 MCP 注册 | 手动实现 `IOperation` | `[Operation]` + `[McpTool]` 属性 + 源代码生成器 | 项目现有的已测试模式，自动生成注册代码 |

**关键洞察：** 数据回传的核心复杂度在于 IPC 提供者生命周期的管理（注册→接收数据→注销）和并发安全。`System.Threading.Channels` + `ConcurrentDictionary` 组合解决了 90% 的基础设施问题。其余复杂度仅在于将已有模式正确应用到新操作类上。

---

## 详细设计

### 1. 数据回传机制（IPC Push → Channel Cache → MCP Poll）

**完整数据流：**

```
Phase 1: Subscribe（MCP 初始化）
  AI: plugin_data_subscribe(plugin="MyPlugin", channel="status")
  DalamudMCP:
    1. 构造完整通道名: "MyPlugin.status"
    2. 构造 IPC CallGate: "DalamudMCP.Relay.MyPlugin.status"
    3. 创建 Channel<string>(capacity=1000, DropOldest)
    4. 调用 pluginInterface.GetIpcProvider<string, object>(callGate).RegisterAction(handler)
    5. 存入 _channels["MyPlugin.status"] = new RelayChannel(channel, provider, "MyPlugin")
    6. 返回 subscribe_success

Phase 2: Push（目标插件推送数据）
  目标插件:
    var sub = pluginInterface.GetIpcSubscriber<string, object>("DalamudMCP.Relay.MyPlugin.status");
    sub.InvokeAction("{\"hp\": 100, \"mp\": 50}");
  DalamudMCP IPC Action 回调:
    channel.Writer.TryWrite("{\"hp\": 100, \"mp\": 50}");  // 写入 Channel

Phase 3: Poll（AI 轮询）
  AI: plugin_data_poll(channel="MyPlugin.status")
  DalamudMCP:
    1. 从 _channels 查找 "MyPlugin.status"
    2. channel.Reader.TryRead 循环读取所有可用数据
    3. 返回 data_available + items[]

Phase 4: Unsubscribe（AI 退订）
  AI: plugin_data_unsubscribe(channel="MyPlugin.status")
  DalamudMCP:
    1. 从 _channels 移除 "MyPlugin.status"
    2. 调用 provider.Dispose() 注销 IPC 端点
    3. channel.Writer.Complete() 关闭 Channel
    4. 返回 unsubscribe_success

Phase 5: Auto-cleanup（插件卸载检测）
  IFramework.Update 事件:
    遍历所有活跃通道 → 提取 PluginName → 检查 InstalledPlugins
    → 插件不存在 → 自动执行 unsubscribe 清理
```

### 2. IPluginIpcGateway 集成

**关键区别：** Phase 15 不使用 `IPluginIpcGateway`（该接口用于创建 Subscriber，方向为 DalamudMCP → 目标插件）。Phase 15 需要 Provider 方向（目标插件 → DalamudMCP）。

**新服务接口：**

```csharp
// [VERIFIED: 接口设计遵循 Phase 11 提取的 IPluginIpcGateway 模式]
// [VERIFIED: src/DalamudMCP.Plugin/Ipc/IPluginIpcGateway.cs:6-16 — 接口命名和 null 返回约定]
namespace DalamudMCP.Plugin.Relay;

public interface IPluginDataRelayService
{
    /// <summary>
    /// 订阅数据回传通道——注册 IPC Provider 并创建有界 Channel。
    /// </summary>
    /// <param name="pluginName">目标插件内部名称</param>
    /// <param name="channelName">通道名（不含插件名前缀）</param>
    /// <param name="capacity">Channel 容量，默认 1000</param>
    /// <returns>订阅成功返回 true；通道已存在返回 false</returns>
    bool Subscribe(string pluginName, string channelName, int capacity = 1000);

    /// <summary>
    /// 退订数据回传通道——注销 IPC Provider 并关闭 Channel。
    /// </summary>
    /// <param name="fullChannelName">完整通道名（{PluginName}.{Channel}）</param>
    /// <returns>退订成功返回 true；通道不存在返回 false</returns>
    bool Unsubscribe(string fullChannelName);

    /// <summary>
    /// 轮询通道中的所有可用数据（非阻塞）。
    /// </summary>
    /// <param name="fullChannelName">完整通道名</param>
    /// <param name="data">输出参数：所有可用数据项</param>
    /// <returns>通道存在返回 true；不存在返回 false</returns>
    bool TryPoll(string fullChannelName, out IReadOnlyList<string> data);

    /// <summary>
    /// 检查通道是否已订阅。
    /// </summary>
    bool IsSubscribed(string fullChannelName);

    /// <summary>
    /// 获取所有活跃通道名。
    /// </summary>
    IReadOnlyCollection<string> ActiveChannels { get; }
}
```

**服务实现关键点：**
- 构造注入 `IDalamudPluginInterface`（用于注册 IPC Provider）和 `IFramework`（用于自动清理检测）
- 实现 `IDisposable`——在 Plugin Dispose 时清理所有活跃 Provider
- 自动清理使用 `IFramework.Update` 每 60 帧检测一次插件安装状态 [ASSUMED: 基于 Dalamud 帧率约 60fps，即每秒检测一次]

### 3. 有界 Channel 设计

| 参数 | 值 | 理由 |
|------|-----|------|
| 数据类型 | `string`（JSON）| 目标插件零序列化依赖，与项目现有 JSON 模式一致 |
| 容量 | 默认 1000，可配置 | 1000 条目约 1MB 内存在 JSON 场景下安全；支持高频推送 |
| 溢出策略 | `BoundedChannelFullMode.DropOldest` | SC5 明确要求丢弃旧数据；保证内存受限 |
| 写入方式 | `channel.Writer.TryWrite()` | 非阻塞，永不抛异常（配合 DropOldest） |
| 读取方式 | `channel.Reader.TryRead()` 循环 | 非阻塞批量读取，适合 MCP poll 语义 |

**溢出行为验证：** [ASSUMED: 基于 System.Threading.Channels 官方行为] 当 Channel 满时，`DropOldest` 自动移除最旧条目再写入新条目。`TryWrite` 在 `DropOldest` 模式下始终返回 `true`。

### 4. 订阅生命周期管理

```
状态机：
  [不存在] —subscribe→ [活跃] —unsubscribe→ [已清理]
                           ↑                   │
                           └─ auto-detect ─────┘ (插件卸载)

自动清理策略（SC4 实现）：
  触发条件：IFramework.Update 每 60 帧触发检测
  检测逻辑：
    foreach channel in _channels:
      pluginName = channel.FullName.Split('.')[0]   // 提取插件名
      if pluginInterface.InstalledPlugins 中不存在 pluginName:
        Unsubscribe(channel.FullName)               // 自动清理

备选方案：在 Poll 时检查（更轻量，但不保证及时清理）
  推荐：使用 IFramework.Update 方案，确保 SC4 的"不产生僵尸订阅"要求
```

### 5. 响应/Result 模型设计

遵循 Phase 12/13/14 建立的模式 [VERIFIED: src/DalamudMCP.Plugin/Operations/SafeInvokePluginIpcOperation.cs:231-239]：

```csharp
[MemoryPackable]
public sealed partial record PluginDataSubscribeResult(
    string FullChannelName,    // "MyPlugin.status"
    string PluginName,         // "MyPlugin"
    bool Success,              
    string Status,             // subscribe_success / already_subscribed / subscribe_failed
    string? ErrorMessage,
    string SummaryText);

[MemoryPackable]
public sealed partial record PluginDataUnsubscribeResult(
    string FullChannelName,
    bool Success,
    string Status,             // unsubscribe_success / not_subscribed
    string? ErrorMessage,
    string SummaryText);

[MemoryPackable]
public sealed partial record PluginDataPollResult(
    string FullChannelName,
    bool Success,
    string Status,             // data_available / no_data / channel_not_found
    int ItemCount,
    string[] Items,            // JSON 字符串数组
    string? ErrorMessage,
    string SummaryText);
```

**状态码矩阵：**

| 操作 | 状态码 | 条件 |
|------|-------|------|
| subscribe | `subscribe_success` | IPC Provider 注册成功，Channel 创建成功 |
| subscribe | `already_subscribed` | 通道名已存在，返回现有状态（幂等） |
| subscribe | `subscribe_failed` | IPC Provider 注册失败或其他异常 |
| unsubscribe | `unsubscribe_success` | Provider 注销 + Channel 关闭 + 注册表移除 |
| unsubscribe | `not_subscribed` | 通道名不存在 |
| poll | `data_available` | 成功读取 N 条数据 |
| poll | `no_data` | 通道存在但无数据 |
| poll | `channel_not_found` | 通道不存在（未订阅或已退订） |

### 6. 操作类设计（3 个新 MCP 工具）

| 操作 ID | MCP 工具名 | 操作类 | 请求参数 |
|---------|-----------|-------|---------|
| `plugin.data.subscribe` | `plugin_data_subscribe` | `PluginDataSubscribeOperation` | `plugin-name` (string), `channel` (string) |
| `plugin.data.unsubscribe` | `plugin_data_unsubscribe` | `PluginDataUnsubscribeOperation` | `channel` (string, 完整通道名) |
| `plugin.data.poll` | `plugin_data_poll` | `PluginDataPollOperation` | `channel` (string, 完整通道名), `max-items` (int?, optional) |

**依赖注入（所有三个操作类共用）：**
- `IPluginDataRelayService` — 通道管理
- `IFramework` — 线程编排（确保 IPC 操作在 Framework 线程执行）
- `IDalamudPluginInterface` — 仅 Poll 操作可能需要，用于自动清理检测

**MCP 工具描述：**

```
plugin_data_subscribe:
  Description: "订阅目标插件的数据回传通道。注册 IPC 端点使目标插件可通过 Dalamud IPC 向 DalamudMCP 推送数据。通道命名约定：IPC CallGate = DalamudMCP.Relay.{plugin-name}.{channel}。目标插件使用 GetIpcSubscriber<string,object>(callGate).InvokeAction(jsonData) 推送 JSON 字符串数据。成功订阅后，使用 plugin_data_poll 轮询获取数据。"
  Summary: "Subscribes to a plugin data relay channel."

plugin_data_unsubscribe:
  Description: "退订数据回传通道。注销 IPC 端点，关闭有界缓冲区，释放相关资源。退订后目标插件无法再向该通道推送数据。"
  Summary: "Unsubscribes from a plugin data relay channel."

plugin_data_poll:
  Description: "轮询指定数据回传通道中的已缓存数据。非阻塞读取：返回通道中当前所有可用数据。max-items 参数可限制返回条目数（默认所有可用）。目标插件卸载时，对应通道自动退订。"
  Summary: "Polls cached data from a plugin data relay channel."
```

### 7. 文件结构清单

**新文件（9 个）：**

| # | 文件路径 | 类型 | 说明 |
|---|---------|------|------|
| 1 | `src/DalamudMCP.Plugin/Relay/IPluginDataRelayService.cs` | 接口 | 公开服务接口 |
| 2 | `src/DalamudMCP.Plugin/Relay/PluginDataRelayService.cs` | 实现 | 管理 Channel + IPC Provider + 自动清理 |
| 3 | `src/DalamudMCP.Plugin/Relay/RelayChannel.cs` | record | 内部记录类型：封装 Channel + Provider + 元数据 |
| 4 | `src/DalamudMCP.Plugin/Operations/PluginDataSubscribeOperation.cs` | 操作类 | MCP: plugin_data_subscribe |
| 5 | `src/DalamudMCP.Plugin/Operations/PluginDataUnsubscribeOperation.cs` | 操作类 | MCP: plugin_data_unsubscribe |
| 6 | `src/DalamudMCP.Plugin/Operations/PluginDataPollOperation.cs` | 操作类 | MCP: plugin_data_poll |
| 7 | `tests/DalamudMCP.Plugin.Operations.Tests/TestShared/Relay/FakePluginDataRelayService.cs` | 测试桩 | 可控的 IPluginDataRelayService 实现 |
| 8 | `tests/DalamudMCP.Plugin.Operations.Tests/PluginDataSubscribeOperationTests.cs` | 测试 | 订阅操作测试 |
| 9 | `tests/DalamudMCP.Plugin.Operations.Tests/PluginDataUnsubscribeOperationTests.cs` | 测试 | 退订操作测试 |
| 10 | `tests/DalamudMCP.Plugin.Operations.Tests/PluginDataPollOperationTests.cs` | 测试 | 轮询操作测试 |

**修改文件（2 个）：**

| # | 文件路径 | 修改内容 |
|---|---------|---------|
| 1 | `src/DalamudMCP.Plugin/Hosting/PluginServiceCollectionExtensions.cs` | 添加 `services.AddSingleton<IPluginDataRelayService, PluginDataRelayService>()` |
| 2 | `src/DalamudMCP.Plugin/Hosting/PluginOperationExposurePolicy.cs` | 将 3 个新操作 ID 加入 `UnsafeOperationIds`：`"plugin.data.subscribe"`, `"plugin.data.unsubscribe"`, `"plugin.data.poll"` |

**验证：** [VERIFIED: src/DalamudMCP.Plugin/Hosting/PluginOperationExposurePolicy.cs:21-27 — UnsafeOperationIds 集合模式]
**验证：** [VERIFIED: src/DalamudMCP.Plugin/Hosting/PluginServiceCollectionExtensions.cs:51 — IPluginIpcGateway 的单例注册模式]

---

## 测试策略

### 测试框架
| 属性 | 值 |
|------|-----|
| 框架 | xunit.v3.mtp-v2 3.2.2 + NSubstitute 5.3.0 |
| 配置文件 | `tests/DalamudMCP.Plugin.Operations.Tests/DalamudMCP.Plugin.Operations.Tests.csproj` |
| 快速运行命令 | `dotnet test tests/DalamudMCP.Plugin.Operations.Tests --filter "FullyQualifiedName~PluginData"` |
| 完整套件命令 | `./build/test.ps1` |

[VERIFIED: tests/DalamudMCP.Plugin.Operations.Tests/DalamudMCP.Plugin.Operations.Tests.csproj:17-18]

### 测试用例矩阵

**PluginDataSubscribeOperationTests（8 个测试）：**

| # | 测试 | 类型 | 覆盖状态 |
|---|------|------|---------|
| 1 | `ExecuteAsync_ReturnsSubscribeSuccess_WhenValidChannel` | 单元 | subscribe_success |
| 2 | `ExecuteAsync_ReturnsAlreadySubscribed_WhenChannelExists` | 单元 | already_subscribed（幂等） |
| 3 | `ExecuteAsync_ReturnsSubscribeFailed_WhenIpcProviderFails` | 单元 | subscribe_failed |
| 4 | `Constructor_RejectsNullRelayService` | 单元 | 构造验证 |
| 5 | `Constructor_RejectsNullFramework` | 单元 | 构造验证 |
| 6 | `ExecuteAsync_ThrowsArgumentException_WhenPluginNameEmpty` | 单元 | 输入验证 |
| 7 | `ExecuteAsync_ThrowsArgumentException_WhenChannelEmpty` | 单元 | 输入验证 |
| 8 | `ExecuteAsync_DefaultCapacityIs1000` | 单元 | 默认参数 |

**PluginDataUnsubscribeOperationTests（5 个测试）：**

| # | 测试 | 类型 | 覆盖状态 |
|---|------|------|---------|
| 1 | `ExecuteAsync_ReturnsUnsubscribeSuccess_WhenChannelExists` | 单元 | unsubscribe_success |
| 2 | `ExecuteAsync_ReturnsNotSubscribed_WhenChannelNotFound` | 单元 | not_subscribed |
| 3 | `Constructor_RejectsNullRelayService` | 单元 | 构造验证 |
| 4 | `ExecuteAsync_ThrowsArgumentException_WhenChannelEmpty` | 单元 | 输入验证 |
| 5 | `ExecuteAsync_UnsubscribeDisposesIpcProvider` | 单元 | 资源清理 |

**PluginDataPollOperationTests（8 个测试）：**

| # | 测试 | 类型 | 覆盖状态 |
|---|------|------|---------|
| 1 | `ExecuteAsync_ReturnsDataAvailable_WhenChannelHasItems` | 单元 | data_available |
| 2 | `ExecuteAsync_ReturnsNoData_WhenChannelIsEmpty` | 单元 | no_data |
| 3 | `ExecuteAsync_ReturnsChannelNotFound_WhenNotSubscribed` | 单元 | channel_not_found |
| 4 | `ExecuteAsync_RespectsMaxItemsParameter` | 单元 | max_items 限制 |
| 5 | `ExecuteAsync_ReturnsAllItems_WhenMaxItemsExceedsAvailable` | 单元 | max_items > 可用 |
| 6 | `Constructor_RejectsNullRelayService` | 单元 | 构造验证 |
| 7 | `Constructor_RejectsNullFramework` | 单元 | 构造验证 |
| 8 | `ExecuteAsync_ThrowsArgumentException_WhenChannelEmpty` | 单元 | 输入验证 |

**PluginDataRelayService 集成测试（5 个测试——可选新增测试文件）：**

| # | 测试 | 类型 | 覆盖状态 |
|---|------|------|---------|
| 1 | `Subscribe_CreatesChannelAndProvider` | 集成 | Subscribe 核心 |
| 2 | `Unsubscribe_DisposesAndRemoves` | 集成 | Unsubscribe 核心 |
| 3 | `Poll_ReadsAllAvailableItems` | 集成 | 完整数据流 |
| 4 | `Channel_DropsOldestOnOverflow` | 集成 | 有界策略 |
| 5 | `AutoCleanup_DetectsUnloadedPlugin` | 集成 | 自动清理 |

**总计：~26 个测试**

### FakePluginDataRelayService 测试桩

```csharp
// 遵循 FakeIpcGateway 模式
// [VERIFIED: tests/DalamudMCP.Plugin.Operations.Tests/TestShared/Ipc/FakeIpcGateway.cs:8-25]
public sealed class FakePluginDataRelayService : IPluginDataRelayService
{
    private readonly ConcurrentDictionary<string, (Channel<string> Channel, bool Subscribed)> _channels = new();

    public bool Subscribe(string pluginName, string channelName, int capacity = 1000)
    {
        var fullName = $"{pluginName}.{channelName}";
        if (_channels.ContainsKey(fullName)) return false;
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
            { FullMode = BoundedChannelFullMode.DropOldest });
        _channels[fullName] = (channel, true);
        return true;
    }

    public bool Unsubscribe(string fullChannelName)
    {
        if (!_channels.TryRemove(fullChannelName, out var entry)) return false;
        entry.Channel.Writer.Complete();
        return true;
    }

    public bool TryPoll(string fullChannelName, out IReadOnlyList<string> data) { ... }
    public bool IsSubscribed(string fullChannelName) => _channels.ContainsKey(fullChannelName);
    public IReadOnlyCollection<string> ActiveChannels => _channels.Keys.ToList();
}
```

---

## 常见陷阱

### 陷阱 1：IPC Provider 和 Subscriber 方向混淆

**出错表现：** 尝试使用 `IPluginIpcGateway.TryCreate()` 创建 Provider——该方法创建的是 Subscriber。
**根本原因：** `IPluginIpcGateway` 为 Phase 14（DALAMUDMCP 调用目标插件）设计，但 Phase 15 需要反向（目标插件调用 DALAMUDMCP）。
**如何避免：** 使用独立服务 `PluginDataRelayService` 管理 Provider 方向。通过 `IDalamudPluginInterface.GetIpcProvider<string, object>()` 注册 IPC 端点。
**预警信号：** 代码中出现 `gateway.TryCreate` + `Subscribe` 字样的组合——这是方向错误。

### 陷阱 2：Channel 写入时的线程假设

**出错表现：** 锁定 Channel 写操作或假设写入顺序。
**根本原因：** IPC Provider 的 Action 回调可能在不同线程调用（取决于 Dalamud IPC 实现）。
**如何避免：** `Channel<string>.Writer.TryWrite()` 是线程安全的，无需额外同步。不要在 `TryWrite` 周围添加锁。
**预警信号：** `lock (_channelLock) { channel.Writer.TryWrite(...) }` ——这是冗余的。

### 陷阱 3：内存泄漏——未注销的 IPC Provider

**出错表现：** Unsubscribe 时忘记 Dispose Provider，导致 Dalamud IPC 端点持续占用。
**根本原因：** `ICallGateProvider` 是 `IDisposable`，不 Dispose 会在 Dalamud 内部保持引用。
**如何避免：** `PluginDataRelayService.Unsubscribe()` 中始终调用 `provider.Dispose()`。在服务类自身 `Dispose()` 中实现批量清理。使用 `using` 或 try-finally 确保异常路径也清理。
**预警信号：** 测试中发现 Unsubscribe 后 `.GetIpcSubscriber()` 仍能成功——说明 Provider 未清理。

### 陷阱 4：自动清理的性能开销

**出错表现：** 每帧都调用 `InstalledPlugins` 遍历导致性能下降。
**根本原因：** `InstalledPlugins` 是 `IEnumerable<IExposedPlugin>`，频繁遍历有开销。
**如何避免：** 使用帧计数节流（每 60 帧检测一次 ≈ 每秒一次），或缓存已安装插件列表并在检测到变化时更新。只有当活跃通道列表非空时才执行检测。
**预警信号：** 配置文件/日志显示 `IFramework.Update` 处理时间异常增长。

### 陷阱 5：订阅操作的幂等性处理

**出错表现：** 同一通道重复订阅时抛出异常或创建重复 Provider。
**根本原因：** 未检查通道是否已存在。
**如何避免：** `Subscribe()` 中先检查 `_channels.ContainsKey(fullName)`，如存在则返回 `already_subscribed` 而不是覆盖。MCP 响应应包含明确的 `already_subscribed` 状态码告知 AI。
**预警信号：** 重复 Subscribe 调用返回不同结果——说明未正确处理幂等性。

---

## 代码参考

验证过的 Phase 14 模式示例：

### 操作类结构模式
```csharp
// 来源: SafeInvokePluginIpcOperation.cs — 完整的操作类样板
// [VERIFIED: src/DalamudMCP.Plugin/Operations/SafeInvokePluginIpcOperation.cs:12-48]

[Operation("plugin.ipc", Description = "...", Summary = "...")]
[ResultFormatter(typeof(SafeInvokePluginIpcOperation.TextFormatter))]
[CliCommand("plugin", "ipc")]
[McpTool("invoke_plugin_ipc")]
public sealed partial class SafeInvokePluginIpcOperation
    : IOperation<SafeInvokePluginIpcOperation.Request, SafeInvokePluginIpcResult>
{
    private readonly Func<Request, CancellationToken, ValueTask<SafeInvokePluginIpcResult>> executor;

    [SupportedOSPlatform("windows")]
    public SafeInvokePluginIpcOperation(IPluginIpcGateway gateway, IFramework framework) { ... }
    internal SafeInvokePluginIpcOperation(Func<...> executor) { ... }
    public ValueTask<SafeInvokePluginIpcResult> ExecuteAsync(Request request, OperationContext context) { ... }
    // ... nested Request class, TextFormatter, result record
}
```

### DI 注册模式
```csharp
// 来源: PluginServiceCollectionExtensions.cs
// [VERIFIED: src/DalamudMCP.Plugin/Hosting/PluginServiceCollectionExtensions.cs:51]
services.AddSingleton<IPluginIpcGateway, PluginIpcGateway>();
// Phase 15 新增:
services.AddSingleton<IPluginDataRelayService, PluginDataRelayService>();
```

### 暴露策略注册模式
```csharp
// 来源: PluginOperationExposurePolicy.cs
// [VERIFIED: src/DalamudMCP.Plugin/Hosting/PluginOperationExposurePolicy.cs:21-27]
private static readonly HashSet<string> UnsafeOperationIds =
[
    "unsafe.invoke.plugin-ipc",
    "plugin.reload",
    "command.slash",
    "plugin.ipc"
    // Phase 15 新增:
    "plugin.data.subscribe",
    "plugin.data.unsubscribe",
    "plugin.data.poll"
];
```

### 测试构造模式
```csharp
// 来源: SafeInvokePluginIpcOperationTests.cs
// [VERIFIED: tests/DalamudMCP.Plugin.Operations.Tests/SafeInvokePluginIpcOperationTests.cs:24-33]
private static SafeInvokePluginIpcOperation CreateOperation(
    Func<SafeInvokePluginIpcOperation.Request, CancellationToken, ValueTask<SafeInvokePluginIpcResult>> executor)
{
    return new SafeInvokePluginIpcOperation(executor);
}
```

---

## 当前技术状态

| 旧方式 | 当前方式 | 变更时间 | 影响 |
|--------|---------|---------|------|
| 无数据回传机制 | `System.Threading.Channels` 有界队列 + Dalamud IPC Provider | Phase 15 | 首次引入目标插件→DalamudMCP 的数据推送通道 |
| N/A（新功能） | `PluginDataRelayService` 单例管理 Provider 生命周期 | Phase 15 | 新模式：与 `IPluginIpcGateway` 对称的 Provider 管理服务 |
| 仅单向 IPC 调用 | 双向：Subscriber（Phase 14）+ Provider（Phase 15）| Phase 15 | 补全 IPC 功能矩阵 |

**已废弃/过时：**
- 无。此阶段是全新功能，不涉及废弃任何现有 API。

---

## 假设日志

| # | 假设声明 | 所在章节 | 假设错误的风险 |
|---|---------|---------|-------------|
| A1 | `IDalamudPluginInterface.GetIpcProvider<T1, TRet>()` API 存在于 Dalamud SDK 15.0.0 | §2（标准技术栈）、§6（操作设计） | HIGH — 如果 API 签名或行为与假设不同，IPC Provider 注册逻辑需重写。验证：在 Dalamud 环境中测试 `GetIpcProvider` 调用。 |
| A2 | `ICallGateProvider<T, TRet>.RegisterAction(Action<T>)` 是 Provider 注册 Action 的标准方式 | §2、§3（IPC Provider 模式） | MEDIUM — 如果 RegisterAction 不可用，需改用 RegisterFunc 并返回 dummy 值。 |
| A3 | `ICallGateProvider` 实现 `IDisposable`，调用 `Dispose()` 即可注销 IPC 端点 | §3、§4（生命周期） | MEDIUM — 如果 Provider 不使用 Dispose 模式，需查找替代注销 API（如 `UnregisterAction()`）。 |
| A4 | `IFramework.Update` 事件可用于周期性检测（~60fps） | §4（自动清理） | LOW — 项目已在 Phase 14 中使用 `IFramework.IsInFrameworkUpdateThread` 和 `RunOnFrameworkThread`，`Update` 事件的可用性已被验证。 |
| A5 | 目标插件使用 `GetIpcSubscriber<string, object>(callGate).InvokeAction(jsonData)` 推送数据 | §6（MCP 工具描述） | MEDIUM — 这是 Dalamud IPC 的标准用法，但需在实际集成测试中验证。 |
| A6 | `System.Threading.Channels.BoundedChannelFullMode.DropOldest` 在 Channel 满时静默丢弃最旧条目 | §3（Channel 设计） | LOW — 这是 .NET BCL 的标准行为，但应在单元测试中显式验证。 |

---

## 待解决问题

1. **IPC Provider 线程安全性**
   - 已知：`Channel<T>` 是线程安全的
   - 不明确：Dalamud 的 `RegisterAction` 回调在哪个线程执行
   - 建议：在实现中不假设线程上下文，依赖 Channel 的线程安全性

2. **自动清理的频率与性能**
   - 已知：`IFramework.Update` 每帧触发
   - 不明确：`InstalledPlugins` 遍历的性能开销
   - 建议：使用帧计数节流（每 60 帧），仅在活跃通道数 > 0 时执行

3. **Channel 容量的最佳默认值**
   - 已知：1000 为合理中间值
   - 不明确：实际使用场景中的高频推送速率
   - 建议：默认 1000，可在 `PluginRuntimeOptions` 中添加配置项

4. **目标插件如何知道通道名称**
   - 已知：AI 通过 MCP 工具管理订阅
   - 不明确：目标插件如何获知已创建的回传通道
   - 建议：MCP 工具描述中明确说明 CallGate 命名约定，AI 负责在提示词中告知目标插件要使用的 CallGate 名称

---

## 安全领域

> `security_enforcement` 默认启用

### 适用 ASVS 类别

| ASVS 类别 | 适用 | 标准控制 |
|-----------|------|---------|
| V2 身份验证 | 否 | N/A — MCP 通信由 NamedPipe 本地认证保护 |
| V3 会话管理 | 否 | N/A — 无会话概念 |
| V4 访问控制 | 是 | 操作归入 `UnsafeOperationIds`，仅在启用 unsafe 操作时暴露 |
| V5 输入验证 | 是 | 通道名验证（非空、长度限制、无路径遍历字符）；JSON 数据格式验证 |
| V6 密码学 | 否 | N/A — 不涉及密钥/加密 |

### 针对本技术栈的已知威胁模式

| 威胁模式 | STRIDE 类别 | 标准缓解 |
|---------|------------|---------|
| 恶意插件通过 IPC 注入超大 JSON 数据导致内存溢出 | 拒绝服务 (D) | 有界 Channel + DropOldest 策略限制内存占用；`max-items` 限制 Poll 返回量 |
| 通道名注入（如 `../` 路径遍历）| 篡改 (T) | 通道名验证：仅允许 `[a-zA-Z0-9._-]` 字符，拒绝路径分隔符 |
| 僵尸 Provider 积累导致 IPC 端点泄漏 | 信息泄露 (I) | 自动清理机制（插件卸载检测）+ Dispose 所有 Provider |
| 未授权插件向 relay 通道推送数据 | 篡改 (T) | MCP 工具仅在 `enableUnsafeOperations=true` 时暴露；通道订阅由 AI 主动管理 |

### MCP 工具的 OWASP 排名风险

| 工具 | 风险 | 缓解 |
|------|------|------|
| `plugin_data_subscribe` | 中 — 启用 IPC 端点，潜在攻击面 | 归入 `unsafe` 操作，需显式启用 |
| `plugin_data_unsubscribe` | 低 — 仅清理资源 | 无特殊风险 |
| `plugin_data_poll` | 低 — 只读操作 | `max-items` 参数上限（如 10000）防止过大响应 |

---

## 验证架构

> `workflow.nyquist_validation: true` [VERIFIED: .planning/config.json:14]

### 测试框架
| 属性 | 值 |
|------|-----|
| 框架 | xunit.v3.mtp-v2 3.2.2 |
| 配置文件 | `tests/DalamudMCP.Plugin.Operations.Tests/DalamudMCP.Plugin.Operations.Tests.csproj` |
| 快速运行命令 | `dotnet test tests/DalamudMCP.Plugin.Operations.Tests --filter "FullyQualifiedName~PluginData"` |
| 完整套件命令 | `./build/test.ps1` |

### 阶段需求 → 测试映射

| 需求 ID | 行为 | 测试类型 | 自动化命令 | 文件存在？ |
|---------|------|---------|-----------|----------|
| RELAY-01 | 订阅通道：IPC Provider 注册 + Channel 创建 | 单元 | `dotnet test ... --filter "PluginDataSubscribe"` | ❌ Wave 0 |
| RELAY-01 | 退订通道：Provider 注销 + Channel 关闭 | 单元 | `dotnet test ... --filter "PluginDataUnsubscribe"` | ❌ Wave 0 |
| RELAY-01 | 轮询数据：非阻塞读取所有缓存条目 | 单元 | `dotnet test ... --filter "PluginDataPoll"` | ❌ Wave 0 |
| RELAY-01 | 数据推送：IPC Action 写入 Channel | 集成 | `dotnet test ... --filter "PluginDataRelayService"` | ❌ Wave 0 |
| RELAY-01 | 溢出丢弃：Channel 满时丢弃最旧数据 | 集成 | `dotnet test ... --filter "DropOldest"` | ❌ Wave 0 |
| RELAY-01 | 自动清理：插件卸载检测 + 自动退订 | 集成 | `dotnet test ... --filter "AutoCleanup"` | ❌ Wave 0 |

### 采样率
- **每个任务提交：** `dotnet test tests/DalamudMCP.Plugin.Operations.Tests --filter "PluginData" --no-restore`
- **每个 Wave 合并：** `./build/test.ps1`
- **阶段关卡：** 完整套件通过后方可执行 `/gsd-verify-work`

### Wave 0 差距
- [ ] `tests/.../PluginDataSubscribeOperationTests.cs` — 覆盖 RELAY-01 订阅路径
- [ ] `tests/.../PluginDataUnsubscribeOperationTests.cs` — 覆盖 RELAY-01 退订路径
- [ ] `tests/.../PluginDataPollOperationTests.cs` — 覆盖 RELAY-01 轮询路径
- [ ] `tests/.../TestShared/Relay/FakePluginDataRelayService.cs` — 共享测试桩
- [ ] `tests/.../PluginDataRelayServiceTests.cs` — 覆盖服务集成场景（可选，可在 Wave 1）

---

## 来源

### 主要来源（HIGH 置信度）
- [VERIFIED: src/DalamudMCP.Plugin/Ipc/IPluginIpcGateway.cs:1-16] — 现有 IPC 网关接口（Subscriber 方向）
- [VERIFIED: src/DalamudMCP.Plugin/Ipc/PluginIpcGateway.cs:18-44] — 网关实现：通过反射创建 Subscriber
- [VERIFIED: src/DalamudMCP.Plugin/Ipc/ReflectionPluginCallGateSubscriber.cs:5-28] — 反射式 IPC Subscriber 封装
- [VERIFIED: src/DalamudMCP.Plugin/Operations/SafeInvokePluginIpcOperation.cs:12-63] — 完整操作类模式样板
- [VERIFIED: src/DalamudMCP.Plugin/Operations/SlashCommandOperation.cs:9-21] — 第二个操作类模式验证
- [VERIFIED: src/DalamudMCP.Plugin/Operations/PluginReloadOperation.cs:10-17] — 第三个操作类模式验证
- [VERIFIED: src/DalamudMCP.Plugin/Hosting/PluginOperationExposurePolicy.cs:21-27] — UnsafeOperationIds 注册模式
- [VERIFIED: src/DalamudMCP.Plugin/Hosting/PluginServiceCollectionExtensions.cs:48-68] — DI 注册模式
- [VERIFIED: src/DalamudMCP.Plugin/Hosting/PluginGeneratedOperationRegistration.cs:10-28] — 操作类自动注册机制
- [VERIFIED: tests/DalamudMCP.Plugin.Operations.Tests/TestShared/Ipc/FakeIpcGateway.cs:8-25] — 测试桩模式
- [VERIFIED: tests/DalamudMCP.Plugin.Operations.Tests/TestShared/Ipc/FakeIpcCallGateSubscriber.cs:8-25] — Subscriber 测试桩
- [VERIFIED: tests/DalamudMCP.Plugin.Operations.Tests/SafeInvokePluginIpcOperationTests.cs:37-57] — 操作类测试模式
- [VERIFIED: tests/DalamudMCP.Plugin.Operations.Tests/DalamudMCP.Plugin.Operations.Tests.csproj:17-18] — 测试框架版本
- [VERIFIED: .planning/config.json:14] — Nyquist 验证已启用
- [VERIFIED: .planning/ROADMAP.md:100-110] — Phase 15 成功标准
- [VERIFIED: .planning/REQUIREMENTS.md:18] — RELAY-01 需求定义

### 次要来源（MEDIUM 置信度）
- [CITED: learn.microsoft.com System.Threading.Channels] — Channel API 文档
- [CITED: Dalamud Plugin API 15] — `IDalamudPluginInterface` 公开 API

### 三级来源（LOW 置信度）
- [ASSUMED: A1-A6 in Assumptions Log] — 标记需要验证的 Dalamud IPC API 细节

### 已审阅的 Phase 14 研究参考
- [VERIFIED: .planning/phases/14-safe-ipc-invoke/14-RESEARCH.md:1-80] — Phase 14 研究文档结构和模式

---

## 元数据

**置信度分解：**
- 标准技术栈：HIGH — `System.Threading.Channels` 是 .NET BCL；`IDalamudPluginInterface` 已在 Phase 11/14 验证；MemoryPack 已在项目中使用
- 架构：HIGH — 操作类模式在 Phase 12/13/14 中 3 次独立验证；IPC Provider 模式基于 Dalamud 标准 API；DI 注册模式与现有完全一致
- 陷阱：MEDIUM — 基于一般开发经验识别，但 Provider 方向是该项目的首次使用，可能存在 Dalamud 特定陷阱
- Provider 管理服务：MEDIUM — 服务接口设计基于 Phase 11 的模式，但 Provider 管理是全新概念

**研究日期：** 2026-05-01
**有效期至：** 2026-06-01（30 天——稳定领域）
