---
phase: 11-ipc-infra
plan: 01
subsystem: infra
tags: [ipc, reflection, dalamud, di, callgate]

# Dependency graph
requires:
  - phase: 10-add-log-reading
    provides: UnsafeInvokePluginIpcOperation 中嵌套的 IPluginIpcGateway / IPluginCallGateSubscriber 接口及实现
provides:
  - IPluginIpcGateway public 接口（TryCreate 方法，通过 IDalamudPluginInterface 反射订阅 IPC）
  - IPluginCallGateSubscriber public 接口（HasFunction 属性，InvokeFunc 方法）
  - PluginIpcGateway internal 实现（反射调用 GetIpcSubscriber<T...>）
  - ReflectionPluginCallGateSubscriber internal 实现（反射封装 HasFunction / InvokeFunc）
affects: [12-plugin-reload, 14-safe-ipc, 15-data-relay]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "IPC 网关抽象模式：public 接口 + internal 反射实现 + DI 单例注册"
    - "嵌套类型提升为顶层类型：保持语义不变，提升可访问性"
    - "反射缓存模式：MethodInfo[] 静态缓存 + PropertyInfo/MethodInfo 实例缓存"

key-files:
  created:
    - src/DalamudMCP.Plugin/Ipc/IPluginIpcGateway.cs
    - src/DalamudMCP.Plugin/Ipc/IPluginCallGateSubscriber.cs
    - src/DalamudMCP.Plugin/Ipc/PluginIpcGateway.cs
    - src/DalamudMCP.Plugin/Ipc/ReflectionPluginCallGateSubscriber.cs
  modified: []

key-decisions:
  - "接口提升为 public 置于 DalamudMCP.Plugin.Ipc 命名空间"
  - "实现类保持 internal，仅通过 DI 注入暴露"
  - "代码从 UnsafeInvokePluginIpcOperation 原样提取，无逻辑修改"

patterns-established:
  - "IPC 服务提取模式：内嵌类型 → 顶层文件 + 接口 public 化"

requirements-completed: []

# Metrics
duration: 3.3 min
completed: 2026-05-01
---

# Phase 11 Plan 01: IPC 基础设施提取 Summary

**从 UnsafeInvokePluginIpcOperation 提取 IPluginIpcGateway / IPluginCallGateSubscriber 接口及实现为独立文件，接口提升为 public 供 DI 注入**

## Performance

- **Duration:** 3.3 min
- **Started:** 2026-05-01T09:53:13Z
- **Completed:** 2026-05-01T09:56:30Z
- **Tasks:** 2
- **Files modified:** 4 (全部新建)

## Accomplishments

- 创建了 `IPluginIpcGateway` public 接口，定义 `TryCreate` 方法——可通过 `IDalamudPluginInterface` 反射订阅任意插件的 IPC CallGate
- 创建了 `IPluginCallGateSubscriber` public 接口，定义 `HasFunction` 属性和 `InvokeFunc` 方法——封装 IPC 订阅者的反射调用能力
- 创建了 `PluginIpcGateway` internal 实现类——通过静态 `MethodInfo[]` 缓存 + `MakeGenericMethod().Invoke()` 反射创建 IPC 订阅者
- 创建了 `ReflectionPluginCallGateSubscriber` internal 实现类——通过反射缓存 `HasFunction` 属性和 `InvokeFunc` 方法，封装任意 IPC 订阅者对象
- 所有代码从 `UnsafeInvokePluginIpcOperation.cs` 第 293-369 行原样提取，无任何逻辑修改

## Task Commits

Each task was committed atomically:

1. **Task 1: 创建 public 接口文件** — `13339fe` (feat)
2. **Task 2: 创建 internal 实现类文件** — `3ff9f94` (feat)

## Files Created/Modified

- `src/DalamudMCP.Plugin/Ipc/IPluginIpcGateway.cs` — public 接口，TryCreate 方法定义
- `src/DalamudMCP.Plugin/Ipc/IPluginCallGateSubscriber.cs` — public 接口，HasFunction + InvokeFunc 定义
- `src/DalamudMCP.Plugin/Ipc/PluginIpcGateway.cs` — internal 实现，反射调用 IDalamudPluginInterface.GetIpcSubscriber()
- `src/DalamudMCP.Plugin/Ipc/ReflectionPluginCallGateSubscriber.cs` — internal 实现，反射封装 CallGateSubscriber

## Decisions Made

无额外决策——完全按照计划执行。接口位置（DalamudMCP.Plugin.Ipc）、可访问性（public/internal）、代码提取方式均遵循 CONTEXT.md 中的 D-01/D-02/D-03 决策。

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- 4 个 IPC 基础设施文件已就位，可供 11-02 和 11-03 使用
- 后续计划 11-02 将重构 `UnsafeInvokePluginIpcOperation` 使用共享 IPC 网关并注册 DI
- 后续计划 11-03 将提取测试桩并新增服务测试

---
*Phase: 11-ipc-infra*
*Completed: 2026-05-01*
