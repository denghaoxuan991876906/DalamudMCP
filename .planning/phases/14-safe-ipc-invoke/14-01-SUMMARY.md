---
phase: 14-safe-ipc-invoke
plan: 01
status: complete
started: 2026-05-01T00:00:00Z
completed: 2026-05-01T00:00:00Z
tasks_total: 2
tasks_complete: 2
files_created:
  - src/DalamudMCP.Plugin/Operations/SafeInvokePluginIpcOperation.cs
files_modified:
  - src/DalamudMCP.Plugin/Hosting/PluginOperationExposurePolicy.cs
---

## Plan Summary

创建了 `SafeInvokePluginIpcOperation` 安全 IPC 调用操作类，并将 `"plugin.ipc"` 操作 ID 注册到 unsafe 暴露策略。

## What was built

1. **SafeInvokePluginIpcOperation.cs** (224 lines) — 完整操作类，包含：
   - `[Operation("plugin.ipc")]` + `[McpTool("invoke_plugin_ipc")]` + `[CliCommand("plugin", "ipc")]`
   - Public 构造函数（DI 注入 `IPluginIpcGateway` + `IFramework`）和 internal 构造函数（测试执行器）
   - `Request` 嵌套类：`PluginName`、`Method`、`ArgumentsJson`（可选）
   - `CreateDalamudExecutor`：输入验证 + Framework 线程编排
   - `InvokeSafeIpc`：核心 IPC 调用逻辑，返回 5 种状态码
   - `ParseArguments` / `ParseJsonElement`：JSON 参数自动推断（int/double/bool/string/object）
   - JSON 对象和数组以 `GetRawText()` 字符串信封传递

2. **PluginOperationExposurePolicy.cs** — 在 `UnsafeOperationIds` 中添加 `"plugin.ipc"`，归类为 unsafe 操作

## Key decisions

- IPC CallGate 名称自动按 `{PluginName}.MCP.{Method}` 约定构造
- 参数类型从 JSON 值自动推断，复杂类型使用 JSON 字符串信封
- 返回类型固定为 `object`，结果序列化为 JSON 字符串
- 所有 IPC 调用始终在 Framework 线程执行（与 unsafe 版本不同）
- 5 种错误状态码覆盖全部 IPC 调用结果：`ipc_success` / `ipc_missing` / `ipc_not_ready` / `ipc_type_mismatch` / `ipc_plugin_error`

## Verified

- [x] dotnet build 成功 — 0 错误 0 警告
- [x] `grep "class SafeInvokePluginIpcOperation"` → 1
- [x] `grep "record SafeInvokePluginIpcResult"` → 1
- [x] `grep "Operation.*plugin\.ipc"` → 2（类属性 + ProtocolOperation）
- [x] `grep "McpTool.*invoke_plugin_ipc"` → 1
- [x] `grep "CliCommand.*plugin.*ipc"` → 1
- [x] 全部 5 种状态码字符串存在于文件中
- [x] `grep "GetRawText"` → 1
- [x] `grep "IPluginIpcGateway gateway"` → 1（public 构造注入）
- [x] `grep "RunOnFrameworkThread"` → 1
- [x] 文件行数 = 224（≥ 180）
- [x] "plugin.ipc" 已添加到 UnsafeOperationIds

## Self-Check: PASSED
