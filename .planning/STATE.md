# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-05-01)

**Core value:** AI 客户端能够以结构化的方式与 FFXIV 游戏及其他 Dalamud 插件交互，实现自动化测试
**Current focus:** v1.1 自动化测试桥接

## Current Position

Milestone: v1.1 自动化测试桥接
Phase: Not started (defining requirements)
Plan: —
Status: Defining requirements
Last activity: 2026-05-01 — Milestone v1.1 started

Progress: [ ] 0%

## Accumulated Context

### Decisions

- 被测插件不依赖 SDK，只需实现 IPC 接口约定
- 单步交互式测试流程，AI 灵活控制步骤
- 重载后不自动等待就绪，AI 端自行决定延迟

### Pending Todos

None.

### Blockers/Concerns

None.

## Deferred Items

| Category | Item | Status | Deferred At |
|----------|------|--------|-------------|
| feature | 插件自动发现（列出已安装插件及 IPC 接口） | 后续里程碑 | 2026-05-01 |
| feature | 批量测试场景执行 | 后续里程碑 | 2026-05-01 |
| feature | SDK/NuGet 包供被测插件引用 | 后续里程碑 | 2026-05-01 |