---
phase: 09-ui-language-switch
slug: ui-language-switch
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-05-01
---

# Phase 9 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 (3.2.2) with Microsoft Testing Platform |
| **Config file** | xunit.runner.json (solution root) |
| **Quick run command** | `dotnet test tests/DalamudMCP.Plugin.Tests/ --filter "PluginConfigWindowModel"` |
| **Full suite command** | `./build/test.ps1` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/DalamudMCP.Plugin.Tests/ --filter "PluginConfigWindowModel" --no-restore`
- **After every plan wave:** Run `./build/test.ps1 -NoBuild`
- **Before `/gsd-verify-work`:** Full suite must be green
- **Max feedback latency:** 30 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 09-01-01 | 01 | 1 | REQ-01, REQ-03 | — | N/A | unit | `dotnet test tests/DalamudMCP.Plugin.Tests/ --filter "PluginUiConfigurationStore"` | ❌ W0 | ⬜ pending |
| 09-01-02 | 01 | 1 | REQ-01, REQ-03 | — | N/A | unit | `dotnet test tests/DalamudMCP.Plugin.Tests/ --filter "JsonLocalization"` | ❌ W0 | ⬜ pending |
| 09-01-03 | 01 | 1 | REQ-01, REQ-03 | — | N/A | build | `dotnet build src/DalamudMCP.Plugin/ -c Debug --no-restore` | ✅ | ⬜ pending |
| 09-02-01 | 02 | 2 | REQ-02, REQ-04 | — | N/A | unit | `dotnet test tests/DalamudMCP.Plugin.Tests/ --filter "PluginConfigWindowModel"` | ❌ W0 | ⬜ pending |
| 09-02-02 | 02 | 2 | REQ-01, REQ-02, REQ-05 | — | N/A | build | `dotnet build src/DalamudMCP.Plugin/ -c Debug --no-restore` | ✅ | ⬜ pending |
| 09-02-03 | 02 | 2 | REQ-02, REQ-03 | — | N/A | build | `dotnet build src/DalamudMCP.Plugin/ -c Debug --no-restore` | ✅ | ⬜ pending |
| 09-03-01 | 03 | 3 | REQ-02, REQ-04 | — | N/A | unit | `dotnet test tests/DalamudMCP.Plugin.Tests/ --filter "PluginConfigWindowModel"` | ❌ W0 | ⬜ pending |
| 09-03-02 | 03 | 3 | REQ-01, REQ-03 | — | N/A | unit | `dotnet test tests/DalamudMCP.Plugin.Tests/ --filter "JsonLocalization"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/DalamudMCP.Plugin.Tests/PluginConfigWindowModelTests.cs` — refactor to use FakeUiLocalization instead of asserting string literals
- [ ] `tests/DalamudMCP.Plugin.Tests/JsonLocalizationTests.cs` — new file for localization dictionary loading and fallback tests
- [ ] `tests/DalamudMCP.Plugin.Tests/PluginUiConfigurationStoreTests.cs` — add test for SelectedLanguage persistence roundtrip

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Language combo box visible in config window | REQ-01 | ImGui rendering requires Dalamud runtime | Load plugin in FFXIV, open config window, verify combo box in header with "中文" and "English" options |
| Language switch updates all text immediately | REQ-02 | Requires live ImGui draw cycle | Switch language via combo box, verify all labels/buttons/status text change without closing window |
| Language preference persists after plugin reload | REQ-03 | Requires Dalamud plugin lifecycle | Select "English", reload plugin via /xlplugins, reopen config window, verify English is selected |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
