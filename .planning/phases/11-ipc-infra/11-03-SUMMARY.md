---
phase: 11-ipc-infra
plan: 03
subsystem: testing
tags: [xunit, nsubstitute, ipc, test-stubs]

# Dependency graph
requires:
  - phase: 11-ipc-infra-01
    provides: IPluginIpcGateway and IPluginCallGateSubscriber public interfaces
  - phase: 11-ipc-infra-02
    provides: PluginIpcGateway and ReflectionPluginCallGateSubscriber DI wiring
provides:
  - FakeIpcGateway 和 FakeIpcCallGateSubscriber 公共测试桩供 Phase 12/14/15 复用
  - PluginIpcGateway 和 ReflectionPluginCallGateSubscriber 独立单元测试覆盖
affects: [12-plugin-reload, 14-cross-plugin-ipc, 15-data-return-ipc]

# Tech tracking
tech-stack:
  added: [NSubstitute 5.3.0]
  patterns: [xUnit v3 Fact 测试模式, 公共测试桩提取模式, NSubstitute mock IDalamudPluginInterface]

key-files:
  created:
    - tests/DalamudMCP.Plugin.Operations.Tests/TestShared/Ipc/FakeIpcGateway.cs
    - tests/DalamudMCP.Plugin.Operations.Tests/TestShared/Ipc/FakeIpcCallGateSubscriber.cs
    - tests/DalamudMCP.Plugin.Operations.Tests/PluginIpcGatewayTests.cs
    - tests/DalamudMCP.Plugin.Operations.Tests/ReflectionPluginCallGateSubscriberTests.cs
  modified:
    - tests/DalamudMCP.Plugin.Operations.Tests/UnsafeInvokePluginIpcOperationTests.cs
    - tests/DalamudMCP.Plugin.Operations.Tests/DalamudMCP.Plugin.Operations.Tests.csproj

key-decisions:
  - "使用 NSubstitute 5.3.0 作为 mock 框架，因其计划中明确引用 Substitute 语法"
  - "FakeIpcGateway 和 FakeIpcCallGateSubscriber 使用字典存储而非硬编码，保持与原 FakeGateway 逻辑一致"
  - "PluginIpcGateway 正向测试使用 NSubstitute 创建 ICallGateSubscriber<bool> mock，通过反射验证 GetIpcSubscriber 泛型方法调用"
  - "CA1822 警告通过 #pragma warning disable 抑制—— InvokeFunc 必须是实例方法以通过 ReflectionPluginCallGateSubscriber 反射查找"

patterns-established:
  - "公共测试桩提取模式：嵌套测试桩类提升为 TestShared/ 下 public 类供跨阶段复用"
  - "NSubstitute + 反射测试模式：用于测试依赖 Dalamud 原生接口的服务"

requirements-completed: []

# Metrics
duration: 21.8 min
completed: 2026-05-01
---

# Phase 11 Plan 03: IPC 测试桩提取与单元测试 Summary

**从 UnsafeInvokePluginIpcOperationTests 提取公共测试桩，为 PluginIpcGateway 和 ReflectionPluginCallGateSubscriber 创建独立单元测试**

## Performance

- **Duration:** 21.8 min
- **Started:** 2026-05-01T10:16:37Z
- **Completed:** 2026-05-01T10:38:23Z
- **Tasks:** 2
- **Files modified:** 6

## Accomplishments

- 提取 `FakeIpcGateway` 和 `FakeIpcCallGateSubscriber` 为 `TestShared/Ipc/` 下的 `public` 测试桩，可被 Phase 12/14/15 测试复用
- 从 `UnsafeInvokePluginIpcOperationTests` 中移除嵌套 `FakeGateway`/`FakeSubscriber` 类，更新所有引用使用独立接口和测试桩
- 为 `PluginIpcGateway` 创建 5 个独立 `[Fact]` 测试，覆盖 TryCreate 成功/失败/异常路径
- 为 `ReflectionPluginCallGateSubscriber` 创建 7 个独立 `[Fact]` 测试，覆盖 HasFunction/InvokeFunc/构造异常
- 添加 NSubstitute 5.3.0 作为测试依赖，用于 mock `IDalamudPluginInterface`

## Task Commits

Each task was committed atomically:

1. **Task 1: 提取 FakeGateway 和 FakeSubscriber 为公共测试桩** - `6b02586` (feat)
2. **Task 2 (TDD RED): 创建单元测试** - `d114ea3` (test)

_TDD note: GREEN 阶段由 Plan 11-01/11-02 中已完成的实现满足，测试在已有实现上验证通过。_

**Plan metadata:** (pending final commit)

## Files Created/Modified

### Created
- `tests/DalamudMCP.Plugin.Operations.Tests/TestShared/Ipc/FakeIpcGateway.cs` — 公共 `IPluginIpcGateway` 测试桩，字典驱动
- `tests/DalamudMCP.Plugin.Operations.Tests/TestShared/Ipc/FakeIpcCallGateSubscriber.cs` — 公共 `IPluginCallGateSubscriber` 测试桩
- `tests/DalamudMCP.Plugin.Operations.Tests/PluginIpcGatewayTests.cs` — 5 个 Fact 测试覆盖 PluginIpcGateway
- `tests/DalamudMCP.Plugin.Operations.Tests/ReflectionPluginCallGateSubscriberTests.cs` — 7 个 Fact 测试覆盖 ReflectionPluginCallGateSubscriber

### Modified
- `tests/DalamudMCP.Plugin.Operations.Tests/UnsafeInvokePluginIpcOperationTests.cs` — 删除嵌套类，引用更新为公共测试桩
- `tests/DalamudMCP.Plugin.Operations.Tests/DalamudMCP.Plugin.Operations.Tests.csproj` — 添加 NSubstitute 5.3.0 依赖

## Decisions Made

- 使用 NSubstitute 5.3.0 作为 mock 框架（计划引用 Substitute 语法，原项目无 mock 库）
- 测试桩类重命名为 `FakeIpcGateway`/`FakeIpcCallGateSubscriber`（从 `FakeGateway`/`FakeSubscriber`），更明确表达用途
- PluginIpcGateway 正向测试使用 `ICallGateSubscriber<bool>` mock，启用 NSubstitute 显式配置绕过对未匹配 callgate 的自动 substitute 行为
- CA1822 静态方法警告通过 pragma 抑制——`InvokeFunc` 必须保持实例方法以通过 ReflectionPluginCallGateSubscriber 的 `BindingFlags.Instance` 反射查找

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] 添加 NSubstitute mock 框架依赖**
- **Found during:** Task 2（TDD RED 阶段）
- **Issue:** 计划要求使用 `Substitute` mock `IDalamudPluginInterface`，但测试项目未安装 NSubstitute
- **Fix:** 手动在 csproj 中添加 `NSubstitute 5.3.0` 依赖
- **Files modified:** `tests/DalamudMCP.Plugin.Operations.Tests/DalamudMCP.Plugin.Operations.Tests.csproj`, `packages.lock.json`
- **Committed in:** `d114ea3` (Task 2 commit)

**2. [Rule 1 - Bug] NSubstitute 自动 substitute 接口返回值导致非预期测试行为**
- **Found during:** Task 2（测试运行阶段）
- **Issue:** NSubstitute 默认对接口返回值自动创建 substitute，导致 `GetIpcSubscriber<bool>(...)` 返回非 null substitute 而非 null，使 "callgate 不存在" 测试错误通过
- **Fix:** 显式配置 `.Returns((ICallGateSubscriber<bool>?)null)` 模拟无匹配 callgate 场景
- **Files modified:** `PluginIpcGatewayTests.cs`
- **Committed in:** `d114ea3` (Task 2 commit)

**3. [Rule 1 - Bug] ArgumentException.ThrowIfNullOrWhiteSpace 异常类型不匹配**
- **Found during:** Task 2（测试运行阶段）
- **Issue:** 代码使用 `.NET` 的 `ArgumentException.ThrowIfNullOrWhiteSpace`，对 null 抛出 `ArgumentNullException`（非 `ArgumentException`）。原测试期望两者均为 `ArgumentException`
- **Fix:** 拆分测试断言：null callgate → `ArgumentNullException`，空白 callgate → `ArgumentException`
- **Files modified:** `PluginIpcGatewayTests.cs`
- **Committed in:** `d114ea3` (Task 2 commit)

---

**Total deviations:** 3 auto-fixed (1 blocking, 2 bugs)
**Impact on plan:** 所有 auto-fix 为正确 mock 行为和测试准确性所必需。无架构变更，无范围蔓延。

## Issues Encountered

- xUnit v3 的 `--filter` 选项已被重命名为 `--filter-class`/`--filter-query`，与 v2 不兼容——使用 `dotnet test` 全量运行避免筛选问题
- PowerShell `$IsWindows` 变量在旧版 Windows PowerShell 中未定义，导致 `build.ps1` 脚本报错——改用 `dotnet build` 直接构建

## Next Phase Readiness

- 公共测试桩 (`FakeIpcGateway`, `FakeIpcCallGateSubscriber`) 可供 Phase 12/14/15 测试直接引用
- 独立单元测试覆盖证明了提取后接口实现的行为正确性
- Phase 11 全部 3 个计划已完成，IPC 基础设施提取阶段就绪

---
*Phase: 11-ipc-infra*
*Completed: 2026-05-01*
