# Technology Stack -- Dalamud API Level 15 Migration

**Project:** DalamudMCP
**Milestone:** API Level 14 -> API Level 15
**Researched:** 2026-04-30
**Overall confidence:** HIGH

## Summary

API Level 15 ships with FFXIV Patch 7.5 and requires bumping three version pins: the MSBuild SDK, the packager (auto-resolved), and the manifest API level. The .NET target (`net10.0` / `net10.0-windows7.0`) and .NET SDK version (`10.0.201`) stay unchanged. No new toolchain dependencies are introduced. The only code changes needed are adapting to the three documented breaking changes (`IChatGui`, `IClientState`, `ImRaii` ref structs).

---

## Recommended Stack (API 15)

### Core Plugin Framework

| Technology | Version | Purpose | Why |
|---|---|---|---|
| `Dalamud.NET.Sdk` | **15.0.0** | MSBuild SDK for Dalamud plugin compilation | Official SDK pinned to API Level 15; available on NuGet as of API 15 release |
| `DalamudPackager` | **15.0.0** | MSBuild task for plugin manifest generation and zip packaging | Auto-referenced by `Dalamud.NET.Sdk/15.0.0`; no manual `<PackageReference>` needed |
| .NET target framework | `net10.0-windows7.0` (Plugin), `net10.0` (other projects) | Runtime target | Unchanged from API 14; API 15 remains on .NET 10.0 |
| Dalamud API Level | **15** (in `DalamudMCP.json`) | Declares runtime compatibility | Bumped from 14 to match the new SDK |

### .NET SDK

| Technology | Version | Purpose | Why |
|---|---|---|---|
| .NET SDK | **10.0.201** | Build toolchain | Unchanged from current `global.json`; no upgrade needed |
| `rollForward` | `latestFeature` | Allows minor/patch SDK updates | Unchanged |

### Unchanged Dependencies

These packages are NOT affected by the API 15 upgrade and remain at their current versions:

| Package | Version | Reason |
|---|---|---|
| `Microsoft.Extensions.DependencyInjection` | 10.0.0 | Pure .NET abstraction; no Dalamud coupling |
| `Microsoft.Extensions.Hosting` | 10.0.x | No breaking changes relevant to this upgrade |
| `MemoryPack` | 1.21.4 | IPC serialization; no Dalamud dependency |
| `Microsoft.CodeAnalysis.CSharp` | 4.14.0 | Source generator; no Dalamud dependency |
| `ModelContextProtocol` | 1.1.0 | MCP protocol SDK; no Dalamud dependency |
| `coverlet.MTP` | 8.0.0 | Test coverage; no change |
| `xunit.v3` | 3.2.2 | Test framework; no change |
| `DotNet.ReproducibleBuilds` | 1.2.39 | Build infrastructure; no change |

### Runtime Assemblies (from `DALAMUD_HOME`)

These are provided by the Dalamud runtime, not NuGet, and must point to the API 15 version of `Hooks/dev`:

| Assembly | Role |
|---|---|
| `Dalamud.dll` (v15.x) | Core Dalamud plugin API |
| `Dalamud.Bindings.ImGui.dll` | ImGui bindings |
| `Lumina.dll` / `Lumina.Excel.dll` | FFXIV game data reader |
| `FFXIVClientStructs.dll` | Client structure definitions |
| `InteropGenerator.Runtime.dll` | Interop code generation runtime |
| `Serilog.dll` | Structured logging |

---

## What Changes (and What Stays the Same)

### Files to Modify

| File | Current | Target | Action |
|---|---|---|---|
| `src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj` | `Sdk="Dalamud.NET.Sdk/14.0.2"` | `Sdk="Dalamud.NET.Sdk/15.0.0"` | Version bump in SDK attribute |
| `src/DalamudMCP.Plugin/DalamudMCP.json` | `"DalamudApiLevel": 14` | `"DalamudApiLevel": 15` | Version bump |
| `src/DalamudMCP.Plugin/packages.lock.json` | `DalamudPackager/14.0.2` | `DalamudPackager/15.0.0` | Lock file refresh |
| `src/DalamudMCP.Plugin/PluginEntryPoint.cs` | `IDalamudPlugin` | (potentially `IAsyncDalamudPlugin`) | If opting into async plugin interface |
| `src/DalamudMCP.Plugin/PluginEntryPoint.cs` (or related files) | Uses `IChatGui` with old `XivChatType` | Adapt to `IChatMessage` + `sourceKind`/`targetKind` | Breaking change fix |
| `src/.../ZoneInitEventArgs` handlers | Uses old `ActiveFestivals`/`ActiveFestivalPhases` | Use `IReadOnlyList<FestivalEntry> ActiveFestivals` | Breaking change fix |

### Files NOT to Change

| File | Reason |
|---|---|
| `global.json` | .NET SDK 10.0.201 remains compatible |
| `Directory.Build.props` | `TargetFramework=net10.0` unchanged |
| `Directory.Build.targets` | No Dalamud-specific logic |
| `DalamudPackager.targets` | The `<DalamudPackager>` task usage stays identical; the assembly is swapped by the SDK |
| All non-Plugin `.csproj` files | No Dalamud SDK dependency |
| CI solution (`DalamudMCP.CI.slnx`) | Plugin project not included in CI builds |

---

## What NOT to Use

| Technology | Why to Avoid |
|---|---|
| `DalamudPackager` as explicit `<PackageReference>` | Redundant when using `Dalamud.NET.Sdk`; the SDK includes it automatically. Current project does NOT have this reference, which is correct. |
| `Dalamud.NET.Sdk` versions < 15.0.0 | Would produce API Level 14 or lower plugins, incompatible with API 15 runtime |
| `DalamudPackager` versions < 15.0.0 | Would generate manifest with wrong `DalamudApiLevel` |
| Legacy `.csproj` style (non-SDK) | The SDK handles assembly resolution, manifest generation, and `DALAMUD_HOME` path resolution. The project already uses SDK style. |
| `TargetFramework` higher than `net10.0` | Dalamud API 15 targets .NET 10.0; higher frameworks are not supported by the runtime |

---

## Breaking Changes in API 15 (Must-Fix)

### 1. `IChatGui` -- `XivChatType` / `IChatMessage`

| Before (API 14) | After (API 15) |
|---|---|
| `OnMessage` events passed raw `XivChatType` values with packed relation data | `IChatMessage` interface exposes `sourceKind` and `targetKind` separately |
| Values above 110 were possible via packing | These must now be handled through `LogMessage` event (API 14+) |

**Impact on DalamudMCP:** Any chat observation tooling that reads or filters `XivChatType` enum values. Check `PluginEntryPoint.cs` and any `IChatGui.OnMessage` subscribers.

### 2. `IClientState` -- `ZoneInitEventArgs`

| Before (API 14) | After (API 15) |
|---|---|
| `ZoneInitEventArgs` with direct zone IDs | Uses `RowRef<Zone>`, `RowRef<TerritoryType>` via `RowRefs` |
| `ActiveFestivals` and `ActiveFestivalPhases` as separate arrays | Merged into `IReadOnlyList<FestivalEntry> ActiveFestivals` |

**Impact on DalamudMCP:** Any code reading territory/zone info or festival state at zone init. Check for `IClientState.TerritoryChanged` or zone-related event subscriptions.

### 3. `ImRaii` -- `IEndObject` removal

| Before (API 14) | After (API 15) |
|---|---|
| `IEndObject` boxing wrapper | Ref structs: `ColorDisposable`, `StyleDisposable` replace `IEndObject` for `PreDraw`/`PostDraw` properties |
| `Push...()` methods returned class instances | `Push...()` now returns ref structs (still same scope if `var` was used) |

**Impact on DalamudMCP:** Only relevant if the plugin uses ImRaii color/style pushing in UI code. Check `Ui/` directory for ImRaii usage.

---

## Optional: `IAsyncDalamudPlugin` (New in API 15)

API 15 introduces a new `IAsyncDalamudPlugin` interface as an alternative to `IDalamudPlugin`:

```csharp
// Current (IDalamudPlugin - synchronous)
public sealed class PluginEntryPoint : IDalamudPlugin { ... }

// New optional (IAsyncDalamudPlugin)
public sealed class PluginEntryPoint : IAsyncDalamudPlugin
{
    public Task LoadAsync(CancellationToken ct) { ... }
    public ValueTask DisposeAsync() { ... }
}
```

**Decision:** This is optional. The current plugin uses `IDalamudPlugin` with `LoadSync: false` (async-safe). Migrating to `IAsyncDalamudPlugin` is NOT required for API 15 compatibility. Defer unless the plugin needs async initialization for MCP server startup.

**Recommendation:** Skip for the API 15 migration milestone. Consider adopting in a future refactoring milestone if async startup latency becomes an issue.

---

## Toolchain Requirements

### DALAMUD_HOME

Must point to a directory containing API 15 assemblies:

- **Windows:** `%APPDATA%\XIVLauncher\addon\Hooks\dev` (must be API 15 version)
- **Override:** `$env:DALAMUD_HOME = 'C:\path\to\Hooks\dev\api15'`

**Verification:** The SDK reads `Dalamud.dll` from `DALAMUD_HOME`. If the directory contains API 14 assemblies, build may succeed but runtime will fail. Check `Dalamud.dll` assembly version.

### Build Commands (Unchanged)

```powershell
$env:DALAMUD_HOME = 'C:\path\to\api15\Hooks\dev'
.\build\restore.ps1
.\build\build.ps1
.\build\test.ps1
```

---

## Migration Procedure

1. Update `DalamudMCP.Plugin.csproj` SDK version
2. Update `DalamudMCP.json` API level
3. Run `dotnet restore` to refresh `packages.lock.json`
4. Fix `IChatGui` / `IClientState` / `ImRaii` breaking changes
5. Verify build and tests with API 15 DALAMUD_HOME
6. Package with Release config and verify manifest `DalamudApiLevel` is 15

---

## Alternatives Considered

| Category | Recommended | Alternative | Why Not |
|---|---|---|---|
| Plugin SDK | `Dalamud.NET.Sdk/15.0.0` | Manual `DalamudPackager` + assembly references | Manual approach requires maintaining separate assembly resolution paths; SDK is the officially recommended and supported path |
| Plugin interface | `IDalamudPlugin` (keep) | `IAsyncDalamudPlugin` | Not required; the current plugin already handles async via `Task.Run`. `IAsyncDalamudPlugin` is experimental in API 15. |
| .NET version | `net10.0` (keep) | `net9.0` or `net8.0` | Not supported by API 15; Dalamud is compiled against `net10.0` |

---

## Sources

- **HIGH** - NuGet API for `Dalamud.NET.Sdk` versions: `https://api.nuget.org/v3-flatcontainer/dalamud.net.sdk/index.json`
- **HIGH** - NuGet API for `DalamudPackager` versions: `https://api.nuget.org/v3-flatcontainer/dalamudpackager/index.json`
- **HIGH** - NuGet API confirmed `Dalamud.NET.Sdk/15.0.0` and `DalamudPackager/15.0.0` exist
- **HIGH** - Dalamud v15 documentation (via Wayback Machine): `https://dalamud.dev/versions/v15/`
  - Confirms `Dalamud.NET.Sdk v15.0.0`, `DalamudPackager v15.0.0`, API Level 15, .NET 10.0.0
  - Documents breaking changes: `IChatGui` (`IChatMessage`), `IClientState` (`RowRefs`, merged `ActiveFestivals`), `ImRaii` (ref structs)
  - Documents optional `IAsyncDalamudPlugin` interface
- **HIGH** - NuGet nuspec for `Dalamud.NET.Sdk/15.0.0`: MIT license, commit `18377d560976f9b200094b19441710486537433d`
- **HIGH** - NuGet nuspec for `DalamudPackager/15.0.0`: EUPL-1.2 license
- **MEDIUM** - Dalamud documentation site shows "15.x (API 15) [Current]" in navbar
- **MEDIUM** - Dalamud documentation reports v15 page status as finalized/current (no longer "not finalized")

---

*Stack analysis for API Level 15 migration: 2026-04-30*
