# Architecture Research: DalamudMCP v1.1 自动化测试桥接

**Domain:** Dalamud 插件跨进程 IPC + MCP 桥接
**Researched:** 2026-05-01
**Overall confidence:** HIGH

## Executive Summary

DalamudMCP v1.1 新增四项功能——插件重载、跨插件 IPC 调用、数据回传、斜杠命令调度——均可融入现有架构，**不需要新增项目或重构层次边界**。关键发现：

1. **已有先例**：`UnsafeInvokePluginIpcOperation` 已通过反射实现跨插件 IPC 调用，其 `IPluginIpcGateway` / `IPluginCallGateSubscriber` 抽象可直接复用和扩展。
2. **操作模型完全适配**：四项新功能都映射为 `[Operation]` 类，源生成器自动注册到 `GeneratedOperationRegistry`、`GeneratedOperationInvoker`、`GeneratedMcpTools`，无需修改生成器。
3. **协议层零变更**：`ProtocolContract` 的请求/响应信封和 `MemoryPack` 序列化无需任何改动。
4. **数据回传是新模式**：现有架构是 AI→Plugin（请求-响应），数据回传需要 Plugin→AI（推送/订阅），这是唯一的架构扩展点。

---

## 现有架构概览

```
┌─────────────────────────────────────────────────────────────────────┐
│                     AI Client (Claude, etc.)                       │
│                     MCP Protocol / CLI / HTTP                       │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
                 ┌─────────────▼─────────────┐
                 │   DalamudMCP.Cli 进程      │
                 │   (CLI / MCP Host)         │
                 │                            │
                 │   RemoteMcpToolService ──►│── NamedPipeProtocolClient
                 │   RemoteCliInvoker    ──►│── NamedPipeProtocolClient
                 └─────────────┬─────────────┘
                               │ Named Pipe (MemoryPack)
                 ┌─────────────▼─────────────┐
                 │   DalamudMCP.Plugin 进程   │
                 │   (Dalamud 插件内)          │
                 │                            │
                 │   NamedPipeProtocolServer   │
                 │         │                  │
                 │   OperationProtocolDispatcher │
                 │         │                  │
                 │   GeneratedOperationInvoker │
                 │         │                  │
                 │   [Operation] 类实例          │
                 │   (各 Operation 通过 DI 注入  │
                 │    Dalamud 服务)              │
                 └─────────────────────────────┘
```

### 组件分层

| 项目 | 职责 | Dalamud 依赖 |
|------|------|-------------|
| `DalamudMCP.Framework` | 操作模型抽象（`IOperation<T,R>`、属性、`OperationContext`） | 无 |
| `DalamudMCP.Protocol` | 命名管道 IPC、协议信封、MemoryPack 序列化 | 无 |
| `DalamudMCP.Framework.Mcp` | MCP 工具绑定辅助 | 无（依赖 ModelContextProtocol NuGet） |
| `DalamudMCP.Framework.Cli` | CLI 调用辅助 | 无 |
| `DalamudMCP.Framework.Generators` | Roslyn 源生成器 | 无（netstandard2.0） |
| `DalamudMCP.Cli` | CLI 二进制入口、MCP 服务托管 | 无 |
| `DalamudMCP.Plugin` | Dalamud 插件主体、操作实现、服务注册 | **是** |

### 数据流：请求-响应模式

```
AI Client ──► MCP Tool Call ──► RemoteMcpToolService.CallToolAsync()
                                    │
                                    ├── ProtocolOperationRequestFactory.CreateFromMcp()
                                    ├── IProtocolOperationClient.InvokeAsync(operationId, payload)
                                    │       │
                                    │       └── NamedPipeProtocolClient 发送 ProtocolRequestEnvelope
                                    │
                          Named Pipe IPC ──► NamedPipeProtocolServer
                                                    │
                                              OperationProtocolDispatcher.DispatchAsync()
                                                    │
                                              GeneratedOperationInvoker.TryInvoke()
                                                    │
                                              Operation.ExecuteAsync()
                                                    │
                                              ProtocolResponseEnvelope 返回
```

### 关键抽象

| 抽象 | 位置 | 角色 |
|------|------|------|
| `IOperation<TRequest, TResult>` | Framework | 操作接口，所有操作必须实现 |
| `[Operation]` + `[McpTool]` + `[CliCommand]` | Framework | 属性声明，由源生成器扫描 |
| `OperationDescriptor` | Framework | 编译期生成的操作描述（参数、可见性、MCP 名称） |
| `GeneratedOperationInvoker` | Generated | 源生成器生成的分发器，按 OperationId 路由 |
| `IOperationInvoker` | Framework | 运行时调用接口 |
| `OperationProtocolDispatcher` | Plugin | 命名管道请求→操作调用的调度器 |
| `ProtocolContract` | Protocol | 信封序列化/反序列化 |
| `PluginOperationExposurePolicy` | Plugin | 控制哪些操作对 MCP/CLI 可见 |
| `IPluginIpcGateway` | Plugin.Operations | Dalamud IPC 网关抽象（已有 UnsafeInvokePluginIpcOperation 中） |

---

## 新功能架构映射

### 功能 1：插件重载

**映射到现有架构：** 新增一个 `[Operation]` 类。

| 组件 | 类型 | 变更 |
|------|------|------|
| `ReloadPluginOperation` | **新增** | `DalamudMCP.Plugin/Operations/ReloadPluginOperation.cs` |
| `IPluginManager` | **引用** | Dalamud 注入服务，仅 API 9+ 可用 |

**数据流：**
```
AI ──► MCP tool: reload_plugin ──► OperationProtocolDispatcher
                                        │
                                  ReloadPluginOperation.ExecuteAsync()
                                        │
                                  IFramework.RunOnFrameworkThread()
                                        │
                                  IDalamudPluginInterface.InstalledPlugins
                                        │
                                  找到目标插件 → 调用 Reload()
                                        │
                                  返回 ReloadPluginResult
```

**关键设计决策：**

1. **使用 `IDalamudPluginInterface.InstalledPlugins`**（而非 `IPluginManager`）。`IExposedPlugin` 有 `Reload()` 方法和 `IsLoaded` 属性，这是 Dalamud 标准的重载方式。
2. **必须在 Framework 线程执行**：`IExposedPlugin.Reload()` 需要 Dalamud Framework 线程上下文。使用 `IFramework.RunOnFrameworkThread()` 尼古。
3. **不自动等待就绪**：按 PROJECT.md 约束，重载触发后立即返回，AI 端决定延迟。

**操作声明示例：**
```csharp
[Operation("plugin.reload", Description = "Reloads a Dalamud plugin by its internal name.")]
[McpTool("reload_plugin")]
[CliCommand("plugin", "reload")]
public sealed partial class ReloadPluginOperation
    : IOperation<ReloadPluginOperation.Request, ReloadPluginResult>
{
    // Request: InternalName, WaitReady (bool, default false)
    // Result:  IsLoaded, InternalName, ErrorMessage?
}
```

### 功能 2：跨插件 IPC 调用

**映射到现有架构：** 扩展现有 `UnsafeInvokePluginIpcOperation` 的模式，新增一个**安全版本**。

| 组件 | 类型 | 变更 |
|------|------|------|
| `InvokePluginIpcOperation` | **新增** | 安全的 IPC 调用操作 |
| `IPluginIpcGateway` | **扩展接口或新增** | 当前接口定义在 `UnsafeInvokePluginIpcOperation` 内部，需要提取到更可访问的位置 |
| `PluginIpcGateway` | **移动** | 从内部类提取为共享服务 |

**与 `unsafe.invoke.plugin-ipc` 的区别：**

| 方面 | `unsafe.invoke.plugin-ipc` | `plugin.invoke-ipc` (新) |
|------|---------------------------|--------------------------|
| 风险等级 | 不安全（需启用 unsafe 操作） | 安全（默认启用） |
| 输入 | callgate 名称 + 原始 JSON 参数 + 类型声明 | 结构化请求（target plugin + method + params） |
| 类型安全 | 运行时反射，类型可错 | 约定类型映射，更可预测 |
| 结果 | 原始 JSON 字符串 | 结构化结果 |

**数据流（与现有完全一致）：**
```
AI ──► MCP tool: invoke_plugin_ipc ──► OperationProtocolDispatcher
                                            │
                                      InvokePluginIpcOperation.ExecuteAsync()
                                            │
                                      IPluginIpcGateway.TryCreate(callgate, ...)
                                            │
                                      subscriber.InvokeFunc(args)
                                            │
                                      返回 InvokePluginIpcResult
```

**关键设计决策：**

1. **复用 `IPluginIpcGateway` 抽象**：当前 `UnsafeInvokePluginIpcOperation.IPluginIpcGateway` 定义在操作类内部（`internal`），需要提取到共享命名空间。
2. **不提供 SDK 给被测插件**：按 PROJECT.md 约束，被测插件仅实现标准的 Dalamud IPC 接口约定，无需额外依赖。
3. **安全版本 vs unsafe 版本**：安全版本使用更受约束的参数格式（适用场景更可控），但底层仍走 Dalamud IPC 反射调用。

### 功能 3：数据回传

**映射到现有架构：** 这是最重要的架构扩展点——需要新增**推送通道**。

**当前架构的限制：** 现有架构是严格的请求-响应模式：
- AI 发请求 → Plugin 处理 → Plugin 返回响应
- Plugin 无法主动推送数据给 AI

**数据回传的需求：** 目标插件通过 Dalamud IPC 发送数据 → DalamudMCP 需要将数据转发给 AI。

**两种可行方案：**

#### 方案 A：轮询模式（推荐）

```
目标插件 ──[Dalamud IPC SendMessage]──► PluginIpcDataRelayService (缓冲区)
                                              │
AI ──► MPL tool: plugin_data_poll ──► PluginDataPollOperation.ExecuteAsync()
                                              │
                                        从缓冲区取出数据 ──► 返回给 AI
```

| 组件 | 类型 | 变更 |
|------|------|------|
| `PluginIpcDataRelayService` | **新增** | 订阅目标插件 IPC 通道，缓冲数据 |
| `PluginDataPollOperation` | **新增** | AI 轮询获取回传数据 |
| `PluginDataSubscribeOperation` | **新增** | AI 订阅/取消订阅目标插件的 IPC 通道 |

**优点：**
- 零协议变更：复用现有请求-响应模式
- AI 完全控制节奏
- MCP 协议兼容（所有 MCP 客户端支持 tools/call）

**轮询模式的工作流程：**
1. AI 调用 `plugin_data_subscribe` → DalamudMCP 订阅目标插件的 IPC 通道
2. 目标插件通过 IPC SendMessage 推送数据 → `PluginIpcDataRelayService` 缓冲
3. AI 调用 `plugin_data_poll` → 获取缓冲区中的新数据
4. AI 完成后调用 `plugin_data_unsubscribe` → 清理订阅

#### 方案 B：MCP Notification 推送（备选）

利用 MCP 协议的 `notifications/tools/list_changed` 或自定义 notification 通道主动推送。需要修改 CLI 端的 MCP 服务层来支持 notification 发送。

**不推荐原因：**
- 需要修改 `RemoteMcpToolService` 和 MCP 服务托管层
- 不是所有 MCP 客户端都支持 notification
- 增加复杂度，收益不大

**最终选择：方案 A（轮询模式）**

### 功能 4：斜杠命令调度

**映射到现有架构：** 新增一个 `[Operation]` 类。

| 组件 | 类型 | 变更 |
|------|------|------|
| `SlashCommandOperation` | **新增** | 斜杠命令调度操作 |

**数据流：**
```
AI ──► MCP tool: execute_slash_command ──► OperationProtocolDispatcher
                                                │
                                          SlashCommandOperation.ExecuteAsync()
                                                │
                                          IFramework.RunOnFrameworkThread()
                                                │
                                          使用 /xlcommand 或游戏内聊天输入
                                                │
                                          返回 SlashCommandResult
```

**关键设计决策：**

1. **使用 Dalamud 的命令系统**：`ICommandManager.DispatchCommand()` 或直接发送聊天消息。`/xlcommand` 类型的命令可以通过 `ICommandManager` 路由，普通游戏 `/` 命令通过聊天输入。
2. **必须在 Framework 线程执行**：所有影响游戏状态的操作都需要 Framework 线程上下文。
3. **无返回值**：斜杠命令是"发送即忘"——触发后立即返回确认，不等待命令结果。命令执行结果可通过 `ChatLogBufferService` 日后查询（已有功能）。

---

## 新增组件清单

### 新增文件

| 文件 | 项目 | 职责 |
|------|------|------|
| `Operations/ReloadPluginOperation.cs` | Plugin | 插件重载操作 |
| `Operations/InvokePluginIpcOperation.cs` | Plugin | 安全跨插件 IPC 调用操作 |
| `Operations/SlashCommandOperation.cs` | Plugin | 斜杠命令调度操作 |
| `Operations/PluginDataSubscribeOperation.cs` | Plugin | IPC 数据订阅操作 |
| `Operations/PluginDataPollOperation.cs` | Plugin | IPC 数据轮询操作 |
| `Operations/PluginDataUnsubscribeOperation.cs` | Plugin | IPC 数据取消订阅操作 |
| `Services/PluginIpcDataRelayService.cs` | Plugin | IPC 数据中继缓冲服务 |
| `Services/PluginIpcGateway.cs` | Plugin | 从 `UnsafeInvokePluginIpcOperation` 提取的 IPC 网关实现 |

### 修改文件

| 文件 | 项目 | 变更 |
|------|------|------|
| `UnsafeInvokePluginIpcOperation.cs` | Plugin | 提取 `IPluginIpcGateway`/`IPluginCallGateSubscriber` 到共享位置，改为引用共享接口 |
| `PluginServiceCollectionExtensions.cs` | Plugin | 注册 `PluginIpcGateway`、`PluginIpcDataRelayService` 为单例 |
| `PluginOperationExposurePolicy.cs` | Plugin | 添加新操作的分类（`ActionOperationIds` 或新分类） |
| `PluginEntryPoint.cs` | Plugin | 无需变更（DI 自动通过源生成器注册） |

### 不变更的文件

| 文件/组件 | 原因 |
|-----------|------|
| 所有 `Framework/` 项目 | 操作模型和协议层完全适配新增操作 |
| `Framework.Generators/` | 源生成器自动处理新的 `[Operation]` 类 |
| `Protocol/` | 协议信封无变更，新操作使用现有序列化 |
| `Framework.Mcp/` | MCP 工具绑定无变更 |
| `Framework.Cli/` | CLI 调用绑定无变更 |
| `OperationProtocolDispatcher` | 通过 `IOperationInvoker` 自动路由新操作 |
| `NamedPipeProtocolServer/Client` | 无协议变更 |

---

## 推荐架构模式

### 模式 1：操作属性 + 源生成器（遵循现有模式）

**何时使用：** 所有新功能——插件重载、IPC 调用、数据轮询、斜杠命令。

**示例：**
```csharp
[Operation("plugin.reload", Description = "...")]
[McpTool("reload_plugin")]
[CliCommand("plugin", "reload")]
[ResultFormatter(typeof(ReloadPluginOperation.TextFormatter))]
public sealed partial class ReloadPluginOperation
    : IOperation<ReloadPluginOperation.Request, ReloadPluginResult>
{
    [MemoryPackable]
    [ProtocolOperation("plugin.reload")]
    public sealed partial record Request
    {
        [Option("internal-name", Description = "The internal name of the plugin to reload.")]
        public string InternalName { get; init; } = string.Empty;
    }

    public ValueTask<ReloadPluginResult> ExecuteAsync(Request request, OperationContext context)
    {
        // 实现在 Framework 线程执行重载
    }
}
```

**为什么：** 源生成器自动注册到 `GeneratedOperationRegistry`、`GeneratedOperationInvoker`、`GeneratedMcpTools`。新增操作不需要修改任何生成器或注册代码。

### 模式 2：Dalamud IPC 网关抽象（提取并扩展现有模式）

**何时使用：** 跨插件 IPC 调用和数据回传。

**当前状态：** `IPluginIpcGateway` 和 `IPluginCallGateSubscriber` 是 `UnsafeInvokePluginIpcOperation` 的内部接口。

**建议：** 提取到 `Services/PluginIpcGateway.cs`，作为共享单例服务：

```csharp
namespace DalamudMCP.Plugin.Services;

public interface IPluginIpcGateway
{
    bool TryCreate(string callgate, IReadOnlyList<Type> typeArguments, out IPluginCallGateSubscriber? subscriber);
}

public interface IPluginCallGateSubscriber
{
    bool HasFunction { get; }
    object? InvokeFunc(IReadOnlyList<object?> arguments);
    // v1.1 新增：订阅 IPC 消息
    IPluginIpcSubscription Subscribe(Action<object?[]> handler);
}

public interface IPluginIpcSubscription : IDisposable
{
    string Callgate { get; }
}
```

**为什么：** 
- `UnsafeInvokePluginIpcOperation` 和新的 `InvokePluginIpcOperation` 共享同一个网关
- `PluginIpcDataRelayService` 需要订阅 IPC 消息（`Subscribe`）
- 测试时可 mock `IPluginIpcGateway`

### 模式 3：数据中继缓冲服务

**何时使用：** IPC 数据回传。

**设计：**
```csharp
namespace DalamudMCP.Plugin.Services;

public sealed class PluginIpcDataRelayService : IDisposable
{
    // 按订阅通道缓冲数据
    private readonly ConcurrentDictionary<string, Channel<PluginIpcDataEvent>> channels;
    
    // 订阅目标 IPC 通道
    public void Subscribe(string ipcChannel, int maxBufferSize = 1000);
    
    // 取消订阅
    public void Unsubscribe(string ipcChannel);
    
    // 轮询：获取指定通道的缓冲数据
    public PluginIpcDataPollResult Poll(string ipcChannel, int maxCount = 100);
    
    // IDisposable
    public void Dispose();
}
```

**关键考虑：**
- 使用 `System.Threading.Channels.Channel<T>` 作为缓冲区（有界，防止内存泄漏）
- 每个订阅通道独立缓冲
- 订阅使用 Dalamud 的 `ICallGateSubscriber.Subscribe()` API
- 数据格式统一为 JSON 字符串（因为 IPC 消息参数类型是动态的）

### 模式 4：暴露策略扩展

**当前：** `PluginOperationExposurePolicy` 分两类：
- `ActionOperationIds`：行动操作（需要启用 action 标志）
- `UnsafeOperationIds`：不安全操作（需要启用 unsafe 标志）

**新增分类建议：**
```csharp
private static readonly HashSet<string> UnsafeOperationIds =
[
    "unsafe.invoke.plugin-ipc"
];

private static readonly HashSet<string> ActionOperationIds =
[
    "target.object",
    "interact.with.target",
    // ... 现有的行动操作 ...
    "plugin.reload",           // 新增：插件重载是行动操作
    "execute.slash-command",   // 新增：斜杠命令是行动操作
];

// 新增：IPC 操作不需要特殊标志，默认启用
// "plugin.invoke-ipc"
// "plugin.data.subscribe"
// "plugin.data.poll"
// "plugin.data.unsubscribe"
```

---

## 反模式警示

### 反模式 1：绕过操作模型直接暴露 IPC

**不应该：** 在 `OperationProtocolDispatcher` 中硬编码新操作的路由逻辑。

**原因：** 破坏了源生成器驱动的操作注册模型。所有操作应该通过 `[Operation]` 属性声明，由源生成器自动注册。

**应该：** 每个新功能都创建一个 `IOperation<TRequest, TResult>` 实现类。

### 反模式 2：将 IPC 网关注入到每个操作

**不应该：** 让每个需要 IPC 的操作都自己构造 `PluginIpcGateway` 实例。

**原因：** 构造函数膨胀、重复代码、测试困难。

**应该：** 提取 `IPluginIpcGateway` 为 DI 单例，通过构造函数注入。

### 反模式 3：在数据回传中使用无限缓冲区

**不应该：** 用 `ConcurrentQueue<T>` 或 `List<T>` 存储回传数据，没有容量限制。

**原因：** 如果 AI 客户端不轮询，内存会无限增长。

**应该：** 使用有界 `Channel<T>` 或带容量限制的环形缓冲区，超出容量时丢弃最旧数据。

### 反模式 4：在不安全的操作中暴露插件发现

**不应该：** 在 v1.1 中提供"列出所有已安装插件及 IPC 接口"的功能。

**原因：** PROJECT.md 明确将"插件自动发现"列为 Out of Scope。

**应该：** AI 客户端需要预先知道目标插件的 IPC callgate 名称。

---

## 数据流变化

### 新增数据流 1：插件重载

```
AI Client
  └── MCP tool: reload_plugin(internal_name: "MyPlugin")
        │
        ├── NamedPipeProtocolClient 发送 ProtocolRequestEnvelope
        │       RequestType: "plugin.reload"
        │       Payload: MemoryPack serialized Request
        │
        └── NamedPipeProtocolServer 接收
                │
            OperationProtocolDispatcher.DispatchAsync()
                │
            GeneratedOperationInvoker.TryInvoke("plugin.reload", ...)
                │
            ReloadPluginOperation.ExecuteAsync()
                │
                ├── IFramework.RunOnFrameworkThread() ← 必须在 Framework 线程
                ├── IDalamudPluginInterface.InstalledPlugins → 找到目标
                ├── IExposedPlugin.Reload() → 触发重载
                └── 返回 ReloadPluginResult { IsReloadTriggered: true }
```

### 新增数据流 2：跨插件 IPC 调用

```
AI Client
  └── MCP tool: invoke_plugin_ipc(callgate: "MyPlugin.GetData", args: [...])
        │
        ├── NamedPipeProtocolClient 发送 ProtocolRequestEnvelope
        │       RequestType: "plugin.invoke-ipc"
        │
        └── NamedPipeProtocolServer 接收
                │
            OperationProtocolDispatcher.DispatchAsync()
                │
            InvokePluginIpcOperation.ExecuteAsync()
                │
                ├── IPluginIpcGateway.TryCreate("MyPlugin.GetData", ...)
                ├── IPluginCallGateSubscriber.HasFunction → 检查
                ├── IPluginCallGateSubscriber.InvokeFunc(args) → 调用目标插件的 IPC 方法
                └── 返回 InvokePluginIpcResult { Succeeded: true, ResultJson: "..." }
```

### 新增数据流 3：数据回传（轮询模式）

```
步骤 1：订阅
AI Client
  └── MCP tool: plugin_data_subscribe(channel: "MyPlugin.StatusChannel")
        │
        └── PluginDataSubscribeOperation.ExecuteAsync()
                │
                └── PluginIpcDataRelayService.Subscribe("MyPlugin.StatusChannel")
                        │
                        └── ICallGateSubscriber<...>.Subscribe(handler) ← Dalamud IPC

步骤 2：目标插件推送数据
目标插件 ──[IPC SendMessage]──► Dalamud IPC 消息总线
                                        │
                                   ICallGateSubscriber 触发 handler
                                        │
                                   PluginIpcDataRelayService 缓冲数据

步骤 3：轮询获取

AI Client
  └── MCP tool: plugin_data_poll(channel: "MyPlugin.StatusChannel")
        │
        └── PluginDataPollOperation.ExecuteAsync()
                │
                └── PluginIpcDataRelayService.Poll("MyPlugin.StatusChannel")
                        │
                        └── 返回 PluginIpcDataPollResult { Events: [...] }
```

### 新增数据流 4：斜杠命令调度

```
AI Client
  └── MCP tool: execute_slash_command(command: "/ping")
        │
        └── SlashCommandOperation.ExecuteAsync()
                │
                ├── IFramework.RunOnFrameworkThread()
                ├── GameInterop 或 IChatGui.SendMessage 执行命令
                └── 返回 SlashCommandResult { Dispatched: true }
```

---

## 可扩展性考虑

| 关注点 | 100 用户 | 10K 用户 | 1M 用户 |
|--------|---------|---------|---------|
| 数据回传缓冲区 | 单 Channel<T>，1K 容量 | 多通道，每通道 1K | 需要流控和过期清理 |
| IPC 调用延迟 | 毫秒级（进程内） | 毫秒级（不受负载影响） | 同左 |
| 斜杠命令执行 | 即时（Framework 线程排队） | 同左 | 同左 |
| 插件重载 | 即时（Framework 线程同步） | 同左 | 同左 |

**注意：** DalamudMCP 是单用户本地工具，不会有多用户并发场景。上述可扩展性维度主要考虑的是数据回传缓冲区在长时间运行时的内存问题。

---

## 推荐构建顺序

按依赖关系和风险排序：

### 阶段 1：基础设施（提取共享 IPC 网关）
**为什么先做：** 所有跨插件功能都依赖 IPC 网关。

1. 从 `UnsafeInvokePluginIpcOperation.cs` 提取 `IPluginIpcGateway` 和 `IPluginCallGateSubscriber` 到 `Services/IPluginIpcGateway.cs`
2. 从 `UnsafeInvokePluginIpcOperation.cs` 提取 `PluginIpcGateway` 实现到 `Services/PluginIpcGateway.cs`
3. 在 `PluginServiceCollectionExtensions.cs` 注册 `IPluginIpcGateway` 为单例
4. 修改 `UnsafeInvokePluginIpcOperation` 使用注入的 `IPluginIpcGateway`
5. 运行测试确保无回归

### 阶段 2：插件重载操作
**为什么先做：** 最简单的跨插件功能，验证新操作模式正确。

1. 创建 `ReloadPluginOperation.cs`
2. 更新 `PluginOperationExposurePolicy` 添加 `plugin.reload` 到行动操作
3. 运行测试

### 阶段 3：斜杠命令调度
**为什么先做：** 功能独立且简单。

1. 创建 `SlashCommandOperation.cs`
2. 更新 `PluginOperationExposurePolicy` 添加 `execute.slash-command` 到行动操作
3. 运行测试

### 阶段 4：安全 IPC 调用
**依赖阶段 1：** 需要 `IPluginIpcGateway` 抽象。

1. 创建 `InvokePluginIpcOperation.cs`（安全版本）
2. 操作默认启用，不需要特殊标志
3. 运行测试

### 阶段 5：数据回传
**依赖阶段 1 和 4：** 最复杂的功能，需要 IPC 订阅能力。

1. 创建 `PluginIpcDataRelayService.cs`
2. 创建 `PluginDataSubscribeOperation.cs`
3. 创建 `PluginDataPollOperation.cs`
4. 创建 `PluginDataUnsubscribeOperation.cs`
5. 在 `PluginServiceCollectionExtensions.cs` 注册 `PluginIpcDataRelayService`
6. 更新 `IPluginCallGateSubscriber` 添加消息订阅支持
7. 运行测试

---

## 源

- Dalamud IPC API (GetIpcSubscriber, ICallGateSubscriber) — dalamud.dev — HIGH confidence
- Dalamud IPluginInterface.InstalledPlugins / IExposedPlugin — dalamud.dev — HIGH confidence
- 现有代码库分析 (`UnsafeInvokePluginIpcOperation`, `OperationProtocolDispatcher`, 源生成器) — HIGH confidence
- Dalamud `ICommandManager` / 聊天命令 API — MEDIUM confidence（需验证 API 15 具体接口）
- `ICallGateSubscriber.Subscribe` / `SendMessage` 消息模式 — MEDIUM confidence（需验证泛型参数限制）

---

*架构研究：DalamudMCP v1.1 自动化测试桥接*
*研究日期：2026-05-01*