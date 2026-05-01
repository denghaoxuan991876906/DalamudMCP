# Feature Landscape

**Domain:** Dalamud MCP 桥接 — 自动化测试桥接（v1.1）
**Researched:** 2025-05-01

## Table Stakes

缺失即"不完整"的基础功能。用户（AI 客户端）期望通过 MCP 与其他 Dalamud 插件交互时，这些功能必须存在。

| Feature | Why Expected | Complexity | Dependencies | Notes |
|---------|-------------|------------|--------------|-------|
| **跨插件 IPC 调用（安全版）** | 已有 `unsafe.invoke.plugin-ipc` 作为底层逃生舱，但 AI 客户端需要结构化的、带约定接口的 IPC 调用方式——不需要手动指定类型参数 | Medium | 现有 `UnsafeInvokePluginIpcOperation`、Dalamud IPC `GetIpcSubscriber`/`GetIpcProvider` | v1.0 已有反射式不安全 IPC 调用；v1.1 需要在其基础上增加约定式调用。被测插件只需实现 Dalamud 标准 IPC 接口约定 |
| **数据回传（IPC 通知 → MCP）** | 自动化测试需要接收被测插件主动推送的数据（状态变更、测试结果、事件通知），否则 AI 只能轮询 | Medium-High | 跨插件 IPC Subscribe 机制、命名管道协议层现有分发器、MCP Server 通知能力 | Dalamud IPC 的 `SendMessage` → Subscribe 模式是推动式（push），但 MCP stdio 是请求-响应式。需要桥接异步通知到 MCP |
| **插件重载** | 测试流程中 AI 需要重置被测插件状态（加载新配置、重新初始化），这是测试循环的核心能力 | Low-Medium | `IPluginManager`（Dalamud 内部 API，无公开接口）、或通过 Dalamud 斜杠命令 `/xlreload` 间接触发 | 警告：Dalamud 没有暴露 `IPluginManager` 作为公开服务接口。需要研究替代方案 |
| **斜杠命令调度** | AI 需要触发游戏内命令（如 `/echo test`、`/xivalexandria` 等）来驱动被测插件功能 | Low | `ICommandManager.ProcessCommand`（仅限 Dalamud 注册命令）、或原生聊天输入注入 | `ICommandManager.ProcessCommand` 只能派发 Dalamud 注册的斜杠命令，不能发送原生游戏命令（如 `/wave`）。原生聊天命令需要内存交互 |

## Differentiators

让 DalamudMCP v1.1 超越"又一个 IPC 桥"，真正成为自动化测试核心的差异化功能。

| Feature | Value Proposition | Complexity | Dependencies | Notes |
|---------|-------------------|------------|--------------|-------|
| **约定式 IPC 接口注册中心** | 被测插件只需实现特定命名约定的 IPC callgate（如 `PluginName.MCP.Action`、`PluginName.MCP.Query`、`PluginName.MCP.Event`），DalamudMCP 自动发现并暴露为 MCP 工具——零 SDK 依赖 | High | 现有操作发现机制、源生成器、Dalamud IPC callgate 命名约定 | 这是 v1.1 的核心技术亮点。被测插件不引入任何 SDK，只需按约定暴露 IPC 接口 |
| **IPC 事件 → MCP 通知桥接** | 将 Dalamud IPC 的 Subscribe 事件自动桥接到 MCP 的 notification 机制，AI 客户端可以实时收到被测插件的状态推送 | High | MCP Streamable HTTP 的 SSE 通知能力、或 MCP stdio 的 notification 消息 | MCP 2025-03-26 协议支持 server → client 通知。需要在 Streamable HTTP 模式下实现 |
| **插件生命周期感知** | MCP 工具能查询已安装插件列表、插件状态（已加载/已卸载），甚至感知插件加载/卸载事件 | Medium | `PluginListInvalidationKind` 事件（Loaded/Unloaded）、`ILocalPluginManifest` 属性 | 介于 table stakes 和 differentiator 之间——"插件自动发现"在 PROJECT.md 中是 Out of Scope，但查询能力是基础必要 |

## Anti-Features

明确不做的功能，以及替代方案。

| Anti-Feature | Why Avoid | What to Do Instead |
|--------------|-----------|-------------------|
| **提供 SDK/NuGet 包** | PROJECT.md 明确排除。被测插件不应依赖额外包——这会提高接入门槛、引入版本耦合 | 被测插件只需按命名约定暴露 Dalamud 标准 IPC callgate（如 `MyPlugin.MCP.Execute`），DalamudMCP 通过约定发现 |
| **批量测试场景执行** | PROJECT.md 明确排除 v1.1。单步交互式测试已足够验证功能 | AI 客户端自行编排多步场景，MCP 每次调用是单步原子操作 |
| **插件自动发现（列出已安装插件及 IPC 接口）** | PROJECT.md 明确排除——作为后续里程碑。v1.1 阶段 AI 需要知道 callgate 名称才能调用 | v1.1 由用户/AI 提供 callgate 名称；后续版本增加枚举能力 |
| **原生游戏聊天命令注入** | 通过内存修改游戏聊天输入风险极高（可能触发反作弊检测），且不同游戏版本维护成本大 | 使用 `ICommandManager.ProcessCommand` 派发 Dalamud 注册命令；原生游戏命令可通过现有 IPC 让被测插件自行触发 |
| **持久化测试状态** | 跨会话持久化测试结果不是 MCP 桥的职责 | 每次会话是无状态的，AI 客户端自行管理测试状态 |

## Feature Dependencies

```
插件重载 ──────────────────────────────────────→ 独立实现（可能通过 /xlreload 斜杠命令）
斜杠命令调度 ──────────────────────────────────→ 独立实现（依赖 ICommandManager）
约定式 IPC 接口注册中心 ────────────────────────→ 依赖现有 UnsafeInvokePluginIpcOperation 改造
跨插件 IPC 调用（安全版） ←──── 依赖 ─────────── 约定式 IPC 接口注册中心
数据回传（IPC → MCP 通知） ←──── 依赖 ────────── 跨插件 IPC 调用基础设施
                                                          ↓
IPC 事件 → MCP 通知桥接 ←──── 进一步依赖 ───── 数据回传基础 + MCP notification 支持
```

**依赖链说明：**
1. **插件重载**和**斜杠命令调度**是独立功能，无交叉依赖，可以最先实现
2. **约定式 IPC 接口注册中心**是核心基础设施，其他 IPC 功能都依赖它
3. **数据回传**依赖 IPC 订阅机制，而 IPC 通知桥接又依赖数据回传的基础设施

## MVP Recommendation

**优先级排序：**

1. **斜杠命令调度**（Low 复杂度，零依赖）— 最容易实现，立即有实用价值
2. **插件重载**（Low-Medium 复杂度，零依赖）— 测试循环核心能力
3. **约定式 IPC 接口注册中心**（High 复杂度，但是一切 IPC 的基础）— 核心差异化功能
4. **跨插件 IPC 调用（安全版）**（Medium 复杂度，依赖 #3）— 在注册中心之上构建
5. **数据回传（IPC → MCP）**（Medium-High 复杂度，依赖 #3+4）— 完成自动化测试闭环
6. **IPC 事件 → MCP 通知桥接**（High 复杂度，依赖 #5）— 锦上添花，可推迟

**Defer:**
- 事件通知桥接（#6）：虽然价值高，但依赖链最长，可在 MVP 稳定后迭代
- 插件列表查询：v1.1 MVP 阶段 AI 通过 callgate 名称调用即可

## Dalamud IPC 模式深度分析

### IPC CallGate 模型

Dalamud IPC 基于 `ICallGateProvider<T1..T8, TRet>` 和 `ICallGateSubscriber<T1..T8, TRet>` 对称设计：

| 角色 | 模式 | API | 用途 |
|------|------|-----|------|
| **Provider（服务端）** | 注册函数 | `RegisterFunc(Func<..., TRet>)` | 暴露 RPC 方法供其他插件调用 |
| **Provider（服务端）** | 注册动作 | `RegisterAction(Action<...>)` | 暴露 RPC 动作（无返回值） |
| **Provider（服务端）** | 推送消息 | `SendMessage(T1, T2, ...)` | 向所有订阅者广播事件 |
| **Subscriber（客户端）** | 调用函数 | `InvokeFunc(T1, T2, ...) → TRet` | 调用 Provider 注册的函数 |
| **Subscriber（客户端）** | 调用动作 | `InvokeAction(T1, T2, ...)` | 调用 Provider 注册的动作 |
| **Subscriber（客户端）** | 订阅事件 | `Subscribe(Action<...>)` | 接收 Provider 推送的消息 |

**关键特点：**
- 泛型参数最多 8 个参数 + 1 个返回值类型
- 复杂类型通过 MessagePack 序列化传递
- 原生类型（int, bool, string 等）直接传递
- 所有调用在同一线程上执行（调用者线程）
- `HasFunction` / `HasAction` 属性检查目标是否可用
- `IpcNotReadyError` 当目标插件未注册时抛出

### 现有不安全 IPC 调用的限制

`UnsafeInvokePluginIpcOperation` 使用反射动态构造 `GetIpcSubscriber` 调用：

| 限制 | 说明 |
|------|------|
| 类型不安全 | AI 必须手动指定 `result-kind` 和 `argument-kinds`，容易出错 |
| 无约定 | 被测插件无法声明"我暴露了哪些 IPC 接口"，AI 必须知道精确的 callgate 名称和类型签名 |
| 仅支持函数调用 | 不支持 Subscribe 模式（数据回传） |
| 仅支持基元类型 | 不支持 MessagePack 序列化的复杂类型 |
| 无错误恢复 | `IpcNotReadyError` 直接作为错误返回，AI 无法区分"插件未加载"和"接口不存在" |

### 约定式 IPC 接口设计建议

被测插件暴露的 callgate 命名约定：

```
{PluginInternalName}.MCP.{Action}
```

其中 `Action` 采用以下标准后缀：

| 后缀 | 含义 | 方向 | 示例 |
|------|------|------|------|
| `Execute` | 执行操作 | AI → Plugin | `MyPlugin.MCP.Execute` |
| `Query` | 查询状态 | AI → Plugin | `MyPlugin.MCP.Query` |
| `Event` | 事件通知 | Plugin → AI（Subscribe） | `MyPlugin.MCP.Event` |

**设计原理：**
- 被测插件使用标准 Dalamud IPC API（`GetIpcProvider`），无需任何额外依赖
- DalamudMCP 在插件端通过 `GetIpcSubscriber` 发现并连接，AI 客户端无需知道底层类型签名
- callgate 名称约定使自动发现成为可能（扫描 `{Name}.MCP.*` 模式）

### 插件重载方案分析

| 方案 | 可行性 | 风险 | 建议 |
|------|--------|------|------|
| **A: `/xlreload {name}` 斜杠命令** | ✅ 可行（通过 `ICommandManager.ProcessCommand`） | 依赖 Dalamud 内置命令，命令格式可能变化 | ✅ 推荐：最简单、最安全 |
| **B: 通过 `IDalamudPluginInterface` 的 IPC** | ❌ 不存在公开 API | `IPluginManager` 是内部接口，不在公开服务中 | ❌ 不推荐 |
| **C: 游戏原生 `/xlreload` 注入** | ⚠️ 可能可行 | 需要内存交互，风险高 | ❌ 不推荐 |

**推荐方案 A：** `ICommandManager.ProcessCommand("/xlreload " + pluginName)` 是最可靠的方式。

### 斜杠命令调度分析

| 命令类型 | 方法 | 限制 |
|----------|------|------|
| **Dalamud 注册命令**（`/xl*`、`/ping`、插件自定义） | `ICommandManager.ProcessCommand(string)` | 只能派发 Dalamud 注册的命令，返回 `bool` 表示是否成功 |
| **游戏原生命令**（`/wave`、`/dance`、游戏文本聊天） | 无公开 API | 需要内存注入，不在 v1.1 范围内 |

**v1.1 策略：** 仅支持 Dalamud 注册命令通过 `ICommandManager.ProcessCommand`。游戏原生命令作为后续考量。

## Sources

- Dalamud 官方 API 文档（dalamud.dev）：IPC CallGate、ICommandManager、IChatGui、PluginLoadReason 等接口验证 — **HIGH confidence**
- DalamudMCP v1.0 源码分析：UnsafeInvokePluginIpcOperation、OperationAttribute 模式、Protocol 层 — **HIGH confidence**
- `IPluginManager` 不作为公开服务暴露：基于 Dalamud 官方 API 文档未列出此接口 — **MEDIUM confidence**（可能存在非公开途径）
- `/xlreload` 斜杠命令行为：基于 Dalamud 社区已知行为 — **MEDIUM confidence**（需验证 ProcessCommand 是否能派发此命令）