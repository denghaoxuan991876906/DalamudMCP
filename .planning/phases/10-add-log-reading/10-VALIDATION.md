---
phase: 10
slug: add-log-reading
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-05-01
---

# Phase 10 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 3.2.2 (`xunit.v3.mtp-v2`) |
| **Config file** | implicit — `tests/DalamudMCP.Plugin.Operations.Tests` project file |
| **Quick run command** | `dotnet test tests/DalamudMCP.Plugin.Operations.Tests --filter "FullyQualifiedName~ChatLog" --no-build` |
| **Full suite command** | `dotnet test tests/DalamudMCP.Plugin.Operations.Tests` |
| **Estimated runtime** | ~5 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/DalamudMCP.Plugin.Operations.Tests --filter "FullyQualifiedName~ChatLog" --no-build`
- **After every plan wave:** Run `dotnet test tests/DalamudMCP.Plugin.Operations.Tests`
- **Before `/gsd-verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 10-01-01 | 01 | 1 | LOG-01 | N/A | N/A | unit | `dotnet test --filter "FullyQualifiedName~ChatLogEntry"` | ❌ W0 | ⬜ pending |
| 10-01-02 | 01 | 1 | LOG-01, LOG-04 | T-10-01 | Capacity enforcement against OOM DoS | unit | `dotnet test --filter "FullyQualifiedName~ChatLogBufferService"` | ❌ W0 | ⬜ pending |
| 10-02-01 | 02 | 2 | LOG-05, LOG-06, LOG-08 | T-10-02 | Input validation on channels/since/max-count | unit | `dotnet test --filter "FullyQualifiedName~ChatLogReadOperation_Carries"` | ❌ W0 | ⬜ pending |
| 10-02-02 | 02 | 2 | LOG-09 | N/A | DI chain correctness | integration | `dotnet build src/DalamudMCP.Plugin` | ❌ W0 | ⬜ pending |
| 10-03-01 | 03 | 3 | LOG-05, LOG-06, LOG-07, LOG-08 | N/A | Operation attribute metadata + executor pattern | unit | `dotnet test --filter "FullyQualifiedName~ChatLogReadOperation"` | ❌ W0 | ⬜ pending |
| 10-03-02 | 03 | 3 | LOG-02, LOG-03, LOG-04 | T-10-03 | Filter + capacity buffer behavior | unit | `dotnet test --filter "FullyQualifiedName~ChatLogBufferService"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/DalamudMCP.Plugin.Operations.Tests/ChatLogReadOperationTests.cs` — unit tests for ChatLogReadOperation attributes, executor, IPluginReaderStatus, TextFormatter
- [ ] `tests/DalamudMCP.Plugin.Tests/ChatLogBufferServiceTests.cs` — unit tests for ChatLogBufferService filtering, capacity, and edge cases

---

## Threat Model Summary

| Threat ID | Threat | STRIDE | Mitigation |
|-----------|--------|--------|------------|
| T-10-01 | max-count 过大导致 OOM | DoS | Request 规范化中 clamp maxCount 上限为 500 |
| T-10-02 | 恶意频道值导致枚举转换异常 | Tampering | 使用 TryParse 验证 channel 参数 |
| T-10-03 | since 日期值异常（未来时间戳） | Tampering | 拒绝超过当前时间 5 分钟以上的未来时间戳 |

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| 插件在 API 15 运行时中加载并订阅 IChatGui.ChatMessage | LOG-01 | 需要真实 Dalamud 运行时 | Phase 5 运行时验证中确认 |
| MCP 工具 get_chat_log 在真实 MCP 客户端中可调用 | LOG-05 | 需要 MCP 客户端连接 | Phase 6 IPC 验证中确认 |
| CLI chat read 命令输出格式正确 | LOG-05 | 需要 CLI 连接真实插件 | Phase 6 IPC 验证中确认 |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
