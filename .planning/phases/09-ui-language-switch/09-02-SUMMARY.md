---
phase: 09-ui-language-switch
plan: 02
type: execute
wave: 2
subsystem: localization
tags: i18n, localization-integration, ui-refactor

requires: [plan 09-01]
provides:
  - Localization-aware PluginConfigWindowModel with computed status getters
  - Localization-aware PluginConfigOperationRow with computed reader/exposure status
  - Fully localized PluginConfigWindow with language selector combo box
  - PluginEntryPoint language initialization from persisted config
affects: [plan 09-03]

tech-stack:
  added: []
  patterns:
    - Computed getter pattern for UI text (no caching; language switch takes effect on next ImGui frame)
    - Draw-time string prefix construction in table cells (CLI: / MCP: prefixes)
    - LanguageChanged event subscription for force-refresh on language switch

key-files:
  modified:
    - src/DalamudMCP.Plugin/Ui/PluginConfigWindowModel.cs
    - src/DalamudMCP.Plugin/Ui/PluginConfigWindow.cs
    - src/DalamudMCP.Plugin/PluginEntryPoint.cs

key-decisions:
  - "ExposureStatusText in PluginConfigOperationRow is a computed getter that relies on stored actionOperationsEnabled/unsafeOperationsEnabled bools (set via UpdateExposureStatus), rather than cached pre-formatted text"
  - "string.Format calls use CultureInfo.InvariantCulture to satisfy project's CA1305 analyzer rule (treating it as error)"
  - "Language combo box labels are in their own language ('中文' and 'English') following standard i18n pattern, not translated"

requirements-completed:
  - REQ-01 (配置窗口语言切换选项)
  - REQ-02 (切换语言后即时更新)
  - REQ-04 (操作结果和状态信息跟随语言切换)
  - REQ-05 (CLI 帮助文本随语言切换更新)

duration: ~12min
completed: 2026-05-01
---

# Phase 9 UI Language Switch -- Plan 02 Summary

**Integrated IUiLocalization into all UI layer components: PluginConfigWindowModel, PluginConfigOperationRow, PluginConfigWindow, and PluginEntryPoint. All status text computed via localization lookups, language selector added to window header, language preference persisted and restored on startup.**

## Performance

- **Duration:** ~12 minutes
- **Tasks:** 3
- **Files modified:** 3

## Accomplishments

- PluginConfigWindowModel: All 7 status text properties (`ProtocolServerStatusText`, `ActionOperationsStatusText`, `UnsafeOperationsStatusText`, `McpServerStatusText`, `McpServerEndpointText`, `McpServerErrorText`, `ReaderStatusText`) are computed getters via `loc["key"]`. Removed 4 static string fields and 2 cached status text fields.
- PluginConfigOperationRow: Added `IUiLocalization loc` field/constructor param. Removed cached `CliCommandText` and `McpToolText` properties. `ReaderStatusText` and `ExposureStatusText` are computed getters. Removed `CreateReaderStatusText` static method.
- PluginConfigWindow: All ~50 Chinese string literals replaced with `localization["key"]` across `DrawHeader`, `DrawRuntimePanel`, `DrawQuickStart`, `DrawAdvancedDetails`, `DrawServerPanel`, `DrawOperations`. Added `DrawLanguageSelector()` in header via `ImGui.Combo("##lang", ...)`. Subscribed to `LanguageChanged` event with `OnLanguageChanged` handler that calls `RefreshModel(force: true)`.
- PluginEntryPoint: Resolves `IUiLocalization` via DI (`compositionRoot.GetRequiredService`), initializes language from `configurationStore.Current.SelectedLanguage`, passes `localization` to `PluginConfigWindow` constructor.
- Operation summaries in the operations table now attempt localization lookup via `localization.GetString($"op.{operation.OperationId}.summary")` with fallback to the attribute's English `Summary`.

## Task Commits

| # | Task | Commit | Key Changes |
|---|------|--------|-------------|
| 1 | Inject IUiLocalization into PluginConfigWindowModel | `28925e6` | `IUiLocalization loc` field; all status text computed getters; simplified `ApplyStatus`, `UpdateEndpointText`, `UpdateError`, `RefreshReaderStatuses` |
| 2 | Refactor PluginConfigOperationRow + DrawOperations | `528e927` | `PluginConfigOperationRow` takes `loc`; `ReaderStatusText`/`ExposureStatusText` computed; `CliCommandText`/`McpToolText` removed; `DrawOperations` uses draw-time `localization["label.cli_prefix"]` |
| 3 | Localize PluginConfigWindow + PluginEntryPoint | `320ceec` | All ~50 string literals replaced; `DrawLanguageSelector` with ImGui.Combo; `LanguageChanged` event subscription; DI injection in PluginEntryPoint |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - CA1305] Missing CultureInfo.InvariantCulture in string.Format calls**
- **Found during:** Task 3 (final build verification)
- **Issue:** The project treats CA1305 as an error -- `string.Format(key, args)` without `IFormatProvider` causes build failure. 5 instances across PluginConfigWindowModel and PluginConfigWindow.
- **Fix:** Added `using System.Globalization` and changed all `string.Format(key, ...)` calls to `string.Format(CultureInfo.InvariantCulture, key, ...)`.
- **Files modified:** `PluginConfigWindowModel.cs`, `PluginConfigWindow.cs`
- **Committed in:** `320ceec` (Task 3)

**2. [Rule 2 - CS0414] languageSwitchPending field assigned but never consumed**
- **Found during:** Task 3 (final build verification)
- **Issue:** The `languageSwitchPending` field was set in `OnLanguageChanged` but never read, causing CS0414 error.
- **Fix:** Added consumption in `Draw()` method: reads and clears the flag each frame.
- **Files modified:** `PluginConfigWindow.cs`
- **Committed in:** `320ceec` (Task 3)

---

**Total deviations:** 2 auto-fixed (2 Rule 2 -- project analyzer requirements)
**Impact on plan:** Minimal. Both were required to satisfy the project's code analysis rules (CA1305-as-error and CS0414-as-error). No behavioral change or scope creep.

## Issues Encountered

None beyond the auto-fixed analyzer compliance issues above.

## Stub Tracking

No stubs introduced. All status text is fully wired via computed getters through the localization service. No placeholder text, empty collections, or mock data present.

## Threat Surface Scan

No additional threat surface introduced beyond what the plan's threat_model documented:
- T-09-03 (Information Disclosure): Missing keys fall back to key name in UI, accepted per plan.
- T-09-04 (Denial of Service): LanguageChanged -> RefreshModel is user-triggered, single-frame, accepted per plan.

## Self-Check: PASSED

- [x] All 3 modified files exist and contain expected changes
- [x] 3 commits confirmed in git log (28925e6, 528e927, 320ceec)
- [x] `dotnet build src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj` passes with 0 errors, 0 warnings
- [x] `IUiLocalization loc` field exists in PluginConfigWindowModel
- [x] No Chinese string literals remain in PluginConfigWindow.cs (except intentional "中文"/"English" combo labels)
- [x] `DrawLanguageSelector` method exists with ImGui.Combo
- [x] `LanguageChanged += OnLanguageChanged` subscription in constructor
- [x] PluginEntryPoint calls `GetRequiredService<IUiLocalization>` and `localization.SetLanguage(...)`
- [x] `localization` passed as first argument to `PluginConfigWindow` constructor
- [x] All computed getters use `loc["key"]` pattern
- [x] No accidental file deletions detected
- [x] No untracked files requiring attention

---
*Phase: 09-ui-language-switch*
*Completed: 2026-05-01*
