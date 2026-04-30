---
phase: 09-ui-language-switch
plan: 01
subsystem: localization
tags: i18n, json, embedded-resource, di

requires: []
provides:
  - IUiLocalization interface (DI-registered localization service contract)
  - JsonLocalization implementation (embedded JSON dictionary loader)
  - en.json / zh.json bilingual dictionary files (85 keys each)
  - PluginUiConfiguration.SelectedLanguage property (config version 4)
  - DI singleton registration of IUiLocalization in PluginServiceCollectionExtensions
  - csproj EmbeddedResource declarations for lang/*.json
affects: [plan 09-02, plan 09-03]

tech-stack:
  added: []
  patterns:
    - JSON-based localization service with interface abstraction
    - Assembly.GetManifestResourceStream for embedded resource loading
    - Language change event-driven refresh pattern

key-files:
  created:
    - src/DalamudMCP.Plugin/Ui/Localization/IUiLocalization.cs
    - src/DalamudMCP.Plugin/Ui/Localization/JsonLocalization.cs
    - src/DalamudMCP.Plugin/lang/en.json
    - src/DalamudMCP.Plugin/lang/zh.json
  modified:
    - src/DalamudMCP.Plugin/Configuration/PluginUiConfiguration.cs
    - src/DalamudMCP.Plugin/Hosting/PluginServiceCollectionExtensions.cs
    - src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj

key-decisions:
  - "Enriched exception thrown (InvalidOperationException) when embedded resource is missing at runtime, providing immediate diagnostic feedback"
  - "GetString falls back to key name when key missing from both dictionaries, aiding debugging of missing translations"
  - "SetLanguage silently rejects values other than 'en'/'zh', matching threat model T-09-02"

patterns-established:
  - "Interface + sealed implementation for localization service, DI-registered as singleton"
  - "Flat Dictionary<string, string> JSON format with dot-delimited keys for UI string IDs"
  - "C# string.Format {0}/{1} placeholders in dictionary values (not ICU format)"

requirements-completed:
  - REQ-01 (配置窗口语言切换选项)
  - REQ-03 (语言偏好持久化)

duration: 4min
completed: 2026-04-30
---

# Phase 9 UI Language Switch — Plan 01 Summary

**Localization service infrastructure with IUiLocalization interface, JsonLocalization implementation, 85-key bilingual JSON dictionaries, config-side SelectedLanguage persistence, and DI registration**

## Performance

- **Duration:** 3m 41s
- **Started:** 2026-04-30T16:52:53Z
- **Completed:** 2026-04-30T16:56:34Z
- **Tasks:** 3 executed + 1 polish
- **Files created:** 4
- **Files modified:** 3

## Accomplishments

- IUiLocalization interface defined with `GetString`, `SetLanguage`, `CurrentLanguage`, indexer, and `LanguageChanged` event
- JsonLocalization sealed class loads `en.json`/`zh.json` from embedded resources at construction; falls back chain: zh -> en -> key name
- SetLanguage restricts to `"en"`/`"zh"` only, matching threat model T-09-02 (spoofing mitigation)
- 85 bilingual key-value pairs created in both `en.json` and `zh.json` with identical key sets (verified)
- PluginUiConfiguration version bumped to 4 with `SelectedLanguage` property defaulting to `"zh"`
- DI registration added in PluginServiceCollectionExtensions: `services.AddSingleton<IUiLocalization, JsonLocalization>()`
- Both JSON files declared as EmbeddedResource in csproj

## Task Commits

1. **Task 1: Create IUiLocalization and JsonLocalization** - `ae689aa` (feat)
2. **Task 2: Create en.json and zh.json** - `d935ad1` (feat)
3. **Task 3: Config, DI, csproj changes** - `b18b894` (feat)
4. **Post-task polish: XML doc comments, formatting** - `800cb34` (docs)

## Files Created

- `src/DalamudMCP.Plugin/Ui/Localization/IUiLocalization.cs` — Interface with `this[string]`, `GetString`, `CurrentLanguage`, `SetLanguage`, `LanguageChanged` event
- `src/DalamudMCP.Plugin/Ui/Localization/JsonLocalization.cs` — Sealed implementation loading embedded JSON dictionaries; fallback chain: zh -> en -> key name
- `src/DalamudMCP.Plugin/lang/en.json` — 85 English string pairs covering all UI categories
- `src/DalamudMCP.Plugin/lang/zh.json` — 85 Chinese string pairs with identical key set

## Files Modified

- `src/DalamudMCP.Plugin/Configuration/PluginUiConfiguration.cs` — Version 3 -> 4; added `SelectedLanguage` property (default `"zh"`)
- `src/DalamudMCP.Plugin/Hosting/PluginServiceCollectionExtensions.cs` — Added `using DalamudMCP.Plugin.Ui.Localization` and `services.AddSingleton<IUiLocalization, JsonLocalization>()`
- `src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj` — Added `<EmbeddedResource Include="lang\en.json" />` and `<EmbeddedResource Include="lang\zh.json" />` in existing ItemGroup

## Decisions Made

- Enriched exception (`InvalidOperationException`) thrown when embedded resource is missing at runtime for immediate diagnostic feedback
- `GetString` returns the key name itself when the key is missing from both dictionaries, aiding debugging of missing translations
- `SetLanguage` silently rejects values other than `"en"`/`"zh"`, matching threat model T-09-02
- Flat `Dictionary<string, string>` JSON format with dot-delimited keys following standard i18n conventions

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] IDE0040 requires explicit `public` on interface members**
- **Found during:** Task 1 (initial build)
- **Issue:** The project's code analysis (IDE0040) requires explicit accessibility modifiers on interface members. The initial code used implicit `public` which is valid C# but fails this project's analyzer rules.
- **Fix:** Added `public` to each interface member: `this[string]`, `GetString`, `CurrentLanguage`, `SetLanguage`, `LanguageChanged`
- **Files modified:** `src/DalamudMCP.Plugin/Ui/Localization/IUiLocalization.cs`
- **Verification:** Build passes with 0 errors, 0 warnings
- **Committed in:** `ae689aa` (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 bug)
**Impact on plan:** Minimal. Required to pass project's code analysis rules. No scope creep or behavioral change.

## Issues Encountered

None — all tasks executed as specified with only the IDE0040 code analysis fix needed.

## Stub Tracking

No stubs were introduced. The localization implementation and JSON dictionaries are fully populated. No placeholder text, empty collections, or mock data present.

## Threat Surface Scan

No additional threat surface introduced beyond what the plan's threat_model documented:
- T-09-01 (Tampering): Mitigated by embedded resource loading from own assembly
- T-09-02 (Spoofing): Mitigated by SetLanguage value restriction to "en"/"zh"

## Next Phase Readiness

- Localization service layer complete and available via DI
- 85-key bilingual dictionaries ready for consumption by Plan 09-02 (window/model localization integration)
- All downstream consumers (PluginConfigWindow, PluginConfigWindowModel) can now inject IUiLocalization

## Self-Check: PASSED

- All 4 new files exist and contain required content
- All 3 modified files exist with expected changes
- 4 commits confirmed in git log
- Build passes with 0 errors, 0 warnings
- 85 keys in both en.json and zh.json with matching key sets
- `window.title` present in both JSON files
- `SelectedLanguage` present in PluginUiConfiguration
- `AddSingleton<IUiLocalization, JsonLocalization>` present in service collection
- `EmbeddedResource` entries present in csproj
- No untracked files (except .claude/ agent metadata)
- No accidental file deletions

---
*Phase: 09-ui-language-switch*
*Completed: 2026-04-30*
