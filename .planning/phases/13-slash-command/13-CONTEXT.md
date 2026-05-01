# Phase 13: 斜杠命令调度 - Context

**Gathered:** 2026-05-01
**Status:** Ready for planning

<domain>
## Phase Boundary

AI 客户端通过 MCP `slash_command` 工具发送 Dalamud 注册的斜杠命令到游戏内。命令通过 `ICommandManager.ProcessCommand()` 在 Framework 线程上以 fire-and-forget 模式派发。仅支持 Dalamud 注册命令。

</domain>

<decisions>
## Implementation Decisions

### 输入验证
- **D-01:** 宽松验证：命令必须以 `/` 开头，长度上限 256 字符。
- **D-02:** 不滤除特殊字符——让 `ICommandManager` 自然拒绝无效命令。不进行控制字符/换行/null 字节过滤。

### Claude's Discretion
以下领域由下游代理根据代码库模式自行决定：

- **响应模型：** Fire-and-forget 模式下返回结构化响应。建议参考 Phase 12 的 `PluginReloadResult` 模式（Success/Status/ErrorMessage/SummaryText），状态码至少包含 `command_sent` / `validation_failed`。无需复杂的状态检测（命令执行结果不可知）。
- **游戏原生命令策略：** `ICommandManager.ProcessCommand()` 仅能派发 Dalamud 注册命令（`REQUIREMENTS.md` 已明确）。游戏原生命令自然无法执行——在 MCP 工具描述中说明此限制，不在代码层面做预检测（预检测需维护命令前缀列表，脆弱且易过时）。
- **暴露策略：** 归类为 `unsafe` 操作（与 Phase 12 的 `PluginReloadOperation` 一致），受 UI 开关控制。

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### 现有实现（Phase 12 参考模式）
- `src/DalamudMCP.Plugin/Operations/PluginReloadOperation.cs` — Phase 12 的操作实现，已使用 `ICommandManager.ProcessCommand()` + Framework 线程编排，是 Phase 13 最直接的代码参考
- `src/DalamudMCP.Plugin/Hosting/PluginServiceCollectionExtensions.cs` — DI 容器注册入口，Phase 13 操作通过源生成器自动注册，无需手动修改此文件

### 测试
- `tests/DalamudMCP.Plugin.Operations.Tests/PluginReloadOperationTests.cs` — Phase 12 测试模式，展示了如何 mock `ICommandManager`（`CreateCommandManager()` at line 33-36）、验证 `ProcessCommand()` 调用、测试 Framework 线程编排

### 上游规格
- `.planning/ROADMAP.md` — Phase 13 目标和成功标准（§Phase 13: 斜杠命令调度）
- `.planning/REQUIREMENTS.md` — SLASH-01 需求 + Out of Scope 中关于「游戏原生聊天命令发送」的说明
- `.planning/PROJECT.md` — 项目技术约束和关键决策

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `ICommandManager.ProcessCommand()`：已被 `PluginReloadOperation` 验证可用（`PluginReloadOperation.cs:110`），通过 DI 构造注入获取
- `IFramework.RunOnFrameworkThread()`：Framework 线程编排模式已建立（`PluginReloadOperation.cs:108-118`）
- 操作类骨架：`[Operation]` + `[CliCommand]` + `[McpTool]` + Request/Result record + TextFormatter 模式成熟

### Established Patterns
- 构造注入：操作类通过构造函数注入 Dalamud 服务（`IDalamudPluginInterface`、`IFramework`、`ICommandManager`）
- 线程安全：检测 `IFramework.IsInFrameworkUpdateThread`，必要时编排到 Framework 线程
- Fire-and-forget：Phase 12 的 `/xlreload` 命令发送即为 fire-and-forget，不等待执行结果
- 源生成器注册：`[Operation]` 属性被 `AddGeneratedPluginOperations()` 自动发现，无需手动 DI 注册

### Integration Points
- `PluginCompositionRoot.CreateFromDalamud()` → `BuildDalamudServiceProvider()` → DI 容器：`ICommandManager` 需在此路径中可用（Phase 12 已解决）
- `OperationProtocolDispatcher`：新操作通过协议调度器自动路由
- `PluginOperationExposurePolicy`：新操作需归类到 unsafe 策略

</code_context>

<deferred>
## Deferred Ideas

- 无 — 讨论未超出阶段范围

</deferred>

---

*Phase: 13-slash-command*
*Context gathered: 2026-05-01*
