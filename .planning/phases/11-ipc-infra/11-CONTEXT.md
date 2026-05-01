# Phase 11: IPC 基础设施提取 - Context

**Gathered:** 2026-05-01
**Status:** Ready for planning

<domain>
## Phase Boundary

从 `UnsafeInvokePluginIpcOperation` 中提取 `IPluginIpcGateway` 和 `IPluginCallGateSubscriber` 为独立 DI 单例服务，供后续跨插件操作（Phase 12/14/15）注入使用。现有 `UnsafeInvokePluginIpcOperation` 重构为使用共享 IPC 网关，功能无回归。该阶段不引入新的面向用户的功能，仅进行基础设施重构。

</domain>

<decisions>
## Implementation Decisions

### 接口放置位置
- **D-01:** 接口放在 `DalamudMCP.Plugin` 项目顶层，不在 Framework 或 Protocol 项目中。
- **D-02:** 提取接口（`IPluginIpcGateway`、`IPluginCallGateSubscriber`，public）+ 实现类（`PluginIpcGateway`、`ReflectionPluginCallGateSubscriber`，internal）为独立文件。`PluginIpcValueKind` 枚举和 `UnsafeInvokePluginIpcResult` 保留在原文件中。
- **D-03:** 新建 `src/DalamudMCP.Plugin/Ipc/` 子目录存放这些文件。

### 网关范围
- **D-04:** `IPluginIpcGateway` 保持纯粹的 IPC CallGate 订阅/调用语义，不扩展插件发现或生命周期管理能力。Phase 12 的插件重载操作直接注入 `IPluginFinder`（Dalamud 原生接口）。
- **D-05:** `PluginIpcGateway` 采用手动 DI 注册，在 `PluginServiceCollectionExtensions.BuildDalamudServiceProvider()` 中通过 `services.AddSingleton<IPluginIpcGateway, PluginIpcGateway>()` 注册。
- **D-06:** 现有测试中的 `FakeGateway` 和 `FakeSubscriber` 提取为测试项目中的公共测试桩，供 Phase 12/14/15 的测试复用。
- **D-07:** `UnsafeInvokePluginIpcOperation` 的 public 构造函数改为注入 `IPluginIpcGateway` + `IFramework`（不再直接 `new PluginIpcGateway`）。internal 测试构造函数保持不变（直接注入 `Func<Request, CancellationToken, ValueTask<UnsafeInvokePluginIpcResult>>` executor）。

### Claude's Discretion
- 提取后各文件内部的导入语句、命名空间组织
- 测试桩的具体文件位置和命名

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### 现有实现
- `src/DalamudMCP.Plugin/Operations/UnsafeInvokePluginIpcOperation.cs` — 当前 IPC 操作完整实现，包含需提取的嵌套接口和类（`IPluginIpcGateway`、`IPluginCallGateSubscriber`、`PluginIpcGateway`、`ReflectionPluginCallGateSubscriber`）
- `src/DalamudMCP.Plugin/Hosting/PluginServiceCollectionExtensions.cs` — DI 容器注册入口，需在此添加 `IPluginIpcGateway` 的单例注册
- `src/DalamudMCP.Plugin/PluginCompositionRoot.cs` — 插件组合根，DI 容器创建流程

### 测试
- `tests/DalamudMCP.Plugin.Operations.Tests/UnsafeInvokePluginIpcOperationTests.cs` — 现有 IPC 操作测试，包含 `FakeGateway` 和 `FakeSubscriber` 的实现
- `tests/DalamudMCP.Plugin.Operations.Tests/GeneratedOperationInvokerTests.cs:457-458` — 源生成器测试中手动注册 `UnsafeInvokePluginIpcOperation` 的方式

### 上游规格
- `.planning/ROADMAP.md` — Phase 11 目标和成功标准
- `.planning/PROJECT.md` — 项目技术约束和关键决策

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `IPluginIpcGateway` / `IPluginCallGateSubscriber` 接口：已在 `UnsafeInvokePluginIpcOperation` 中定义完备，只需提升为顶层类型
- `PluginIpcGateway`：基于 `IDalamudPluginInterface.GetIpcSubscriber()` 反射调用，无外部依赖
- `ReflectionPluginCallGateSubscriber`：封装反射调用 `HasFunction` / `InvokeFunc`，纯反射实现
- 测试中的 `FakeGateway` / `FakeSubscriber`：已有完整的手动 mock 实现，可直接提取

### Established Patterns
- DI 注册：所有服务在 `PluginServiceCollectionExtensions.BuildDalamudServiceProvider()` 中手动 `AddSingleton` 注册（`src/DalamudMCP.Plugin/Hosting/PluginServiceCollectionExtensions.cs:46-74`）
- 操作类通过源生成器自动注册：`services.AddGeneratedPluginOperations()` 扫描 `[Operation]` 属性
- 构造注入：操作类通过 DI 构造函数注入 Dalamud 服务（如 `IDalamudPluginInterface`、`IFramework`）

### Integration Points
- `PluginCompositionRoot.CreateFromDalamud()` → `BuildDalamudServiceProvider()` → DI 容器：所有服务注册的入口
- `UnsafeInvokePluginIpcOperation` 的 public 构造函数：需从 `new PluginIpcGateway(pluginInterface)` 改为 DI 注入 `IPluginIpcGateway`
- 源生成器 `OperationDescriptorGenerator`：扫描 `[Operation]` 属性自动生成注册代码，不需要关注接口提取

</code_context>

<deferred>
## Deferred Ideas

- 无 — 讨论未超出阶段范围

</deferred>

---

*Phase: 11-ipc-infra*
*Context gathered: 2026-05-01*
