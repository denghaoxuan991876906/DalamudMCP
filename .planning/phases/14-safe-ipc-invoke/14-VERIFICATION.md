---
status: passed
phase: 14-safe-ipc-invoke
plans: 2
verified: 2026-05-01
---

# Phase 14 Verification: 安全 IPC 调用

## Goal Verification

Phase goal: AI 客户端能够通过 MCP 调用目标插件的 IPC 函数，传入参数并获取返回值，错误信息结构化可读

### Success Criteria Check

| # | Criterion | Status | Evidence |
|---|-----------|--------|----------|
| 1 | MCP `invoke_plugin_ipc` 工具可用，指定 plugin-name + method + arguments-json | ✅ PASSED | `[McpTool("invoke_plugin_ipc")]` 属性已注册；Request 包含 PluginName/Method/ArgumentsJson |
| 2 | 约定式命名 `{Name}.MCP.{Action}`，目标插件零 SDK 依赖 | ✅ PASSED | `InvokeSafeIpc` 构建 callgate = `$"{pluginName}.MCP.{method}"`；无 SDK 引用 |
| 3 | Framework 线程执行，支持基元类型和 JSON 字符串信封 | ✅ PASSED | `CreateDalamudExecutor` 使用 `IFramework.RunOnFrameworkThread`；`ParseJsonElement` 支持 JSON 信封 (`GetRawText`) |
| 4 | 4+ 种状态码细分 | ✅ PASSED | 5 种状态码全部实现：ipc_success / ipc_missing / ipc_not_ready / ipc_type_mismatch / ipc_plugin_error |
| 5 | 现有 `unsafe.invoke.plugin-ipc` 逃生舱不受影响 | ✅ PASSED | `UnsafeInvokePluginIpcOperation.cs` 未修改；新操作为独立文件 |

### must_haves Verification

| Plan | must_haves | Status |
|------|-----------|--------|
| 14-01 | 10 truths + 2 artifacts + 4 key_links | ✅ All verified |
| 14-02 | 5 truths + 2 artifacts + 3 key_links | ✅ All verified |

## Artifacts

| Path | Type | Status |
|------|------|--------|
| `src/.../SafeInvokePluginIpcOperation.cs` | New | ✅ Created (237 lines) |
| `src/.../PluginOperationExposurePolicy.cs` | Modified | ✅ Added "plugin.ipc" |
| `tests/.../SafeInvokePluginIpcOperationTests.cs` | New | ✅ Created (478 lines) |

## Test Coverage

- **24/24** tests passing
- Coverage breakdown: 10 success paths + 7 error paths + 2 threading + 5 constructor/validation
- All 5 status codes tested
- Type inference: int, double, bool, string, null, JSON envelope
- Exception routing: InvalidCastException → ipc_type_mismatch, other exceptions → ipc_plugin_error

## Build

- `dotnet build src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj -c Debug` — 0 errors, 0 warnings
- `dotnet test --filter-class SafeInvokePluginIpcOperationTests` — 24 passed, 0 failed

## Issues

None.

## Conclusion

**Phase 14 目标达成。** 安全 IPC 调用操作已实现并经过完整测试，所有 24 个测试通过。现有 unsafe 逃生舱不受影响。
