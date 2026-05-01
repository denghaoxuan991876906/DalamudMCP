# Technology Stack — v1.1 自动化测试桥接

**Project:** DalamudMCP
**Milestone:** v1.1 — 自动化测试桥接（跨插件 IPC、插件重载、斜杠命令调度）
**Researched:** 2026-05-01
**Overall confidence:** HIGH

## 总结

v1.1 不引入任何新的 NuGet 包或运行时依赖。所有新功能完全基于已有的 Dalamud API 15 平台能力和现有项目基础设施实现：

- **插件重载/斜杠命令调度** → 走 `IDalamudPluginInterface` 的 IPC callgate 体系 + `IChatGui` 的 Print 方法注入斜杠命令到游戏聊天框
- **跨插件 IPC 调用** → 已有 `UnsafeInvokePluginIpcOperation` 的反射模式可复用；v1.1 新增结构化操作并定义 DalamudMCP 专用 callgate 命名约定
- **数据回传** → DalamudMCP 注册 `ICallGateProvider<string, string>` callgate，目标插件通过 `GetIpcSubscriber` 调用后由 DalamudMCP 将数据推送到 MCP 客户端（通过现有的 NamedPipe/MCP 协议层）
- **MCP 通知** → `ModelContextProtocol` v1.1.0 已支持 `SendNotificationAsync`，可用于数据回传的推送机制

所有技术决策均在现有技术栈内闭环，无需额外依赖。

---

## 推荐技术栈

### 核心框架（不变）

| 技术 | 版本 | 用途 | 推荐理由 |
|------|------|------|----------|
| `Dalamud.NET.Sdk` | 15.0.0 | MSBuild SDK | 已在 v1.0 验证通过 |
| .NET 目标框架 | `net10.0` / `net10.0-windows7.0` | 运行时 | 不变 |
| `Dalamud API Level` | 15 | 运行时兼容性声明 | 不变 |
| `MemoryPack` | 1.21.4 | 二进制序列化（命名管道协议） | 不变，用于新数据回传类型 |
| `Microsoft.Extensions.DependencyInjection` | 10.0.0 | DI 容器 | 不变，新操作通过 DI 注册 |
| `ModelContextProtocol` | 1.1.0 | MCP 协议 SDK | 不变，v1.1 需将其通知能力用于数据推送 |
| `Microsoft.CodeAnalysis.CSharp` | 4.14.0 | 源生成器 | 不变，新操作的 `[Operation]` 属性继续由生成器自动注册 |

### 新增功能对应的 Dalamud API（已有则复用）

| Dalamud API | 用途 | 现有使用 | v1.1 新增使用 |
|-------------|------|----------|--------------|
| `IDalamudPluginInterface.GetIpcProvider<T>` | 注册 DalamudMCP 的 IPC callgate（数据回传入口） | 尚未使用 | **新增**：注册 `ICallGateProvider<string,string>` 供目标插件调用 |
| `IDalamudPluginInterface.GetIpcSubscriber<T1..Tn,TRet>` | 调用目标插件暴露的 IPC 方法 | `UnsafeInvokePluginIpcOperation` 已使用反射版本 | **新增**：结构化 IPC 调用操作直接使用强类型版本 |
| `IChatGui.Print` | 向游戏聊天框输出文字（含斜杠命令执行） | 已注入到 `PluginEntryPoint` | **新增**：通过 `Print(new SeStringBuilder().Build(), XivChatType.Debug)` 发送斜杠命令 |
| `IFramework.RunOnFrameworkThread` | 在框架线程执行 IPC 调用 | `UnsafeInvokePluginIpcOperation` 已使用 | **复用**：插件重载和 IPC 调用均需框架线程执行 |
| `ICallGateSubscriber.HasFunction` | 检查目标插件是否已注册 IPC 方法 | `UnsafeInvokePluginIpcOperation` 已使用 | **复用**：开始监听前检测目标插件就绪状态 |
| `ICallGateSubscriber.Subscribe` | 订阅目标插件的 IPC 通知 | 尚未使用 | **新增**：订阅目标插件的推送 callgate |

### 新增功能对应的 MCP SDK 能力

| MCP SDK 能力 | 版本 | 用途 | 推荐理由 |
|--------------|------|------|----------|
| `McpServer.SendNotificationAsync` | 1.1.0 | 数据回传：IPC 数据推送到 MCP 客户端 | MCP 协议原生通知机制，避免轮询；支持自定义通知方法名 |

### 开发工具（不变）

| 工具 | 用途 | 备注 |
|------|------|------|
| `xunit.v3.mtp-v2` 3.2.2 | 单元测试运行器 | 新操作需配套测试 |
| `coverlet.MTP` 8.0.0 | 测试覆盖率 | 不变 |
| `DotNet.ReproducibleBuilds` 1.2.39 | 构建可重现性 | 不变 |

---

## 新增功能技术方案

### 1. 插件重载

**机制：** 通过 `IChatGui` 向聊天框发送 `/xlreload <插件内部名>` 斜杠命令。

**为什么不用 `IPluginManager`：**
- Dalamud API 15 不向插件暴露 `IPluginManager` 接口（它是 Dalamud 内部类型）
- `/xlreload` 是 Dalamud 官方提供的重载机制，稳定可靠
- 斜杠命令走游戏聊天框，与手动操作行为一致

**技术细节：**
```csharp
// 使用已注入的 IChatGui 发送斜杠命令
chatGui.Print(new SeStringBuilder().AddText("/xlreload PluginInternalName").Build(), XivChatType.Debug);
```

**关键约束：**
- 不自动等待就绪（由 AI 端控制延迟）→ 符合 v1.1 设计决策
- 重载成功与否通过 `IChatGui.ChatMessage` 事件监听确认消息（可选增强）

### 2. 跨插件 IPC 调用

**机制：** 扩展现有 `UnsafeInvokePluginIpcOperation` 模式，新增结构化 IPC 操作。

**技术方向：**
- 保留 `UnsafeInvokePluginIpcOperation` 作为底层逃生舱
- 新增高层封装操作，使用 Dalamud 规定的 callgate 命名约定（如 `DalamudMCP.Invoke.<callgate_name>`）
- 目标插件只需在自身代码中注册 `ICallGateProvider` 即可被调用，无需 SDK 依赖

**已有基础设施复用：**
- `IPluginIpcGateway` / `IPluginCallGateSubscriber` 内部接口（`UnsafeInvokePluginIpcOperation.cs`）→ 扩展或复用
- `PluginIpcValueKind` 枚举 → 复用
- `IFramework.RunOnFrameworkThread` → 复用框架线程调度

### 3. 数据回传

**机制：** DalamudMCP 注册 `ICallGateProvider<string, string>` callgate，目标插件通过 `GetIpcSubscriber` 调用此 callgate 发送数据，DalamudMCP 收到后通过 MCP 通知推送到 AI 客户端。

**callgate 命名约定：**
- `DalamudRelay.Data` — 通用数据回传（目标插件 → DalamudMCP → AI）
- `DalamudRelay.Status` — 状态查询（AI → DalamudMCP → 目标插件，通过 `HasFunction` 检测可用性）

**推送机制：**
- 标准 MCP 工具调用：AI 主动轮询 `relay.data.read` 操作
- MCP 通知推送（增强）：DalamudMCP 通过 `McpServer.SendNotificationAsync` 推送数据变更通知

**为什么不用消息队列库：**
- Dalamud IPC callgate 本身就是进程内消息传递机制，零延迟
- MCP 协议通知机制已能满足推送需求
- 引入额外消息队列（如 `System.Threading.Channels`）仅在有背压需求时才考虑

### 4. 斜杠命令调度

**机制：** 通过 `IChatGui.Print` 发送斜杠命令字符串到游戏聊天框。

**技术实现：**
- 与插件重载共享同一发送机制
- 需要注意 `IChatGui.Print` 发送的斜杠命令会被游戏执行
- 使用 `XivChatType.Debug` 或 `Echo` 频道避免命令泄漏到公共聊天

---

## 不应使用的技术

| 避免 | 原因 | 替代方案 |
|------|------|----------|
| `IPluginManager`（Dalamud 内部类型） | 不在公共 API 15 中暴露，依赖它会在版本更新时崩溃 | `/xlreload` 斜杠命令 |
| `Dalamud.Networking.Http` | 已在 API 15 中移除，无替代 | 不需要网络通信 |
| `ChatHandlers` 静态类 | 已移除 | `IChatGui.ChatMessage` 事件 |
| 目标插件引入 NuGet SDK 包 | 违反 v1.1 约束："被测插件只需实现 IPC 接口约定" | Dalamud 原生 `ICallGateProvider` / `ICallGateSubscriber` |
| `System.Threading.Channels` 消息队列 | 无背压需求时增加不必要的复杂度 | 直接通过 MCP 通知或操作结果返回数据 |
| 自定义 WebSocket/SSE 推送通道 | Named Pipe 协议层 + MCP 通知已足够 | MCP `SendNotificationAsync` |
| JSON-RPC over named pipe 独立协议 | 已有 MemoryPack 二进制协议层，无需引入新协议 | 复用 `ProtocolContract` + `MemoryPack` |

---

## 替代方案考虑

| 类别 | 推荐方案 | 替代方案 | 不选替代方案的原因 |
|------|----------|----------|---------------------|
| 插件重载 | `/xlreload` 斜杠命令 | Dalamud 内部 `PluginManager.ReloadPlugin()` | 内部 API，不稳定，版本更新可能变化 |
| IPC 调用 | 反射方式 `GetIpcSubscriber`（扩展已有操作） | 为每个目标插件生成强类型代理 | 目标插件 IPC 签名未知，反射方式通用性更强 |
| 数据回传 | DalamudMCP 注册 callgate → MCP 通知 | 目标插件注册 callgate → DalamudMCP 轮询 Subscribe | 推送比轮询效率更高；MCP 通知天然单向推送 |
| 斜杠命令 | `IChatGui.Print` 发送到聊天框 | `ICommandManager` 直接执行 | `ICommandManager` 只处理已注册的自定义命令，不处理游戏原生命令。`/xlreload` 等是游戏级命令需走聊天框 |
| 操作注册 | 扩展源生成器 `[Operation]` 属性 | 运行时反射动态注册 | 生成器模式已验证，编译时检查更安全 |

---

## 版本兼容性

| 包 | 当前版本 | v1.1 影响 | 兼容性说明 |
|----|----------|-----------|------------|
| `Dalamud.NET.Sdk` | 15.0.0 | 不变 | API 15 已验证 |
| `MemoryPack` | 1.21.4 | 不变 | 新增 `[MemoryPackable]` 数据类型 |
| `ModelContextProtocol` | 1.1.0 | 不变 | 已支持 `SendNotificationAsync` |
| `Microsoft.Extensions.DependencyInjection` | 10.0.0 | 不变 | 新操作通过 DI 注册 |
| `Microsoft.CodeAnalysis.CSharp` | 4.14.0 | 不变 | 生成器现有逻辑兼容新操作 |
| `xunit.v3.mtp-v2` | 3.2.2 | 不变 | 新增测试用例 |
| .NET SDK | 10.0.201 | 不变 | `global.json` 无需更新 |

---

## 安装（无新包）

```bash
# 无需安装新 NuGet 包
# 所有新功能使用已有的 Dalamud API 和 ModelContextProtocol SDK

# 构建命令不变
.\build\restore.ps1
.\build\build.ps1
.\build\test.ps1
```

---

## 按方案的栈变体

**如果需要数据回传的背压处理：**
- 引入 `System.Threading.Channels`（BCL 内置，非 NuGet 包）
- 使用 `Channel<string>.CreateBounded()` 缓冲 IPC 数据流
- 配合 MCP 通知的分发速率控制背压

**如果 Dalamud API 16 改变了 IPC 机制：**
- Dalamud IPC 是公共 API 的一部分，`ICallGateProvider` / `ICallGateSubscriber` 自 API 4 起保持稳定
- 如果 API 变化，`UnsafeInvokePluginIpcOperation` 的反射适配层已经隔离了变化点
- 新增的结构化操作需要添加 API 版本检测

**如果需要更丰富的回传数据类型：**
- 当前设计使用 `string` 类型（JSON 序列化后的字符串）
- 如需强类型数据，可在 callgate 中使用 MemoryPack 序列化的 `byte[]`
- 但这要求目标插件也引用 MemoryPack（违反无 SDK 依赖约束），因此 JSON string 是更好的选择

---

## Dalamud IPC Callgate 命名约定（v1.1）

DalamudMCP 定义以下 callgate 名称供目标插件使用：

| Callgate 名称 | 方向 | 类型签名 | 用途 |
|---------------|------|----------|------|
| `DalamudMCP.Relay.Data` | 目标插件 → DalamudMCP | `ICallGateProvider<string, string>` → 目标插件调用 `InvokeFunc(jsonPayload)` 发送数据 |
| `DalamudMCP.Relay.Status` | DalamudMCP → 目标插件 | `ICallGateProvider<string>` → DalamudMCP 查询目标插件是否就绪 |

目标插件接入约定：

```csharp
// 目标插件代码（无需 SDK 依赖，只需 Dalamud 本身）
public class MyTargetPlugin : IDalamudPlugin
{
    public MyTargetPlugin(IDalamudPluginInterface pluginInterface)
    {
        // 注册 IPC 方法供 DalamudMCP 调用
        var callProvider = pluginInterface.GetIpcProvider<string, string>("MyPlugin.DoSomething");
        callProvider.RegisterFunc(DoSomething);

        // 订阅 DalamudMCP 的数据回传 callgate
        var relaySubscriber = pluginInterface.GetIpcSubscriber<string, string>("DalamudMCP.Relay.Data");
        // 注意：DalamudMCP 是 provider，目标插件无需订阅数据回传
    }

    private string DoSomething(string input) => $"Result: {input}";
}
```

**重要说明：** 准确的 callgate 约定将在架构文档中细化。此处仅定义技术可行性方向。

---

## 来源

- **HIGH** — `IDalamudPluginInterface` IPC API 文档（dalamud.dev/api/Dalamud.Plugin/Interfaces/IDalamudPluginInterface）：确认 `GetIpcProvider<T>` 和 `GetIpcSubscriber<T>` 的完整泛型签名（T1..T8, TRet）
- **HIGH** — `ICallGateProvider` / `ICallGateSubscriber` API 文档（dalamud.dev/api/Dalamud.Plugin.Ipc）：确认 `RegisterFunc`、`InvokeFunc`、`SendMessage`、`Subscribe`、`HasFunction` 方法
- **HIGH** — `ICallGateSubscriber<T1..T8, TRet>` 各泛型重载文档：确认最多支持 8 个参数 + 返回值
- **HIGH** — `IChatGui` API 文档（dalamud.dev/api/Dalamud.Plugin.Services/Interfaces/IChatGui）：确认 `Print` 方法和 `ChatMessage` 事件
- **HIGH** — `ICommandManager` API 文档（dalamud.dev/api/Dalamud.Plugin.Services/Interfaces/ICommandManager）：确认 `AddHandler` / `RemoveHandler` 仅处理自定义命令
- **HIGH** — 项目源码分析：`UnsafeInvokePluginIpcOperation.cs` 已实现反射 IPC 调用，`PluginIpcGateway` 和 `ReflectionPluginCallGateSubscriber` 内部抽象层
- **HIGH** — 项目源码分析：`OperationProtocolDispatcher.cs` 确认命名管道协议层的请求-响应模型
- **HIGH** — 项目源码分析：`PluginMcpServerController.cs` 确认 MCP HTTP 服务器启动/探测机制
- **HIGH** — `ModelContextProtocol` NuGet v1.1.0 确认 `McpServer.SendNotificationAsync` 支持自定义通知
- **MEDIUM** — `PluginLoadReason` 枚举确认 `Reload = 8` 值存在，验证重载是 Dalamud 官方支持的加载原因
- **MEDIUM** — `IAsyncDalamudPlugin` 确认 API 15 新接口，但 v1.1 不打算使用

---

*Stack research for v1.1 自动化测试桥接: 2026-05-01*