---
phase: 14-safe-ipc-invoke
plan: 02
status: complete
started: 2026-05-01T00:00:00Z
completed: 2026-05-01T00:00:00Z
tasks_total: 2
tasks_complete: 2
files_created:
  - tests/DalamudMCP.Plugin.Operations.Tests/SafeInvokePluginIpcOperationTests.cs
files_modified:
  - src/DalamudMCP.Plugin/Operations/SafeInvokePluginIpcOperation.cs
key-files:
  created:
    - tests/DalamudMCP.Plugin.Operations.Tests/SafeInvokePluginIpcOperationTests.cs
    - src/DalamudMCP.Plugin/Operations/SafeInvokePluginIpcOperation.cs
    - src/DalamudMCP.Plugin/Hosting/PluginOperationExposurePolicy.cs
---

## Plan Summary

为 `SafeInvokePluginIpcOperation` 创建完整单元测试（24 个测试），覆盖成功路径、错误路径、Framework 线程编排和构造函数验证。

## What was built

**SafeInvokePluginIpcOperationTests.cs** (478 行，24 个测试)：

### Task 1: 测试基础设施和成功路径测试（10 个测试）
1. `InvokeSafeIpc_ReturnsSuccess_WithNoArguments` — 无参数 IPC 调用成功
2. `InvokeSafeIpc_ReturnsSuccess_WithSingleIntArgument` — 单 int 参数调用
3. `InvokeSafeIpc_ReturnsSuccess_WithMultipleArguments` — 多类型参数调用 (int, string, bool)
4. `InvokeSafeIpc_ReturnsSuccess_WithJsonEnvelopeArgument` — JSON 对象信封参数
5. `InvokeSafeIpc_InfersIntType_ForIntegerJsonNumber` — 整数推断为 int
6. `InvokeSafeIpc_InfersDoubleType_ForDecimalJsonNumber` — 浮点数推断为 double
7. `InvokeSafeIpc_InfersObjectType_ForNullArgument` — null 推断为 object
8. `InvokeSafeIpc_ReturnsSuccess_WithBoolArgument` — true 布尔参数
9. `InvokeSafeIpc_ReturnsSuccess_WithFalseBoolArgument` — false 布尔参数
10. `InvokeSafeIpc_ReturnsSuccess_WithStringArgument` — 字符串参数

### Task 2: 错误路径、线程编排和构造函数验证（14 个测试）
11. `InvokeSafeIpc_ReturnsIpcMissing_WhenGatewayHasNoMatchingCallgate` — 空网关
12. `InvokeSafeIpc_ReturnsIpcMissing_WhenCallgateDifferent` — 不匹配的 callgate
13. `InvokeSafeIpc_ReturnsIpcNotReady_WhenHasFunctionIsFalse` — HasFunction=false
14. `InvokeSafeIpc_ReturnsIpcTypeMismatch_WhenInvalidCastExceptionThrown` — InvalidCastException 和 TargetInvocationException(InvalidCastException)
15. `InvokeSafeIpc_ReturnsIpcPluginError_WhenTargetPluginThrows` — InvalidOperationException
16. `InvokeSafeIpc_ReturnsIpcPluginError_WhenTargetInvocationExceptionWithoutInvalidCast` — TargetInvocationException(InvalidOperationException)
17. `InvokeSafeIpc_ReturnsIpcPluginError_WhenArgumentsJsonIsInvalid` — 无效 JSON
18. `ExecuteAsync_CallsInvokeDirectly_WhenAlreadyOnFrameworkThread` — Framework 线程直接调用
19. `ExecuteAsync_CallsRunOnFrameworkThread_WhenNotOnFrameworkThread` — 非 Framework 线程编排
20. `Constructor_RejectsNullGateway` — null gateway
21. `Constructor_RejectsNullFramework` — null framework
22. `ExecuteAsync_RejectsNullRequest` — null request
23. `ExecuteAsync_ThrowsArgumentException_WhenPluginNameIsEmpty` — 空 PluginName
24. `ExecuteAsync_ThrowsArgumentException_WhenMethodIsEmpty` — 空 Method

## Bug fixes during testing

在测试过程中发现并修复了 2 个问题：
1. `CreateDalamudExecutor` 中 `return await framework.RunOnFrameworkThread(() => ...)` 模式错误——`RunOnFrameworkThread` 返回 `Task` 而非 `Task<T>`，导致 NRE。修复为闭包捕获模式。
2. `InvokeSafeIpc` 缺少直接 `InvalidCastException` 捕获——添加了 `catch (InvalidCastException ex)` 在 `TargetInvocationException` 之前。
3. 通用异常捕获的 `SummaryText` 从 `"error"` 修正为 `"plugin error"` 以匹配 plan 规范。

## Verified

- [x] 24 个测试全部通过 — 0 失败
- [x] `[Fact]` 数量 = 24
- [x] 全部 5 种状态码字符串存在
- [x] `Substitute.For<IPluginCallGateSubscriber>` — NSubstitute mock 用于异常路径
- [x] `InvalidCastException` — 类型不匹配场景
- [x] `RunOnFrameworkThread` — Framework 线程编排
- [x] `ArgumentNullException` — 构造函数/请求 null 验证
- [x] `ArgumentException` — 空 PluginName/Method 验证
- [x] 文件行数 = 478（≥ 300）

## Self-Check: PASSED
