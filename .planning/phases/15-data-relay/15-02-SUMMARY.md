---
phase: 15-data-relay
plan: 02
subsystem: 数据回传
tags: [mcp-tool, plugin-data, subscribe, unsubscribe, poll, channel, ipc-relay, xunit, memorypack]

# Dependency graph
requires:
  - phase: 15-01
    provides: IPluginDataRelayService 接口 + PluginDataRelayService 实现 + DI 注册
provides:
  - MCP 工具 plugin_data_subscribe（订阅数据回传通道）
  - MCP 工具 plugin_data_unsubscribe（退订通道并清理资源）
  - MCP 工具 plugin_data_poll（非阻塞轮询缓存数据，支持 max-items 限制）
  - UnsafeOperationIds 暴露策略注册（3 个新操作 ID）
  - FakePluginDataRelayService 测试桩（ConcurrentDictionary + Channel 纯内存实现）
  - 21 个单元测试（Subscribe 8 个 + Unsubscribe 5 个 + Poll 8 个）
affects: [mcp-client, plugin-testing, relay-lifecycle]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "MCP 操作类模式：sealed partial + IOperation<TReq, TRes> + DI 构造注入 + Framework 线程编排 + internal 测试构造"
    - "FakePluginDataRelayService 测试桩模式：实现接口，ConcurrentDictionary 管理 Channel<string>，WriteData 辅助方法"
    - "操作类使用 try-catch 统一异常处理，返回 Success=false + 错误状态码的模式"

key-files:
  created:
    - src/DalamudMCP.Plugin/Operations/PluginDataSubscribeOperation.cs
    - src/DalamudMCP.Plugin/Operations/PluginDataUnsubscribeOperation.cs
    - src/DalamudMCP.Plugin/Operations/PluginDataPollOperation.cs
    - tests/DalamudMCP.Plugin.Operations.Tests/TestShared/Relay/FakePluginDataRelayService.cs
    - tests/DalamudMCP.Plugin.Operations.Tests/PluginDataSubscribeOperationTests.cs
    - tests/DalamudMCP.Plugin.Operations.Tests/PluginDataUnsubscribeOperationTests.cs
    - tests/DalamudMCP.Plugin.Operations.Tests/PluginDataPollOperationTests.cs
  modified:
    - src/DalamudMCP.Plugin/Hosting/PluginOperationExposurePolicy.cs

key-decisions:
  - "操作类使用 public DI 构造函数注入 IPluginDataRelayService + IFramework，internal 构造注入 Func executor 用于单元测试"
  - "max-items 上限钳位至 10000（通过常量 MaxItemsUpperLimit），超限自动裁剪"
  - "所有三个 MCP 工具分类为 unsafe 操作，仅 enableUnsafeOperations=true 时暴露给 AI"
  - "FakePluginDataRelayService 使用 BoundedChannelFullMode.DropOldest 策略模拟真实 relay 的溢出行为"

requirements-completed:
  - RELAY-01

# Metrics
duration: ~15min
completed: 2026-05-02
---

# Phase 15 Plan 02: 操作类+测试 总结

**3 个 MCP 工具操作类（subscribe/unsubscribe/poll）+ 暴露策略注册 + 21 个单元测试全部通过**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-05-02
- **Completed:** 2026-05-02
- **Tasks:** 2
- **Files created/modified:** 8

## Accomplishments

- 创建 PluginDataSubscribeOperation，支持 subscribe_success / already_subscribed / subscribe_failed 三种状态码，幂等订阅
- 创建 PluginDataUnsubscribeOperation，支持 unsubscribe_success / not_subscribed 状态码，退订后清理通道数据
- 创建 PluginDataPollOperation，支持 data_available / no_data / channel_not_found 状态码，max-items 参数钳位至 10000
- 所有三个操作 ID 注册至 UnsafeOperationIds，通过 UI 安全开关受控暴露
- FakePluginDataRelayService 纯内存测试桩，使用 ConcurrentDictionary + Channel<string> + DropOldest 溢出策略
- 21 个单元测试覆盖全部状态码路径、空输入验证、构造函数 null 检查、max-items 限制、通道清理

## Task Commits

Each task was committed atomically:

1. **Task 1: 创建 3 个操作类 + 暴露策略注册** - `413f935` (feat)
2. **Task 2: 创建测试桩 + 21 个单元测试** - `60bbcf5` (test)

## Files Created/Modified

- `src/DalamudMCP.Plugin/Operations/PluginDataSubscribeOperation.cs` - MCP: plugin_data_subscribe 操作类（141 行）
- `src/DalamudMCP.Plugin/Operations/PluginDataUnsubscribeOperation.cs` - MCP: plugin_data_unsubscribe 操作类（119 行）
- `src/DalamudMCP.Plugin/Operations/PluginDataPollOperation.cs` - MCP: plugin_data_poll 操作类（161 行）
- `tests/.../TestShared/Relay/FakePluginDataRelayService.cs` - 可控的 IPluginDataRelayService 测试桩
- `tests/.../PluginDataSubscribeOperationTests.cs` - 8 个订阅操作测试
- `tests/.../PluginDataUnsubscribeOperationTests.cs` - 5 个退订操作测试
- `tests/.../PluginDataPollOperationTests.cs` - 8 个轮询操作测试
- `src/DalamudMCP.Plugin/Hosting/PluginOperationExposurePolicy.cs` - UnsafeOperationIds 新增 3 行

## Decisions Made

- .NET 10 中 `ArgumentException.ThrowIfNullOrWhiteSpace` 的 `paramName` 为表达式路径（如 `"request.Channel"` 而非简短名 `"channel"`），测试断言需匹配实际值

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] CA2208 编译错误：ArgumentException paramName 不匹配**
- **Found during:** Task 1 构建验证
- **Issue:** `throw new ArgumentException("...", nameof(Request.MaxItems))` — `nameof(Request.MaxItems)` 不是方法参数名，触发 CA2208
- **Fix:** 改为 `nameof(request)`（lambda 有效参数名）
- **Files modified:** `src/DalamudMCP.Plugin/Operations/PluginDataPollOperation.cs`
- **Committed in:** `413f935`

**2. [Rule 1 - Bug] XML 注释解析错误**
- **Found during:** Task 2 构建验证
- **Issue:** `Channel<string>` 中的尖括号被 XML 解析器误认为 XML 标签
- **Fix:** 改为 `Channel(Of String)` 避免 XML 解析冲突
- **Files modified:** `tests/.../TestShared/Relay/FakePluginDataRelayService.cs`
- **Committed in:** `60bbcf5`

**3. [Rule 1 - Bug] 测试断言 paramName 与 .NET 10 实际值不匹配**
- **Found during:** Task 2 测试运行
- **Issue:** 测试断言 `Assert.Equal("channel", ex.ParamName)` 但 .NET 10 生成 `"request.Channel"`（表达式路径）
- **Fix:** 4 处测试断言更新为实际 paramName（`"request.PluginName"` / `"request.Channel"`）
- **Files modified:** 所有 3 个测试文件
- **Committed in:** `60bbcf5`

**4. [Rule 1 - Bug] subscribe_failed 测试使用错误的构造函数**
- **Found during:** Task 2 测试运行
- **Issue:** internal 构造传入 `throw` lambda 导致异常未被操作类的 try-catch 捕获而传播
- **Fix:** 改为 internal 构造传入预构建的失败 `PluginDataSubscribeResult` 对象
- **Files modified:** `tests/.../PluginDataSubscribeOperationTests.cs`
- **Committed in:** `60bbcf5`

---

**Total deviations:** 4 auto-fixed (4 Rule 1 - Bug)
**Impact on plan:** 所有修复均为 .NET 10 行为适配和测试修复，无架构变更，无范围蔓延。

## Issues Encountered

- xUnit v3 (Microsoft.Testing.Platform v2) 使用 `--filter-class` 而非传统 `--filter` 参数，需调整测试运行命令
- .NET 10 中 `ThrowIfNullOrWhiteSpace` 的 `paramName` 行为与该库旧版本不同（表达式路径 vs 参数名），需适配测试

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Plan 15-02 完成，Phase 15 全部 2 个 plan 执行完毕
- 3 个 MCP 工具操作类已就绪，源生成器自动注册至 DI
- 21 个单元测试全部通过，覆盖全部状态码路径和边界条件
- Phase 15 可进入验证阶段（`/gsd-verify-work`）

---
*Phase: 15-data-relay*
*Plan: 02*
*Completed: 2026-05-02*
