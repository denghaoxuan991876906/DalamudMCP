# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-05-01)

**Core value:** AI 客户端能够以结构化的方式与 FFXIV 游戏及其他 Dalamud 插件交互，实现自动化测试
**Current focus:** Phase 11 — IPC 基础设施提取

## Current Position

Milestone: v1.1 自动化测试桥接
Phase: 11 of 15 (IPC 基础设施提取)
Plan: 2 of 3
Status: ◆ In progress — Wave 2 complete (11-02), awaiting 11-03
Last activity: 2026-05-01 — Plan 11-02 completed (DI wiring complete)

Progress: [▓▓░░░░░░░░░░░░░░] 8%

## Performance Metrics

**Velocity:**
- Total plans completed: 17 (15 v1.0 + 2 v1.1)
- Average duration: 3.2 min (v1.1)
- Total execution time: 6.3 min (v1.1)

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| v1.0 (1-10) | 15 | — | — |
| 11-ipc-infra | 2 | 6.3 min | 3.2 min |

**Recent Trend:**
- Last 5 plans: v1.0 plans completed 2026-05-01
- Trend: Stable

*Updated after v1.1 roadmap creation*

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- 被测插件不依赖 SDK，只需实现 IPC 接口约定（降低接入门槛）
- 单步交互式测试流程，AI 灵活控制步骤（无预定义场景）
- 重载后不自动等待就绪，AI 端自行决定延迟（更灵活）
- 数据回传采用轮询模式（AI 主动 poll），不用 MCP Notification 推送（保持架构一致性）
- IPC 调用仅支持基元类型和 JSON 字符串信封（无 SDK 依赖约束）

### Pending Todos

None.

### Blockers/Concerns

- `IExposedPlugin.Reload()` 运行时行为需 Phase 12 验证（线程要求、完成时机）
- `ICallGateSubscriber` 泛型参数运行时限制需 Phase 14 验证
- `/xlreload` 是否可通过 `ICommandManager.ProcessCommand()` 派发需 Phase 13 确认
- IPC 事件订阅回调的线程模型需 Phase 15 测试

## Deferred Items

| Category | Item | Status | Deferred At |
|----------|------|--------|-------------|
| feature | 插件自动发现 | 后续里程碑 | 2026-05-01 |
| feature | 批量测试场景执行 | 后续里程碑 | 2026-05-01 |
| feature | SDK/NuGet 包供被测插件引用 | 后续里程碑 | 2026-05-01 |

## Session Continuity

Last session: 2026-05-01
Stopped at: Completed 11-02-PLAN.md (DI wiring and constructor refactoring)
Resume file: .planning/phases/11-ipc-infra/11-03-PLAN.md