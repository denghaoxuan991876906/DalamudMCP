# 项目研究摘要

**项目:** DalamudMCP
**领域:** FFXIV Dalamud 插件 MCP 桥接 — 自动化测试桥接（v1.1）
**研究日期:** 2026-05-01
**置信度:** HIGH

## 执行摘要

DalamudMCP v1.1 是一个 Dalamud 插件的 MCP 桥接工具扩展，旨在为 AI 客户端提供跨插件 IPC 调用、插件重载、斜杠命令调度和数据回传四项自动化测试能力。该项目的核心洞察是：**所有新功能完全在现有技术栈内闭环实现，零新增 NuGet 依赖**。Dalamud API 15 的 `ICallGateProvider/Subscriber` 对称模型、`ICommandManager` 命令派发、`IExposedPlugin.Reload()` 重载机制以及 ModelContextProtocol v1.1.0 的通知能力，已经覆盖了全部功能需求。

推荐方案是将四项新功能全部映射为 `[Operation]` 属性类，复用现有源生成器注册管道和命名管道协议层。关键架构决策是：**数据回传采用轮询模式（AI 主动 `plugin_data_poll`）而非 MCP Notification 推送**，这避免了修改协议层和 MCP 服务托管层，保持了架构一致性。构建顺序应遵循依赖链：先提取共享 IPC 网关基础设施，再按复杂度递增实现插件重载→斜杠命令→安全 IPC 调用→数据回传。

核心风险集中在五个方面：IPC 类型擦除与"无 SDK 依赖"约束的矛盾（需用基元类型信封模式）、插件重载后 IPC 通道断裂的级联失败（需监听生命周期事件自动退订）、Framework 线程亲和性违反导致游戏崩溃（所有新操作必须 `RunOnFrameworkThread`）、数据回传无界队列的内存泄漏（需有界 `Channel<T>`）、以及 `PluginOperationExposurePolicy` 硬编码分类的维护性问题（需扩展为属性驱动分类）。

## 关键发现

### 推荐技术栈

零新增依赖。所有功能基于现有 Dalamud API 15 和 ModelContextProtocol v1.1.0 SDK 构建。关键能力包括：`ICallGateProvider/Subscriber` 用于跨插件 IPC 通信、`IExposedPlugin.Reload()` 用于插件重载、`ICommandManager.ProcessCommand()` 用于斜杠命令派发、`IFramework.RunOnFrameworkThread()` 用于线程封送、`McpServer.SendNotificationAsync` 用于 MCP 通知推送（备选方案）。

**核心技术：**
- **Dalamud IPC CallGate** — 跨插件 RPC 和事件通信的对称模型，支持 `InvokeFunc`/`Subscribe`/`SendMessage` 三种模式
- **源生成器 `[Operation]` 属性模式** — 新操作通过属性声明自动注册到 MCP/CLI/协议层，零手动注册代码
- **`IExposedPlugin.Reload()`** — Dalamud 公开 API 中标准的插件重载方式，需在 Framework 线程执行
- **`ICommandManager.ProcessCommand()`** — 仅支持 Dalamud 注册命令的路由，不支持游戏原生命令
- **有界 `Channel<T>`** — 数据回传缓冲策略，防止内存无限增长

### 预期特性

**必须有（核心能力）：**
- **跨插件 IPC 调用（安全版）** — AI 通过结构化接口调用目标插件的 IPC 方法，已有 `unsafe.invoke.plugin-ipc` 底层逃生舱，v1.1 新增约定式安全版本
- **数据回传（IPC → MCP）** — 目标插件通过 IPC SendMessage 推送数据，DalamudMCP 中继到 AI 客户端，完成自动化测试闭环
- **插件重载** — AI 触发被测插件重载以重置状态，通过 `IExposedPlugin.Reload()` 在 Framework 线程执行
- **斜杠命令调度** — AI 通过 `ICommandManager.ProcessCommand()` 派发 Dalamud 注册命令

**应该有（差异化）：**
- **约定式 IPC 接口注册中心** — 被测插件按 `{Name}.MCP.{Action}` 命名约定暴露 IPC 接口，零 SDK 依赖
- **细粒度 IPC 错误分类** — 区分 `ipc_missing`/`ipc_not_ready`/`ipc_type_mismatch`/`ipc_plugin_error` 等状态

**推迟到 v2+：**
- **IPC 事件 → MCP Notification 自动桥接** — 依赖链最长（需数据回传基础 + MCP 服务层修改），推迟到 MVP 稳定后
- **插件自动发现** — PROJECT.md 明确 Out of Scope，v1.1 阶段 AI 需预先知道 callgate 名称

### 架构方法

所有四项新功能映射为 `[Operation]` 类，复用现有 `OperationProtocolDispatcher → GeneratedOperationInvoker → ExecuteAsync` 请求-响应管道。唯一的架构扩展是数据回传需要的 **推送通道**，通过 `PluginIpcDataRelayService`（有界 Channel 缓冲 + 轮询操作）解决，不修改协议层。关键重构是将 `IPluginIpcGateway/IPluginCallGateSubscriber` 从 `UnsafeInvokePluginIpcOperation` 内部提取为共享单例服务。

**主要组件：**
1. **`IPluginIpcGateway`（提取）** — IPC 网关抽象，从内部类提取为共享服务，供所有跨插件操作注入使用
2. **`PluginIpcDataRelayService`（新增）** — 数据中继缓冲服务，订阅 IPC 事件并缓冲到有界 Channel，供轮询操作读取
3. **六个新 Operation 类** — `ReloadPluginOperation`、`SlashCommandOperation`、`InvokePluginIpcOperation`、`PluginDataSubscribeOperation`、`PluginDataPollOperation`、`PluginDataUnsubscribeOperation`

### 关键陷阱

1. **IPC 类型擦除 vs 无 SDK 依赖** — 目标插件可能使用自定义类型参数，但跨 AppDomain 无法加载对方类型。**防范：** 只支持基元类型和 JSON 字符串信封，文档明确约定
2. **插件重载后 IPC 通道级联断裂** — 旧 subscriber 引用变成僵尸，新插件实例不会通知旧订阅者。**防范：** 监听插件生命周期事件自动退订，所有 IPC 调用包裹 try-catch 返回结构化错误
3. **Framework 线程亲和性违反** — 在错误线程执行 IPC 调用可导致游戏崩溃。**防范：** 所有跨插件操作默认 `RunOnFrameworkThread`，遵循 `UnsafeInvokePluginIpcOperation` 已有模式
4. **数据回传无界缓冲区** — 高频 IPC 事件导致内存持续增长。**防范：** 使用有界 `Channel<T>` + 丢弃旧数据策略
5. **PluginOperationExposurePolicy 硬编码分类** — 新增操作时容易遗漏分类。**防范：** 为 `[Operation]` 属性添加分类元数据，源生成器自动生成分类

## 路线图影响

基于研究，建议以下阶段结构：

### Phase 1: IPC 基础设施提取
**理由:** 所有跨插件功能都依赖 IPC 网关抽象，必须先提取才能并行开发其他操作
**交付:** 从 `UnsafeInvokePluginIpcOperation` 提取 `IPluginIpcGateway` / `IPluginCallGateSubscriber` 为共享单例服务，注册到 DI 容器，确保现有功能回归通过
**涉及特性:** 跨插件 IPC 调用的前置条件
**避免陷阱:** Pitfall 9（源生成器模式）、Pitfall 10（硬编码分类）—— 在此阶段扩展 `OperationCategory` 属性体系
**需要研究:** 否——模式清晰，已由 `UnsafeInvokePluginIpcOperation` 验证

### Phase 2: 插件重载操作
**理由:** 最简单的跨插件功能（Low-Medium 复杂度），可立即验证新操作模式和线程封送模式
**交付:** `ReloadPluginOperation` + 细粒度错误响应（`reload_initiated`/`ipc_missing`/`ipc_reloading`）+ MCP 工具描述中的等待建议
**涉及特性:** 插件重载
**避免陷阱:** Pitfall 2（IPC 断裂级联）—— 重载后 IPC 通道处理、Pitfall 7（缺乏完成信号）—— 结构化状态返回 + IsReady 轮询建议
**需要研究:** 是——需确认 `IExposedPlugin.Reload()` 的运行时行为和线程要求

### Phase 3: 斜杠命令调度
**理由:** 功能独立且简单（Low 复杂度），与插件重载共享 Framework 线程封送模式
**交付:** `SlashCommandOperation` + 输入验证（命令格式/长度/特殊字符过滤）+ fire-and-forget 模式
**涉及特性:** 斜杠命令调度
**避免陷阱:** Pitfall 4（Framework 线程阻塞）—— 异步 fire-and-forget 设计、Pitfall 12（注入风险）—— 输入过滤
**需要研究:** 否——`ICommandManager.ProcessCommand()` 是已验证的公开 API

### Phase 4: 安全 IPC 调用
**理由:** 依赖 Phase 1 的共享 IPC 网关，是约定式 IPC 接口的基础
**交付:** `InvokePluginIpcOperation`（安全版本）+ 细粒度 IPC 错误分类体系 + IPC 命名约定文档
**涉及特性:** 跨插件 IPC 调用（安全版）、约定式 IPC 接口注册中心
**避免陷阱:** Pitfall 1（类型擦除）—— 基元类型信封模式、Pitfall 3（线程亲和性）—— 默认 RunOnFrameworkThread、Pitfall 5（约定矛盾）—— 先定义约定再实现、Pitfall 8（异常分类粗糙）—— 扩展错误码
**需要研究:** 是——`ICallGateSubscriber` 泛型参数限制需运行时验证，需确认基元类型信封在所有场景下的可行性

### Phase 5: 数据回传
**理由:** 最复杂的功能（Medium-High），依赖 IPC 网关（Phase 1）+ IPC 调用基础设施（Phase 4）+ 订阅模式
**交付:** `PluginIpcDataRelayService` + `PluginDataSubscribeOperation` + `PluginDataPollOperation` + `PluginDataUnsubscribeOperation` + 插件生命周期监听
**涉及特性:** 数据回传（IPC → MCP）
**避免陷阱:** Pitfall 2（生命周期退订）—— 监听插件卸载事件、Pitfall 6（连接管理）—— 专门的订阅管理器、Pitfall 11（背压）—— 有界 Channel + 丢弃策略
**需要研究:** 是——`ICallGateSubscriber.Subscribe` 的运行时行为和泛型限制需验证

### 阶段排序理由

- **依赖链驱动：** Phase 1 是其他所有跨插件功能的前置条件（IPC 网关提取）
- **复杂度递增：** 重载（简单）→ 斜杠命令（简单）→ IPC 调用（中等）→ 数据回传（复杂），降低风险
- **陷阱隔离：** 每个阶段有明确的陷阱关注点，不会相互干扰
- **线程封送模式在 Phase 2 建立：** 重载操作最适合建立 `RunOnFrameworkThread` 默认封送模式，后续阶段复用

### 研究标记

**需要深入研究（`/gsd research-phase`）：**
- **Phase 2:** `IExposedPlugin.Reload()` 的运行时行为、Framework 线程要求的精确边界
- **Phase 4:** `ICallGateSubscriber<T1..T8, TRet>` 泛型参数的运行时限制、基元类型信封在 MessagePack 序列化下的行为
- **Phase 5:** `ICallGateSubscriber.Subscribe` 回调线程模型、IPC 事件订阅的泛型签名约束

**标准模式（跳过研究）：**
- **Phase 1:** 模式清晰，代码提取重构，已由现有代码验证
- **Phase 3:** `ICommandManager.ProcessCommand()` 是简单公开 API，无需研究

## 置信度评估

| 领域 | 置信度 | 备注 |
|------|--------|------|
| 技术栈 | HIGH | 零新增依赖，所有 API 均有官方文档和代码库验证 |
| 特性 | HIGH | Dalamud IPC CallGate 模型和项目需求明确，已验证可行性 |
| 架构 | HIGH | 所有新功能映射为 [Operation] 类，源生成器模式已验证，仅数据回传需架构扩展（有先例） |
| 陷阱 | HIGH | 代码库审计+官方文档双验证，5 个 Critical 陷阱均有明确防范策略 |

**整体置信度:** HIGH

### 待解决的差距

- **IPC 泛型签名限制：** `ICallGateSubscriber` 最多支持 8 个泛型参数 + 1 个返回值，但运行时构造泛型实例时的类型加载行为需在 Phase 4 实现时验证
- **插件重载完成信号：** Dalamud 没有公开的"插件重载完成"事件，AI 端只能通过轮询 IPC 通道就绪状态确认——这在 Phase 2 需要详细设计
- **数据回传 IPC 订阅线程模型：** `ICallGateSubscriber.Subscribe` 的回调线程是目标插件线程，需确认是否需要 Framework 线程封送——Phase 5 实现时需测试
- **`ICommandManager` vs `IChatGui` 精确行为：** 斜杠命令通过 ICommandManager 还是 IChatGui 执行在边界情况（如 `/xlreload` 是否可通过 ICommandManager 派发）需 Phase 3 运行时确认

## 来源

### 主要来源（HIGH 置信度）
- Dalamud 官方 API 文档（dalamud.dev）— `ICallGateProvider/Subscriber`、`ICommandManager`、`IChatGui`、`IFramework`、`IExposedPlugin` 接口验证
- DalamudMCP v1.0 源码审计 — `UnsafeInvokePluginIpcOperation`、`OperationProtocolDispatcher`、源生成器模式、`PluginMcpServerController`
- ModelContextProtocol v1.1.0 NuGet — `SendNotificationAsync` 能力确认
- PROJECT.md — 项目约束（无 SDK 依赖、不自动等待就绪、不实现插件自动发现）

### 次要来源（MEDIUM 置信度）
- Dalamud PluginLoadReason 枚举验证 — `Reload = 8` 值存在，重载是官方支持的加载原因
- Dalamud IPC 示例代码和社区模式 — 订阅模式、消息推送的使用方式
- `ICommandManager` API 行为 — 仅支持 Dalamud 注册命令，不支持游戏原生命令

### 待验证（需运行时确认）
- `IExposedPlugin.Reload()` 在 Framework 线程上的精确行为和完成时机
- `ICallGateSubscriber.Subscribe` 回调的线程上下文
- `/xlreload` 是否可通过 `ICommandManager.ProcessCommand()` 派发（研究建议是使用 `IChatGui.Print`）
- IPC 泛型参数在跨 AppDomain 反射构造时的类型加载行为

---
*研究完成：2026-05-01*
*路线图就绪：是*