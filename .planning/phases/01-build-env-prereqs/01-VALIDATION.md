---
phase: 1
phase_slug: build-env-prereqs
date: 2026-04-30
---

# Validation Strategy: Phase 1 — 构建环境前提确认

## Nyquist Applicability

**Nyquist 验证不适用于本阶段。**

Phase 1 是纯环境验证阶段 — 无代码变更、无测试文件、无自动化测试覆盖。所有验证均通过手动 CLI 命令执行（PowerShell + curl + dotnet CLI），结果记录为结构化工件文件。

| 维度 | 适用性 | 原因 |
|------|--------|------|
| 单元测试覆盖 | 不适用 | 无代码变更 |
| 集成测试覆盖 | 不适用 | 无代码变更 |
| 回归测试覆盖 | 不适用 | 无代码变更 |
| Nyquist/UAT 验证 | 不适用 | 纯环境前提确认 |

## 验证方式

本阶段的验证通过计划中的 3 个 task 完成：

1. **Task 1** — DALAMUD_HOME 路径解析 + Dalamud.dll 版本 (FileVersion 15.0.0.0)
2. **Task 2** — NuGet 包版本 (Dalamud.NET.Sdk/15.0.0, DalamudPackager/15.0.0) + .NET SDK (10.0.x)
3. **Task 3** — 综合验证报告编译

每个 task 的 `acceptance_criteria` 通过 `Select-String` 检查工件输出文件中的 Expected/Actual/Status 字段来验证。

## 验证工件

| 工件 | 验证内容 | Status 预期值 |
|------|----------|---------------|
| `dalamud-home-check.txt` | DALAMUD_HOME 路径 + Dalamud.dll 版本 | ResolvedPath: 非空, FileVersion: 15.0.0.0 |
| `nuget-sdk-check.txt` | NuGet 包版本可用性 | Dalamud.NET.Sdk/15.0.0: PASS, DalamudPackager/15.0.0: PASS |
| `dotnet-sdk-check.txt` | .NET SDK 版本 | 输出为 10.0.x, rollForward: PASS |
| `verification-summary.txt` | 汇总报告 | ENV-01: PASS |

---

*Validation strategy defined: 2026-04-30*
*Nyquist: NOT APPLICABLE (pure environment verification phase)*
