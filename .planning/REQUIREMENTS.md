# Requirements: DalamudMCP API 15 Migration

**Defined:** 2026-04-30
**Core Value:** AI 客户端能够以结构化的方式读取 FFXIV 游戏状态并执行游戏内操作

## v1 Requirements

Requirements for API 15 migration. Each maps to roadmap phases.

### Configuration

- [ ] **CFG-01**: `Dalamud.NET.Sdk` in `src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj` line 1 upgraded from `14.0.2` to `15.0.0`
- [ ] **CFG-02**: `DalamudApiLevel` in `src/DalamudMCP.Plugin/DalamudMCP.json` line 14 changed from `14` to `15`
- [ ] **CFG-03**: All `packages.lock.json` files regenerated to reflect `DalamudPackager/15.0.0` (3 files: Plugin, Plugin.Tests, Plugin.Operations.Tests)

### Build Environment

- [ ] **ENV-01**: `DALAMUD_HOME` verified to point to API 15 Hooks/dev directory containing `Dalamud.dll` reference assemblies
- [ ] **ENV-02**: Plugin project compiles successfully via `./build/build.ps1` with API 15 references

### Verification

- [ ] **VAL-01**: Plugin loads without errors in Dalamud API 15 runtime (`DalamudApiLevel: 15`)
- [ ] **VAL-02**: All 20+ game operations function correctly end-to-end (observation reads + action writes)
- [ ] **VAL-03**: CLI named pipe IPC bridge operates correctly — CLI connects to Plugin, dispatches operations, receives responses
- [ ] **VAL-04**: All three CLI modes verified: stdio MCP server, Streamable HTTP MCP server, direct CLI shell
- [ ] **VAL-05**: Plugin packaged zip contains accurate `DalamudMCP.json` manifest (API 15 no longer overwrites distributed manifests)

## v2 Requirements

None — this is a compatibility migration with no deferred features.

## Out of Scope

| Feature | Reason |
|---------|--------|
| Non-Dalamud dependency upgrades (MemoryPack, DI, etc.) | Scope creep — not required for API 15 compatibility |
| Architecture refactoring or layer reorganization | Six of seven projects have zero Dalamud dependency; no structural changes needed |
| IPC protocol changes | Protocol v2.0.0 is API-level agnostic |
| `IAsyncDalamudPlugin` migration | Not required for API 15; existing synchronous pattern still supported |
| DI container simplification via `IDalamudPluginInterface` | Pattern works as-is; no API 15 requirement to change |
| FFXIVClientStructs version upgrade | Separate concern — only required if Patch 7.5 struct layouts actually changed |

## Traceability

| Requirement | Phase |
|-------------|-------|
| CFG-01 | Pending |
| CFG-02 | Pending |
| CFG-03 | Pending |
| ENV-01 | Pending |
| ENV-02 | Pending |
| VAL-01 | Pending |
| VAL-02 | Pending |
| VAL-03 | Pending |
| VAL-04 | Pending |
| VAL-05 | Pending |

**Coverage:**
- v1 requirements: 10 total
- Mapped to phases: 0
- Unmapped: 10 ⚠️

---
*Requirements defined: 2026-04-30*
*Last updated: 2026-04-30 after initial definition*
