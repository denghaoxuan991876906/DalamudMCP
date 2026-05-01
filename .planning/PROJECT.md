# DalamudMCP

## What This Is

DalamudMCP 是一个 FFXIV 本地 MCP 桥接插件，同时也是一个自动化测试桥梁。它在 Dalamud 插件内运行 MCP 协议服务，通过命名管道将 FFXIV 游戏状态（观察）和操作（行动）暴露给外部 AI 客户端。CLI 二进制文件充当协议桥接，支持 stdio MCP、Streamable HTTP MCP 和直接 CLI 三种模式。除游戏状态读写外，AI 可通过 MCP 重载其他插件、调用其 IPC 方法、接收回传数据，从而实现游戏内自动化测试流程。

## Core Value

**AI 客户端能够以结构化的方式与 FFXIV 游戏及其他 Dalamud 插件交互，实现自动化测试** — 如果一切失败，命名管道 IPC 桥接、操作调度和跨插件通信必须可靠工作。

## Current Milestone: v1.1 自动化测试桥接

**Goal:** 让 DalamudMCP 成为其他 Dalamud 插件的自动化测试桥梁，AI 可重载插件、调用 IPC、接收回传数据

**Target features:**
- 插件重载：AI 通过 MCP 触发指定插件的重载
- 跨插件 IPC 调用：AI 通过 MCP 调用目标插件暴露的 IPC 方法
- 数据回传：目标插件通过 IPC 向 MCP 发送数据，MCP 转发给 AI
- 斜杠命令调度：AI 通过 MCP 发送游戏内斜杠命令触发插件功能

## Requirements

### Validated

- ✓ 命名管道 IPC 桥接（插件 ↔ CLI） — v0.2.0
- ✓ 基于属性的操作模型（`[Operation]`、`[CliCommand]`、`[McpTool]`） — v0.2.0
- ✓ Roslyn 源生成器自动注册操作 — v0.2.0
- ✓ 20+ 游戏内操作（观察 + 行动） — v0.2.0
- ✓ stdio MCP 服务模式 — v0.2.0
- ✓ Streamable HTTP MCP 服务模式 — v0.2.0
- ✓ 直接 CLI 模式 — v0.2.0
- ✓ Dalamud API Level 15 兼容 — v1.0
- ✓ Dalamud 中文/英文界面语言切换 — v1.0
- ✓ 游戏日志读取能力 — v1.0

### Active

- [ ] 插件重载：AI 通过 MCP 触发指定插件的重载
- [ ] 跨插件 IPC 调用：AI 通过 MCP 调用目标插件暴露的 IPC 方法
- [ ] 数据回传：目标插件通过 IPC 向 MCP 发送数据，MCP 转发给 AI
- [ ] 斜杠命令调度：AI 通过 MCP 发送游戏内斜杠命令触发插件功能

### Out of Scope

- 插件自动发现（列出已安装插件及 IPC 接口）— 后续里程碑
- 提供 SDK/NuGet 包给被测插件 — 被测插件只需实现 IPC 接口约定
- 批量测试场景执行 — v1.1 仅支持单步交互式

## Context

- **技术环境：** C#/.NET 10.0，Dalamud 插件框架，7 个源码项目 + 8 个测试项目
- **当前版本：** v1.0（API 15 兼容）
- **上游依赖：** `Dalamud.NET.Sdk/15.0.0`、`DalamudPackager/15.0.0`、`MemoryPack/1.21.4`、`Microsoft.Extensions.DependencyInjection/10.0.0`
- **构建系统：** PowerShell 构建脚本（`build/`），CI 使用 GitHub Actions
- **测试流程：** 单步交互式 — AI 发命令→等回应→发命令→等回应
- **插件接入：** 被测插件只需实现约定的 IPC 接口，无需额外依赖

## Constraints

- **Dalamud IPC：** 跨插件通信必须走 Dalamud IPC 机制
- **插件接入：** 被测插件不引入额外 SDK 依赖，仅实现 IPC 接口约定
- **重载安全：** 插件重载由 AI 端控制时机，MCP 触发后不自动等待就绪
- **兼容性：** 必须在 Dalamud API 15 运行时正常工作

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| 升级到 API 15 | FFXIV Patch 7.5 要求 | ✓ Good |
| 被测插件不依赖 SDK | 降低接入门槛，只需实现 IPC 接口 | — Pending |
| 单步交互式测试流程 | AI 灵活控制测试步骤，无需预定义场景 | — Pending |
| 重载后不自动等待就绪 | 由 AI 端决定延迟，更灵活 | — Pending |

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
*Last updated: 2026-05-01 after milestone v1.1 initialization*
