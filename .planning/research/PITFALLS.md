# Domain Pitfalls: DalamudMCP v1.1 自动化测试桥接

**Domain:** 跨插件 IPC 桥接、插件重载、斜杠命令调度（添加到现有 Dalamud 插件）
**Researched:** 2026-05-01
**Confidence:** HIGH（代码库审计 + Dalamud 官方文档 + 已有 pitfall 研究基础）

## Critical Pitfalls

可能导致重写或严重运行时故障的错误。

---

### Pitfall 1: IPC 调用网关的类型擦除导致数据丢失或反序列化失败

**What goes wrong:**
Dalamud 的 `GetIpcSubscriber<T1,...,T8,TRet>` 是强类型的泛型 API——调用者必须精确知道参数数量和类型。但 PROJECT.md 明确约束「被测插件不引入额外 SDK 依赖」，这意味着目标插件的 IPC 通道签名对 MCP 是"约定式"的而非"编译式"的。现有的 `UnsafeInvokePluginIpcOperation` 用反射绕过了泛型约束（通过 `MakeGenericMethod` 动态构造），但新功能如果要暴露更友好的 IPC 接口，必须在运行时根据约定推断类型——任何类型不匹配都会导致 `TargetInvocationException` 或静默返回错误类型。

**Why it happens:**
泛型 IPC 的类型参数在编译时确定。没有共享类型库意味着 MCP 只能依赖字符串描述（如现有 `result-kind`, `argument-kinds` 参数）来重建泛型。如果目标插件注册了一个 `GetIpcProvider<MyCustomType, bool>` 通道，而 MCP 只能通过 `string` 名字发现它，`MyCustomType` 无法被 MCP 的 AppDomain 解析，反射构造会失败。

**Consequences:**
- 高级类型（非基元类型如 `bool`, `int`, `string`）的 IPC 调用直接崩溃
- 返回值反序列化为 `JsonElement` 后丢失原始类型信息
- 目标插件开发者被迫只用基元类型，限制了 IPC 接口的表达能力

**Prevention:**
1. 新增的跨插件 IPC 操作应继续使用与 `UnsafeInvokePluginIpcOperation` 相同的「基元类型信封」模式——只支持 Dalamud `PluginIpcValueKind` 枚举中的类型
2. 对复杂数据回传，使用 `string`（JSON）作为 IPC 返回类型，由目标插件负责序列化，MCP 侧只做透传
3. 文档中明确约定：跨插件 IPC 接口不得使用自定义复杂类型参数，必须使用基元类型或 JSON 字符串

**Detection:**
- `TargetInvocationException` 包含 `InnerException` 提示类型加载失败
- `GetIpcSubscriber` 返回正确的 subscriber 但 `InvokeFunc` 抛出 `MissingMethodException`
- 目标插件使用非基元类型参数时，MCP 侧无法构造正确的泛型方法

**Phase to address:** Phase 1（跨插件 IPC 调用设计）—— 在实现前必须确定类型约定

---

### Pitfall 2: 插件重载期间 IPC 通道断裂导致级联失败

**What goes wrong:**
Dalamud 插件重载时（无论通过 `/xlreload` 还是其他机制），旧插件的 IPC Provider 被注销。如果 MCP 持有对该插件 `ICallGateSubscriber` 的引用并尝试调用，会抛出 `IpcNotReadyError` 或返回 `HasFunction == false`。更严重的是，如果 MCP 订阅了目标插件的 IPC 事件（数据回传功能），插件卸载时事件订阅不会自动清理，可能导致 MCP 收集到一个「僵尸 subscriber」引用。

**Why it happens:**
Dalamud IPC 的生命周期绑定到插件实例。`ICallGateProvider` 在插件 `Dispose()` 时调用 `UnregisterFunc()` / `UnregisterAction()`。订阅端的 `ICallGateSubscriber` 引用不会自动失效，但 `HasFunction` 变为 `false`。如果 MCP 在此期间发起调用：
1. 尝试调用 `InvokeFunc` → `IpcNotReadyError`
2. 尝试调用 `InvokeAction` → `IpcNotReadyError`
3. 旧事件订阅残留 → 内存泄漏，新加载的插件实例不会通知旧订阅者

**Consequences:**
- AI 收到不明意义的 IPC 错误，无法区分「插件未安装」和「插件正在重载」
- 数据回传订阅丢失，AI 不再收到目标插件的推送数据
- 潜在内存泄漏：持有对已卸载插件类型的引用阻止 GC 回收

**Prevention:**
1. 所有跨插件 IPC 调用必须包裹在 try-catch 中，捕获 `IpcNotReadyError` 并返回结构化错误响应（区分 `ipc_missing` 和 `ipc_reloading` 两种状态）
2. 数据回传（IPC 事件订阅）模块必须监听 Dalamud 的插件生命周期事件，在目标插件卸载时自动退订
3. 实现重试策略：IPC 调用失败时，返回「插件可能正在重载」提示而非硬错误
4. PROJECT.md 已决定「重载后不自动等待就绪」—— 但必须在 MCP 工具描述中明确告知 AI 客户端需要轮询等待

**Detection:**
- `HasFunction` 返回 `false` 但之前返回过 `true`
- `IpcNotReadyError` 异常出现
- 重载后旧事件订阅不再收到通知，但无报错

**Phase to address:** Phase 2（插件重载操作）和 Phase 3（数据回传）—— 两个功能都需要处理此生命周期问题

---

### Pitfall 3: Framework 线程亲和性违反导致游戏崩溃

**What goes wrong:**
Dalamud 的游戏内 API（包括 IPC 调用内部的某些操作）只能在 Framework 线程上执行。如果 MCP 通过命名管道收到 AI 的 IPC 调用请求，在管道 IO 线程上直接执行 IPC 调用，会触发 `InvalidOperationException` 或导致游戏内存损坏。现有的 `UnsafeInvokePluginIpcOperation` 已正确处理此问题（`RunOnFrameworkThread` 参数默认为 `true`），但新增的操作可能遗漏此处理。

**Why it happens:**
MCP 请求通过 `NamedPipeProtocolServer` 的 IO 线程到达。`OperationProtocolDispatcher.DispatchAsync` 在管道线程的 `Task.Run` 中执行。调用链最终到达 `ExecuteAsync`，而新操作的开发者可能忘记使用 `IFramework.RunOnFrameworkThread` 封装 IPC 调用。

关键代码路径：
```
NamedPipeProtocolServer.AcceptLoopAsync → Task.Run → HandleConnectionAsync → handler → DispatchAsync → ExecuteAsync
```
整个链路默认在非 Framework 线程上运行。

**Consequences:**
- 操作在不同线程访问 Dalamud 服务，导致 `InvalidOperationException`
- 更严重的：在错误线程修改游戏状态可能导致游戏崩溃
- 间歇性故障——某些插件恰好不检查线程，某些严格检查

**Prevention:**
1. 新增的跨插件 IPC 操作必须继承 `UnsafeInvokePluginIpcOperation` 的模式：提供 `RunOnFrameworkThread` 选项并默认启用
2. 在 `OperationProtocolDispatcher` 或操作基类层面添加默认线程封送逻辑
3. 所有需要访问 Dalamud API 的新操作都应注入 `IFramework` 并使用 `RunOnFrameworkThread` 或检查 `IsInFrameworkUpdateThread`
4. 为新增操作添加自动化的线程检查测试

**Detection:**
- `InvalidOperationException` 提及 "framework thread" 或 "game thread"
- 游戏随机崩溃且调用栈包含 MCP 操作
- 操作在测试中通过但首次 AI 调用时失败

**Phase to address:** Phase 1（跨插件 IPC 调用设计）—— 必须在首批操作中建立线程封送模式

---

### Pitfall 4: 斜杠命令调度阻塞 Framework 线程

**What goes wrong:**
通过 `ICommandManager` 或 `IChatGui` 发送斜杠命令是同步操作，执行在 Framework 线程上。如果目标插件在处理该命令时显示对话框、等待用户输入或执行长时间操作，Framework 线程被阻塞，导致：
1. 游戏画面冻结
2. MCP 的命名管道超时（默认 5 秒）
3. 其他排队的 MCP 操作无法处理

**Why it happens:**
Dalamud 斜杠命令处理器运行在游戏主线程上。`/xlreload` 等命令本身就是同步的。当 MCP 通过新操作发送斜杠命令时，如果用 `RunOnFrameworkThread` 封装，命令执行期间游戏主线程被占用。

但更隐蔽的是：某些命令（如 `/xlreload`）会触发插件卸载/重载，这可能间接触发 MCP 自身的 IPC 断裂（Pitfall 2），形成级联故障。

**Consequences:**
- 游戏卡顿 1-5 秒（取决于命令执行时间）
- MCP 管道连接超时断开
- 如果命令触发了插件卸载，MCP 的 IPC 操作链路断裂

**Prevention:**
1. 斜杠命令调度操作必须是异步的——发送命令后立即返回，不等待命令执行完成
2. 为斜杠命令操作设置 Framework 线程执行超时（如 2 秒），超时后返回「命令已发送但执行状态未知」
3. 明确在 MCP 工具描述中告知 AI：斜杠命令是发后即忘（fire-and-forget），AI 应通过后续观察操作确认结果
4. 避免在 MCP 操作中等待斜杠命令的副作用完成

**Detection:**
- 游戏短暂冻结后 MCP 连接断开
- `NamedPipeProtocolServer` 的 5 秒空闲超时触发
- AI 客户端报告 MCP 连接超时

**Phase to address:** Phase 4（斜杠命令调度）—— 必须设计为异步 fire-and-forget

---

### Pitfall 5: 项目约束「无 SDK 依赖」与 IPC 注册发现机制矛盾

**What goes wrong:**
PROJECT.md 规定「被测插件不引入额外 SDK 依赖，仅实现 IPC 接口约定」。但 Dalamud IPC 没有内置的服务发现机制——`GetIpcSubscriber` 需要知道准确的字符串名称才能获取 subscriber。没有共享的常量类或接口，不同插件开发者可能：
1. 使用不同的命名约定（如 `MyPlugin.GetData` vs `myplugin.get_data`）
2. 对同一功能使用不同的参数类型签名
3. 根本不知道应该注册哪些 IPC 通道来与 MCP 兼容

**Why it happens:**
Dalamud IPC 的设计哲学是「插件间私下约定」，适合两个已知插件之间的点对点通信。DalamudMCP 要做的是「任意插件通过约定暴露测试接口给 MCP」，这是一个一对多的多播场景，需要比 IPC 命名更正式的约定。

**Consequences:**
- 不同作者的目标插件实现不同的 IPC 接口命名，MCP 无法统一调用
- MCP 必须为每个已知插件硬编码 IPC 通道名称，违背「通用桥接」目标
- 目标插件开发者不知道应注册哪些通道，集成门槛高

**Prevention:**
1. 定义清晰的 IPC 接口约定文档，包含：
   - 通道命名格式：`<PluginInternalName>.<Action>`（如 `PingPlugin.Ping`, `PingPlugin.GetData`）
   - 参数类型限定：只使用基元类型或 `string`（JSON）
   - 必需通道：`<PluginInternalName>.IsReady` 返回 `bool`，表示插件是否准备好被测试
2. 在 MCP 侧实现约定格式的 IPC 通道名拼装逻辑，而非硬编码特定插件名
3. 提供 INTEGRATION.md 中的接口约定模板，目标插件开发者直接复制实现
4. 考虑实现一个通用的「MCP 桥接约定注册」通道：目标插件注册 `<PluginInternalName>._mcp.Register`，MCP 订阅后获取该插件支持的通道列表

**Detection:**
- 目标插件开发者反映「不知道注册什么通道」
- MCP 调用已知插件名但 IPC 通道不存在
- 不同版本的同一插件注册了不同名称的通道

**Phase to address:** Phase 1（跨插件 IPC 调用设计）—— 必须在代码前先定义约定

---

## Moderate Pitfalls

可能导致功能降级或不一致行为的错误。

---

### Pitfall 6: 数据回传（IPC 事件推送）的连接管理复杂度

**What goes wrong:**
数据回传要求目标插件通过 `ICallGateProvider.SendMessage()` 主动推送数据，MCP 通过 `ICallGateSubscriber.Subscribe()` 接收。这引入了有状态连接管理：
1. MCP 必须在启动时扫描所有已知插件并订阅其数据通道
2. 新加载的插件不会被自动发现（插件发现功能在 Out of Scope 中）
3. 插件重载后必须重新订阅
4. 订阅回调在插件线程而非 MCP 线程执行，需要线程封送

**Prevention:**
1. 实现一个 IPC 订阅管理器，监听插件加载/卸载事件
2. 使用 Dalamud 的 `IPoetState`（如可用）或 `IClientState.TerritoryChanged` 等事件作为替代触发条件
3. 数据回传通道应使用 `ICallGateProvider<string>` 类型（JSON 字符串），避免类型加载问题
4. 订阅回调必须使用 `IFramework.RunOnFrameworkThread` 或通过 `ConcurrentQueue` + 轮询模式进入 MCP 管道

**Phase to address:** Phase 3（数据回传）—— 需要专门的订阅管理器

---

### Pitfall 7: 插件重载操作缺乏可靠的完成信号

**What goes wrong:**
PROJECT.md 决定「重载后不自动等待就绪」。但 AI 客户端需要知道重载何时完成才能继续测试。如果 AI 只是发送重载命令后立即执行下一步操作，很可能调用了尚未重载完成的插件的 IPC 通道，得到 `IpcNotReadyError`。

现有代码中 `PluginMcpServerController` 有 `WaitForAvailability` 轮询（20 次 × 100ms = 2 秒），但那是检查 MCP 自身的 HTTP 端点可用性，不是检查目标插件的重载状态。

**Prevention:**
1. 插件重载操作返回结构化状态：`{ "status": "reload_initiated", "plugin": "PluginName", "note": "AI should poll plugin readiness via IPC before proceeding" }`
2. 提供一个 `check_plugin_ready` 操作，AI 可用来轮询目标插件的 `IsReady` 通道
3. 在 MCP 工具描述中明确建议 AI 在重载后等待 2-5 秒再验证
4. 检测 `IpcNotReadyError` 并转化为用户友好的「插件尚未就绪，请稍后重试」消息

**Phase to address:** Phase 2（插件重载操作）—— 返回结果设计

---

### Pitfall 8: 跨插件 IPC 调用的异常处理过于粗糙

**What goes wrong:**
现有 `UnsafeInvokePluginIpcOperation` 只区分了三种结果：`ipc_missing`（通道不存在）、`ipc_error`（调用异常）和成功。但当跨插件 IPC 用于自动化测试时，AI 需要更细粒度的错误信息来决定下一步：
- 「插件未安装」vs 「插件正在重载」vs 「IPC 版本不兼容」vs 「参数类型不匹配」
- 「目标插件内部异常」vs 「IPC 超时」
- 「框架线程阻塞」vs 「权限不足」

所有这些都映射到同一个 `ipc_error` 原因码。

**Prevention:**
1. 扩展错误码体系：`ipc_missing`, `ipc_not_ready`, `ipc_type_mismatch`, `ipc_timeout`, `ipc_plugin_error`, `ipc_framework_blocked`
2. 在错误响应中包含目标插件名称和通道名称，帮助 AI 诊断
3. 对于 `IpcNotReadyError`，额外提供「可能原因：插件未加载或正在重载」提示
4. 对 `TargetInvocationException`，展开 `InnerException` 信息而非只返回外层消息

**Phase to address:** Phase 1（跨插件 IPC 调用设计）—— 错误分类体系

---

### Pitfall 9: 项目结构膨胀——新操作未遵循源生成器模式

**What goes wrong:**
现有 20+ 操作都通过 `[Operation]` / `[McpTool]` / `[CliCommand]` 属性 + Roslyn 源生成器自动注册。新操作如果手动注册到 DI 容器而非使用属性模式，会绕过源生成器，导致：
1. MCP 工具列表不包含新工具
2. CLI 命令列表缺失
3. 协议描述操作列表过时
4. 运行时操作分发器找不到新操作

**Prevention:**
1. 所有新操作必须使用 `[Operation]` + `[MemoryPackable]` + `[ProtocolOperation]` 属性模式
2. Request 类型必须包含 `[Option]` 或 `[Argument]` 属性用于参数绑定
3. 新操作类型必须在 `PluginServiceCollectionExtensions.BuildDalamudServiceProvider` 中通过 `AddGeneratedPluginOperations()` 自动注册
4. 不要手动调用 `services.AddSingleton<NewOperation>()`——源生成器会处理注册

**Phase to address:** 所有新操作开发阶段—— 需要代码审查确保遵循模式

---

### Pitfall 10: PluginOperationExposurePolicy 硬编码操作 ID 列表无法扩展

**What goes wrong:**
`PluginOperationExposurePolicy` 使用 `HashSet<string>` 硬编码了 `ActionOperationIds` 和 `UnsafeOperationIds`。新增的跨插件 IPC 操作需要分类（哪些是「观察」、哪些是「动作」、哪些是「非安全」），但如果操作 ID 在编译时才由源生成器生成，硬编码列表容易遗漏或错配。

```csharp
private static readonly HashSet<string> ActionOperationIds = [
    "target.object", "interact.with.target", "move.to.entity", ...
];

private static readonly HashSet<string> UnsafeOperationIds = [
    "unsafe.invoke.plugin-ipc"
];
```

新增操作时，开发者容易忘记将新操作 ID 添加到此列表。

**Prevention:**
1. 为 `[Operation]` 属性添加分类属性（如 `OperationCategory`）：`Observe`、`Action`、`Unsafe`
2. 让源生成器自动生成分类元数据，`PluginOperationExposurePolicy` 改为基于属性而非硬编码列表
3. 或者至少在 `GeneratedOperationRegistry` 中暴露每个操作的分类标签
4. 添加构建时检查：如果操作 ID 不在任何分类中，产生编译警告

**Phase to address:** Phase 1（跨插件 IPC 调用设计）—— 需要先扩展分类体系

---

### Pitfall 11: 数据回传的无界队列和背压问题

**What goes wrong:**
数据回传场景中，目标插件可能高频发送数据（如每帧位置更新、战斗日志流）。如果 MCP 的管道连接暂时断开或 AI 客户端处理速度跟不上，数据会积压在内存中。没有背压机制会导致内存持续增长。

**Why it happens:**
IPC 事件订阅的回调直接在目标插件线程执行。如果 MCP 侧将这些事件排入队列但消费者（管道写入）跟不上，队列无限增长。

**Prevention:**
1. 使用有界 `Channel<T>` 或 `ConcurrentQueue<T>` 配合丢弃策略
2. 当队列达到上限时，丢弃最旧数据或合并相似数据
3. 对数据回传操作添加最大缓冲区大小配置
4. 文档中明确说明数据回传不保证送达——如果 AI 客户端断连，旧数据可能丢失

**Phase to address:** Phase 3（数据回传）—— 架构设计时考虑

---

### Pitfall 12: 斜杠命令中的特殊字符和注入风险

**What goes wrong:**
AI 客户端可能生成包含特殊字符的斜杠命令文本（如引号、换行符、分号）。这些字符通过 `ICommandManager` 发送到游戏聊天时可能：
1. 被游戏聊天系统解释为多个命令
2. 触发游戏宏系统
3. 造成聊天窗口显示异常

此外，如果 AI 在命令参数中注入意外的 `/` 前缀，可能触发非预期的游戏命令。

**Prevention:**
1. 斜杠命令操作必须验证命令格式：以 `/` 开头，不含换行符
2. 对命令内容进行安全过滤：禁止包含行终止符和 null 字符
3. 长度限制：游戏聊天输入有字符数限制（约 500 字符），超长命令会被截断
4. 在 MCP 工具描述中明确：此操作直接发送命令到游戏，不应用于执行危险操作

**Phase to address:** Phase 4（斜杠命令调度）—— 输入验证

---

## Minor Pitfalls

可能导致 annoyance 或轻微不一致的问题。

---

### Pitfall 13: MCP 工具描述误导 AI 导致误用

**What goes wrong:**
AI 客户端依赖 MCP 工具的 `description` 和参数描述来决定如何使用工具。如果不精确描述跨插件 IPC 操作的能力和限制，AI 可能：
1. 尝试传递复杂对象给只接受基元类型的 IPC 通道
2. 在插件重载期间反复调用失败的操作
3. 将数据回传误认为是请求-响应模式

**Prevention:**
1. MCP 工具描述必须包含使用条件和限制说明
2. 对于 IPC 调用操作，明确说明只支持基元类型参数
3. 对于重载操作，明确说明返回后需轮询确认
4. 对于数据回传，明确说明是推送模式而非请求模式
5. 在错误响应中返回可操作的指导（如「请等待 3 秒后重试」）

---

### Pitfall 14: 测试框架对 Dalamud IPC 的模拟挑战

**What goes wrong:**
现有测试模式使用内部接口+构造函数注入（如 `UnsafeInvokePluginIpcOperation.IPluginIpcGateway`）。新增操作需要类似的可测试设计，但数据回传（事件订阅）和重载操作涉及生命周期回调，更难模拟。

**Prevention:**
1. 继续使用内部接口+构造函数注入模式
2. 为 Dalamud IPC 订阅行为定义 `IIpcSubscriberManager` 接口，便于 mock
3. 为插件重载定义 `IPluginReloadService` 接口，隔离 Dalamud API 依赖
4. 数据回传测试使用 `ConcurrentQueue<T>` + 超时的消费模式，避免依赖真实管道

---

### Pitfall 15: 协议版本兼容性——新增操作不影响旧客户端

**What goes wrong:**
新增的 v1.1 操作改变 `describe-operations` 返回的操作目录。如果旧版本 CLI 连接到新版本 Plugin，或新版本 CLI 连接到旧版本 Plugin，操作目录不匹配导致工具不可用或报错。

**Prevention:**
1. `ProtocolContract.CurrentVersion` 保持 `"2.0.0"` 不变（新增操作是增量，不破坏协议）
2. CLI 和 Plugin 同时升级，确保操作目录一致
3. `PluginMcpServerController` 已有 `ExpectedMcpToolNames` 检查——确保新增操作名也包含在预期列表中
4. 考虑为 v1.1 的操作添加 `min_version` 元数据标记

---

## Phase-Specific Warnings

| Phase Topic | Likely Pitfall | Mitigation |
|-------------|---------------|------------|
| 跨插件 IPC 调用设计 | Pitfall 1（类型擦除）、Pitfall 3（线程亲和性）、Pitfall 5（约定矛盾）、Pitfall 8（异常分类）| 先定义约定，再实现操作；所有 IPC 调用默认 RunOnFrameworkThread |
| 插件重载操作 | Pitfall 2（IPC 断裂）、Pitfall 7（缺乏完成信号） | 重载操作返回后附带等待建议；实现 IsReady 轮询 |
| 数据回传 | Pitfall 2（生命周期）、Pitfall 6（连接管理）、Pitfall 11（背压）| 订阅管理器 + 有界队列 + 插件生命周期监听 |
| 斜杠命令调度 | Pitfall 4（Framework 阻塞）、Pitfall 12（注入风险） | 异步 fire-and-forget + 输入过滤 + 长度限制 |
| 操作注册 | Pitfall 9（源生成器模式）、Pitfall 10（分类硬编码）| 使用属性模式；扩展 OperationCategory |
| 测试 | Pitfall 14（IPC 模拟） | 内部接口注入 + 订阅管理器抽象 |
| 工具描述 | Pitfall 13（AI 误用） | 精确描述限制和条件 |
| 兼容性 | Pitfall 15（版本不匹配） | 保持协议版本不变；同步升级 |

---

## Critical Integration Risks

基于现有代码结构和 v1.1 新功能的交叉点：

| 交叉点 | 风险 | 影响范围 |
|--------|------|----------|
| `NamedPipeProtocolServer` + 数据回传 | 管道空闲超时（5 秒）可能切断长连接数据推送 | Phase 3（数据回传） |
| `PluginMcpServerController` + 重载后 MCP 工具列表变更 | 重载后暴露策略变化导致 MCP 工具列表与实际不一致 | Phase 2（插件重载） |
| `UnsafeInvokePluginIpcOperation` + 新增友好的 IPC 操作 | 两种 IPC 调用路径（unsafe vs typed）并存，用户混淆 | Phase 1（IPC 设计） |
| `PluginOperationExposurePolicy` + 新分类 | 新增操作 ID 必须分类为 action/unsafe/observe | 所有 Phase |
| 源生成器 `OperationDescriptorGenerator` + 新 Request 类型 | 所有新 Request 必须是 `[MemoryPackable]` + `[ProtocolOperation]` | 所有 Phase |
| `MemoryPack` 序列化 + IPC 返回的复杂类型 | IPC 返回的自定义类型无法通过 MemoryPack 序列化到管道协议 | Phase 1, 3 |

---

## "Looks Done But Isn't" Checklist

- **[跨插件 IPC 调用]** IPC 通道名拼对了，调用也有返回 → 但返回值是 `object` 无法序列化 → 管道协议报错
- **[插件重载]** 重载命令发送成功 → 但重载后 IPC 通道名相同、subscriber 引用失效 → `IpcNotReadyError`
- **[数据回传]** Subscribe 成功 → 但插件重载后订阅丢失 → 永远收不到数据，无错误
- **[斜杠命令]** 命令发送成功 → 但 MCP 等待游戏响应超时 → 管道断开
- **[暴露策略]** 新操作添加到操作列表 → 但未添加到 `ActionOperationIds` → 用户必须额外启用动作开关才能使用
- **[源生成器]** 新操作类编译通过 → 但 `[MemoryPackable]` 缺失运行时生成器 → 管道反序列化失败
- **[测试]** mock 接口测试通过 → 但真实 Dalamud IPC 场景中线程亲和性不匹配 → 游戏崩溃

---

## Sources

- 代码库审计：`UnsafeInvokePluginIpcOperation.cs`、`OperationProtocolDispatcher.cs`、`PluginMcpServerController.cs`、`NamedPipeProtocolServer.cs`（HIGH confidence — 一手代码）
- Dalamud 官方文档：`GetIpcSubscriber<T1...TRet>` API 和 `ICallGateProvider/ICallGateSubscriber` 模式（HIGH confidence — Context7 验证）
- 已有 pitfalls 研究：`.planning/research/PITFALLS.md`（v1.0 API 15 迁移坑点）（HIGH confidence — 项目内研究）
- Dalamud IPC 示例代码和生命周期文档（MEDIUM confidence — 官方示例模式已验证，插件重载细节需要运行时确认）
- 项目约束：PROJECT.md 明确「无 SDK 依赖」和「重载后不自动等待就绪」（HIGH confidence — 项目定义）

---

*Pitfalls research for: DalamudMCP v1.1 自动化测试桥接*
*Researched: 2026-05-01*