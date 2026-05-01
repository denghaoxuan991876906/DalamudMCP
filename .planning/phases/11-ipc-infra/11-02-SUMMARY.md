---
phase: 11-ipc-infra
plan: 02
subsystem: infra
tags: [ipc, dependency-injection, refactoring, dalamud, mcp]

# Dependency graph
requires:
  - phase: 11-ipc-infra
    provides: IPluginIpcGateway, IPluginCallGateSubscriber, PluginIpcGateway, ReflectionPluginCallGateSubscriber（独立 Ipc/ 目录文件）
provides:
  - UnsafeInvokePluginIpcOperation 重构完成，public 构造函数通过 DI 注入 IPluginIpcGateway
  - DI 容器注册 IPluginIpcGateway → PluginIpcGateway 单例映射
  - 嵌套类型已从操作文件中移除，引用独立 Ipc/ 目录文件
affects: [12-plugin-reload, 14-cross-plugin-ipc, 15-ipc-callback]

# Tech tracking
tech-stack:
  added: []
  patterns: [DI 构造注入, 单例服务手动注册]

key-files:
  created: []
  modified:
    - src/DalamudMCP.Plugin/Operations/UnsafeInvokePluginIpcOperation.cs
    - src/DalamudMCP.Plugin/Hosting/PluginServiceCollectionExtensions.cs
    - src/DalamudMCP.Plugin/Ipc/IPluginIpcGateway.cs
    - src/DalamudMCP.Plugin/Ipc/IPluginCallGateSubscriber.cs

key-decisions:
  - "IPluginIpcGateway 作为 DI 单例注册，供所有操作类注入使用（遵循 D-05/D-07 决策）"
  - "UnsafeInvokePluginIpcOperation 构造函数改为注入 IPluginIpcGateway + IFramework，不再直接 new PluginIpcGateway"

patterns-established:
  - "IPC 网关 DI 模式：操作类通过构造注入 IPluginIpcGateway，DI 容器管理 PluginIpcGateway 单例生命周期"
  - "接口文件添加显式 public 可访问性修饰符以满足 IDE0040 代码风格规则"

requirements-completed: []

# Metrics
duration: 3 min
completed: 2026-05-01
---

# Phase 11 Plan 02: IPC 网关 DI 连线 Summary

**重构 UnsafeInvokePluginIpcOperation 移除嵌套类型并通过 DI 注入共享 IPluginIpcGateway 单例**

## Performance

- **Duration:** 3 min
- **Started:** 2026-05-01T10:06:00Z
- **Completed:** 2026-05-01T10:09:02Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- UnsafeInvokePluginIpcOperation 中的嵌套接口/类已移除，替换为 Plan 01 创建的独立 Ipc/ 目录文件引用
- public 构造函数改为 DI 注入 `IPluginIpcGateway gateway` + `IFramework framework`（不再直接 new PluginIpcGateway）
- DI 容器中通过 `services.AddSingleton<IPluginIpcGateway, PluginIpcGateway>()` 注册 IPC 网关单例
- 项目编译通过 — 0 个警告，0 个错误

## Task Commits

Each task was committed atomically:

1. **Task 1: 重构 UnsafeInvokePluginIpcOperation.cs — 移除嵌套类型，DI 注入网关** - `c3bca19` (refactor)
2. **Task 2: 在 PluginServiceCollectionExtensions 中注册 IPluginIpcGateway 单例** - `fd7cea6` (feat)

## Files Created/Modified
- `src/DalamudMCP.Plugin/Operations/UnsafeInvokePluginIpcOperation.cs` - 移除嵌套类型（-78 行），public 构造函数改为 DI 注入 IPluginIpcGateway，移除不需要的 using
- `src/DalamudMCP.Plugin/Hosting/PluginServiceCollectionExtensions.cs` - 新增 using 和 `AddSingleton<IPluginIpcGateway, PluginIpcGateway>()` 注册
- `src/DalamudMCP.Plugin/Ipc/IPluginIpcGateway.cs` - 接口方法添加显式 `public` 修饰符（IDE0040 修复）
- `src/DalamudMCP.Plugin/Ipc/IPluginCallGateSubscriber.cs` - 接口成员添加显式 `public` 修饰符（IDE0040 修复）

## Decisions Made
- 遵循 CONTEXT.md 中所有 Phase 11 决策（D-01 至 D-07），无偏离
- `PluginIpcValueKind` 枚举和 `UnsafeInvokePluginIpcResult` record 保留在原文件位置

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] IDE0005 — 移除不再需要的 using 指令**
- **Found during:** Task 1（构建验证）
- **Issue:** 嵌套类 `PluginIpcGateway` 被移除后，`using Dalamud.Plugin;` 不再被引用，触发 IDE0005 编译错误
- **Fix:** 从 using 块中移除 `using Dalamud.Plugin;`
- **Files modified:** `src/DalamudMCP.Plugin/Operations/UnsafeInvokePluginIpcOperation.cs`
- **Verification:** `dotnet build` 通过，0 错误
- **Committed in:** `c3bca19`（Task 1 提交）

**2. [Rule 3 - Blocking] IDE0040 — Plan 01 文件缺少显式可访问性修饰符**
- **Found during:** Task 1（构建验证）
- **Issue:** Plan 01 创建的 `IPluginIpcGateway.cs` 和 `IPluginCallGateSubscriber.cs` 中接口成员缺少显式 `public` 修饰符，构建配置将 IDE0040 视为错误
- **Fix:** 为接口成员添加显式 `public` 修饰符
- **Files modified:** `src/DalamudMCP.Plugin/Ipc/IPluginIpcGateway.cs`、`src/DalamudMCP.Plugin/Ipc/IPluginCallGateSubscriber.cs`
- **Verification:** `dotnet build` 通过，0 错误
- **Committed in:** `c3bca19`（Task 1 提交）

---

**Total deviations:** 2 auto-fixed（均为 Rule 3 阻塞问题）
**Impact on plan:** 两个修复均为构建正确性所必需。无功能变更，无范围蔓延。

## Issues Encountered
- `./build/build.ps1` 因 PowerShell 5.1 不支持 `$IsWindows` 变量而失败，改用 `dotnet build` 直接构建

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- IPluginIpcGateway 已可通过 DI 容器注入到所有操作类
- 为 Phase 12（插件重载）、Phase 14（跨插件 IPC 调用）、Phase 15（IPC 回调）铺平道路
- Internal 测试构造函数保持兼容：`internal UnsafeInvokePluginIpcOperation(Func<...> executor)`

---
*Phase: 11-ipc-infra*
*Completed: 2026-05-01*
