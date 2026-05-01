---
phase: 12
slug: plugin-reload
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-05-01
---

# Phase 12 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit (.NET) |
| **Config file** | xunit.runner.json (仓库根目录) |
| **Quick run command** | `.dotnet\dotnet.exe test tests/DalamudMCP.Plugin.Operations.Tests/DalamudMCP.Plugin.Operations.Tests.csproj --filter "FullyQualifiedName~PluginReloadOperation" --no-restore` |
| **Full suite command** | `.dotnet\dotnet.exe test tests/DalamudMCP.Plugin.Operations.Tests/DalamudMCP.Plugin.Operations.Tests.csproj --no-restore` |
| **Estimated runtime** | ~10 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet build --no-restore`
- **After every plan wave:** Run `dotnet test --filter PluginReloadOperation --no-build`
- **Before `/gsd-verify-work`:** Full suite must be green
- **Max feedback latency:** 30 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 12-01-01 | 01 | 1 | RELOAD-01 | T-12-01, T-12-02, T-12-03, T-12-04 | plugin_name 非空验证、自身重载硬阻止、状态码安全返回 | 编译 | `dotnet build src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj --no-restore` | ❌ W0 | ⬜ pending |
| 12-01-02 | 01 | 1 | RELOAD-01 | T-12-01 | 暴露策略仅添加安全操作 ID | 编译 | `dotnet build src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj --no-restore` | ✅ | ⬜ pending |
| 12-02-01 | 02 | 2 | RELOAD-01 | — | 测试桩不涉及安全威胁 | 编译 | `dotnet build tests/DalamudMCP.Plugin.Operations.Tests/DalamudMCP.Plugin.Operations.Tests.csproj --no-restore` | ❌ W0 | ⬜ pending |
| 12-02-02 | 02 | 2 | RELOAD-01 | — | 单元测试验证所有状态码路径和输入验证 | 单元测试 | `dotnet test tests/DalamudMCP.Plugin.Operations.Tests/DalamudMCP.Plugin.Operations.Tests.csproj --filter "PluginReloadOperation" --no-build` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `src/DalamudMCP.Plugin/Operations/PluginReloadOperation.cs` — 插件重载操作实现 (RELOAD-01)
- [ ] `tests/DalamudMCP.Plugin.Operations.Tests/TestShared/Ipc/FakeExposedPlugin.cs` — IExposedPlugin 测试桩
- [ ] `tests/DalamudMCP.Plugin.Operations.Tests/PluginReloadOperationTests.cs` — PluginReloadOperation 单元测试

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| 实际 Dalamud 运行时重载行为 | RELOAD-01 | `IExposedPlugin.Reload()` 的真实行为只能在 FFXIV 游戏内验证 | 在游戏内加载 DalamudMCP，通过 MCP `reload_plugin` 工具重载 SamplePlugin，观察是否正常 unload→reload |
| IPC 通道恢复时间 | RELOAD-01 | 恢复时间因插件而异，无法自动化测试 | 重载后轮询 IPC 通道状态，记录恢复耗时 |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
