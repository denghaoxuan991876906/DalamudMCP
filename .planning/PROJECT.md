# DalamudMCP

## What This Is

DalamudMCP 是一个 FFXIV 本地 MCP 桥接插件。它在 Dalamud 插件内运行 MCP 协议服务，通过命名管道将 FFXIV 游戏状态（观察）和操作（行动）暴露给外部 AI 客户端。CLI 二进制文件充当协议桥接，支持 stdio MCP、Streamable HTTP MCP 和直接 CLI 三种模式。

## Core Value

**AI 客户端能够以结构化的方式读取 FFXIV 游戏状态并执行游戏内操作** — 如果一切失败，命名管道 IPC 桥接和操作调度必须可靠工作。

## Requirements

### Validated

- ✓ 命名管道 IPC 桥接（插件 ↔ CLI） — v0.2.0
- ✓ 基于属性的操作模型（`[Operation]`、`[CliCommand]`、`[McpTool]`） — v0.2.0
- ✓ Roslyn 源生成器自动注册操作 — v0.2.0
- ✓ 20+ 游戏内操作（观察 + 行动） — v0.2.0
- ✓ stdio MCP 服务模式 — v0.2.0
- ✓ Streamable HTTP MCP 服务模式 — v0.2.0
- ✓ 直接 CLI 模式 — v0.2.0
- ✓ Dalamud API Level 14 兼容 — v0.2.0

### Active

- [ ] 升级到 Dalamud API Level 15
- [ ] `Dalamud.NET.Sdk` 升级到 15.0.0
- [ ] `DalamudPackager` 升级到 15.0.0
- [ ] 修复 API 15 破坏性变更（如有）：
  - [ ] `IChatGui` — `XivChatType` 解析变更（新增 `sourceKind`/`targetKind`）
  - [ ] `IClientState` — `ZoneInitEventArgs` 改用 `RowRefs`，`ActiveFestivals` 合并
- [ ] `DalamudMCP.json` manifest 中 `DalamudApiLevel` 改为 15
- [ ] 所有 `packages.lock.json` 文件更新至新版本

### Out of Scope

- 新增功能或操作 — 本次仅为 API 级别升级
- FFXIVClientStructs 版本升级 — 除非 API 15 强制要求
- .NET 版本升级 — API 15 仍使用 net10.0

## Context

- **技术环境：** C#/.NET 10.0，Dalamud 插件框架，7 个源码项目 + 8 个测试项目
- **当前版本：** v0.2.0，目标 Dalamud API Level 14
- **上游依赖：** `Dalamud.NET.Sdk/14.0.2`、`DalamudPackager/14.0.2`、`MemoryPack/1.21.4`、`Microsoft.Extensions.DependencyInjection/10.0.0`
- **构建系统：** PowerShell 构建脚本（`build/`），CI 使用 GitHub Actions
- **API 15 状态：** API 15 随 FFXIV Patch 7.5 发布，`.NET` 版本保持 10.0.0

## Constraints

- **SDK 版本:** `Dalamud.NET.Sdk` 必须为 `15.0.0`
- **API Level:** `DalamudApiLevel` 必须为 `15`
- **兼容性:** 升级后插件必须在 Dalamud API 15 运行时正常加载和工作
- **构建:** 需要有效的 `DALAMUD_HOME`（指向 Hooks/dev 目录，含 API 15 引用程序集）

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| 升级到 API 15 | FFXIV Patch 7.5 要求 | — Pending |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd-transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd-complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-04-30 after initialization*
