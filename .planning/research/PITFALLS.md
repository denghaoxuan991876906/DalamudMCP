# Pitfalls Research: Dalamud API 14 → 15 Migration

**Domain:** FFXIV Dalamud API version migration (MCP bridge plugin)
**Researched:** 2026-04-30
**Confidence:** MEDIUM

## Critical Pitfalls

### Pitfall 1: Silently Broken `IClientState` Dependencies

**What goes wrong:**
The plugin injects `IClientState` into 20+ operation classes via constructor DI. If any member of `IClientState` changed signature (not just `ZoneInitEventArgs`), the code compiles against the old API assemblies but fails at runtime when loaded into API 15. The failure could be a `MissingMethodException`, a silently null return, or wrong data from a changed struct layout.

**Why it happens:**
The plugin projects reference `Dalamud.NET.Sdk/14.0.2` which resolves Dalamud assemblies from the local `DALAMUD_HOME` / `Hooks/dev/` directory. When upgrading the SDK version to `15.0.0`, the resolved assemblies change. However, if `DALAMUD_HOME` still points to an API 14 directory (or is missing), the build might use cached or incorrect assemblies, producing a binary that looks correct but fails when loaded into an API 15 Dalamud runtime.

Compile-time API compat checking (`Microsoft.DotNet.ApiCompat.Tool` is used upstream) does not help if the wrong assemblies are resolved during build.

**How to avoid:**
1. Update `DALAMUD_HOME` to point to an API 15 runtime BEFORE updating the SDK version.
2. After SDK bump, do a clean build (`dotnet clean` + `dotnet restore` + `dotnet build`).
3. Verify the resolved assembly versions in build output — they should show API 15 assemblies.
4. Run ALL operation tests against a live API 15 Dalamud instance, not just compilation tests.

**Warning signs:**
- Build succeeds with warnings about deprecated/obsolete members
- `dotnet restore` shows package version conflicts between 14.x and 15.x packages
- `packages.lock.json` still references `DalamudPackager/[14.0.2, )`
- The `Hooks/dev/` directory contains API 14 assemblies

**Phase to address:**
Phase 1 (SDK & Assembly Upgrade) — must be verified before any code changes are attempted.

---

### Pitfall 2: XivChatType Semantic Break — Unnoticed Chat Parsing Regression

**What goes wrong:**
In API 15, `XivChatType` in `IChatGui.OnMessage` events is now properly parsed. Previously it contained packed relation data (source/target kind embedded in values above 110). The enum now represents pure `LogKind` sheet rows, with the relation data exposed via new `sourceKind` / `targetKind` parameters. If the plugin does not currently subscribe to `IChatGui` chat events, this pitfall is dormant. But if any operation or future feature parses chat messages by matching `XivChatType` values, the old packed values will no longer appear, breaking filters and type detection silently.

**Why it happens:**
Developers often use chat type enum values directly without checking whether they are "raw" (pre-API 15) or "resolved" (API 15). Comparisons like `chatType == XivChatType.Say` or `chatType >= (XivChatType)110` will behave differently after migration because the packed bits are stripped.

**How to avoid:**
1. Audit the full codebase for any `XivChatType` usage — both direct enum comparisons and numeric casts.
2. If chat parsing exists, migrate to use the new `sourceKind`/`targetKind` parameters introduced in API 15.
3. Consider using the `LogMessage` event (introduced in API 14) which already provides cleaner parsing.
4. Add explicit test cases that verify chat type detection against known message patterns.

**Warning signs:**
- Any switch/if chain on `XivChatType` values
- Numeric comparisons against `XivChatType` (e.g., `(int)type > 110`)
- Custom chat log filtering logic

**Phase to address:**
Phase 2 (API Compatibility Audit) — after SDK upgrade, audit all Dalamud service usage.

---

### Pitfall 3: `IClientState` API Surface Changes Silently Breaking Operations

**What goes wrong:**
API 15 changes `ZoneInitEventArgs` to use `RowRef`s and merges `ActiveFestivals` / `ActiveFestivalPhases` arrays into a single `IReadOnlyList<FestivalEntry> ActiveFestivals`. The plugin injects `IClientState` into 24 operation files. Any operation that subscribes to zone change events or reads territory/festival data will either fail to compile (good — caught early) or compile but return wrong data (bad — silent semantic change).

**Why it happens:**
`IClientState` is injected as a constructor parameter and passed through to operation `ExecuteAsync` methods. The migration pattern from API 13→14 showed that `IClientState` had `LocalPlayer` and `LocalContentId` obsoleted/moved to other services. API 15 continues this trend of reorganizing `IClientState` responsibilities. The changes may be subtle enough that the code compiles but produces different results.

**How to avoid:**
1. Build against API 15 assemblies and fix ALL compilation errors before runtime testing.
2. For each operation that uses `IClientState`, trace what members it accesses and verify each one against API 15 docs.
3. Pay special attention to:
   - `TerritoryType` / zone-related properties (may now return `RowRef` instead of raw values)
   - `LocalPlayer` — verify still available or migrated to `IObjectTable`
   - Special properties like `IsPvP`, `IsInHomeWorld` — any that changed behavior
4. Add compile-time API compat validation — build against API 15 reference assemblies, not the runtime assemblies.

**Warning signs:**
- Build warnings about `[Obsolete]` members on `IClientState`
- Properties returning new wrapper types instead of raw primitives
- Zone change event handler signatures changed
- Any `RowRef` usage where raw IDs were previously expected

**Phase to address:**
Phase 2 (API Compatibility Audit) — systematic audit of all `IClientState` access patterns.

---

### Pitfall 4: Sync-over-Async Deadlock in New Framework Thread Constraints

**What goes wrong:**
The codebase already has 5 known sync-over-async locations (`.GetAwaiter().GetResult()` usage). API 15 may introduce stricter thread affinity checks — `IObjectTable` in API 12+ throws `InvalidOperationException` if accessed off the main thread. If API 15 extends this to other services (e.g., `IClientState`), the existing sync-over-async patterns will deadlock or throw at runtime instead of silently working.

The critical path is `PluginEntryPoint.cs:61` which blocks synchronously on `compositionRoot.StartAsync().GetAwaiter().GetResult()` during plugin construction. If this runs on a Dalamud internal thread (not the framework thread), and `StartAsync` accesses any thread-sensitive API, this deadlocks.

**Why it happens:**
Dalamud's custom `TaskScheduler` queues work to the framework thread. When you call `.GetAwaiter().GetResult()` on a task that was scheduled via this scheduler, the calling thread blocks waiting for the task, but the task can't execute because it's waiting for the framework thread — which is the blocked calling thread. This is a classic deadlock.

API 15 may tighten thread-safety requirements further, making patterns that "happened to work" in API 14 start failing.

**How to avoid:**
1. Convert all 5 sync-over-async sites to use proper `await`:
   - `PluginEntryPoint.cs:61` — use async factory pattern instead of blocking in constructor
   - `PluginEntryPoint.cs:97` — implement `IAsyncDisposable` properly
   - `PluginMcpServerController.cs:195,559` — make the probe methods fully async
   - `CliMcpServerRunner.cs:40-43` — restructure to avoid `Task.Run + GetAwaiter`
2. For the plugin entry point specifically: use `OnLoad` or an async initialization event instead of doing async work in the constructor.
3. Switch from `Thread.Sleep` to `Task.Delay` in polling patterns (concern already logged in CONCERNS.md).

**Warning signs:**
- Any `.GetAwaiter().GetResult()` or `.Result` or `.Wait()` call
- Plugin freezes on load (indicates deadlock in constructor)
- "Operation is not valid" exceptions mentioning thread affinity at runtime

**Phase to address:**
Phase 3 (Thread Safety Fixes) — after API compatibility audit, fix deadlock-waiting-to-happen before they manifest in API 15.

---

### Pitfall 5: Plugin Manifest Mismatch — Plugin Fails to Load With No Error

**What goes wrong:**
The plugin manifest (`DalamudMCP.json`) declares `"DalamudApiLevel": 14`. After SDK upgrade, if this is not updated to 15, Dalamud will refuse to load the plugin entirely. The user sees the plugin in the installer but it fails silently — no window, no error message, just "Failed to load." The `InternalName.json` inside the plugin zip must also match the repository manifest starting from API 15.

**Why it happens:**
Dalamud's API level check happens at load time, before any plugin code runs. If the API level in the manifest does not match the runtime's API level, the plugin is rejected. The error message is not user-visible in the default UI; users must check the Dalamud log.

Additionally, API 15 changes the manifest behavior: `InternalName.json` inside the plugin zip is no longer overwritten by the repository manifest. If the zip contains a stale `InternalName.json`, it takes precedence and may cause a mismatch.

**How to avoid:**
1. Update `DalamudMCP.json` line 14 to `"DalamudApiLevel": 15` as part of Phase 1.
2. Remove any stale `InternalName.json` from the build output before packaging.
3. Verify the manifest in the final packaged zip (`latest.zip`) has the correct API level.
4. Add a build step that validates the manifest API level matches the SDK version.
5. Test load the plugin against a known-working API 15 Dalamud installation.

**Warning signs:**
- Plugin appears in installer but fails to load without UI feedback
- Dalamud log shows "API Level mismatch" or "Invalid manifest"
- `latest.zip` contains an old `InternalName.json` from a previous build

**Phase to address:**
Phase 1 (SDK & Assembly Upgrade) — verified during build validation.

---

### Pitfall 6: DALAMUD_HOME Pointing to Wrong Runtime

**What goes wrong:**
The build scripts (`build.ps1`, etc.) use `DALAMUD_HOME` environment variable to locate the Dalamud reference assemblies. If `DALAMUD_HOME` points to a directory containing API 14 assemblies, the plugin compiles against the wrong API level. The build succeeds, but the plugin fails at runtime when loaded into API 15 Dalamud — with confusing `MissingMethodException` or `TypeLoadException` errors.

**Why it happens:**
XIVLauncher's `Hooks/dev/` directory contains the current dev assemblies. When API 15 releases, the developer's machine may still have API 14 assemblies if they have not updated XIVLauncher to the API 15 version. Or they may have both versions and the wrong one is referenced.

The `.csproj` uses `Dalamud.NET.Sdk/14.0.2` which resolves assemblies from the SDK's pinned path — but the actual resolution still depends on `DALAMUD_HOME` finding the correct runtime assemblies for the Dalamud service implementations.

**How to avoid:**
1. Before beginning migration, verify `DALAMUD_HOME` path exists and confirm it contains API 15 assemblies.
2. Add a build validation step that checks the Dalamud assembly version against the expected API level.
3. Document the required `DALAMUD_HOME` setup in the migration README.
4. Consider using `DALAMUD_HOME` only for runtime testing, and relying on SDK NuGet packages for reference assemblies.
5. After SDK upgrade, run `.\build\restore.ps1` with explicit `-DalamudHome` pointing to API 15.

**Warning signs:**
- Build output shows `Dalamud.dll` version 14.x instead of 15.x
- `dotnet build --no-restore` with no errors but runtime fails
- `packages.lock.json` shows `DalamudPackager` version `14.0.2`
- `Debug.WriteLine` or runtime `TypeLoadException` for Dalamud types

**Phase to address:**
Phase 0 (Prerequisite Verification) — must be verified before any code changes.

---

### Pitfall 7: CI Cannot Validate the Migration

**What goes wrong:**
The CI solution (`DalamudMCP.CI.slnx`) explicitly excludes the Plugin project and its tests because they require the Dalamud SDK. The entire migration — SDK upgrade, API changes, manifest update — happens in code that CI never compiles. A broken migration passes CI, gets merged to main, and nobody discovers the failure until they build locally with DALAMUD_HOME pointing to API 15.

**Why it happens:**
This is a pre-existing CI gap documented in CONCERNS.md. The Plugin project's dependency on `Dalamud.NET.Sdk` means it cannot compile in CI environments without a Dalamud installation. CI only builds the `DalamudMCP.Framework`, `DalamudMCP.Protocol`, and `DalamudMCP.Cli` projects (and their tests), none of which reference the Dalamud API.

**How to avoid:**
1. Accept that CI won't catch migration issues and plan for manual local validation.
2. Create a detailed local validation checklist that must be signed off before merging.
3. Consider adding a CI step that installs the Dalamud SDK in CI (using `dotnet restore` with a NuGet config that knows where to find the SDK package) — at minimum to verify compilation.
4. Document the CI gap explicitly in the migration PR to prevent accidental merges of broken code.

**Warning signs:**
- CI passes but plugin doesn't compile locally
- PR merged with "CI passes" claim but plugin is broken
- No Plugin project tests run in CI (this is already known but easy to forget)

**Phase to address:**
Phase 0 (Prerequisite Verification) — acknowledge and document before starting.

---

### Pitfall 8: MemoryPack Protocol Version Out of Sync After Build

**What goes wrong:**
The protocol between the CLI and Plugin uses `MemoryPack` binary serialization. After the SDK upgrade, `packages.lock.json` must be regenerated. If the regeneration pins a different `MemoryPack` version (or the existing `1.21.4` is incompatible with `Dalamud.NET.Sdk/15.0.0`'s transitive dependencies), the CLI and Plugin may end up using different `MemoryPack` versions. Binary serialization between them then fails with cryptic deserialization errors.

**Why it happens:**
The `packages.lock.json` files are per-project and must all be in sync. The Plugin project depends on `Dalamud.NET.Sdk` which transitively depends on various NuGet packages. Different versions of `MemoryPack` have different binary layouts. If the CLI (which doesn't reference Dalamud) gets a different `MemoryPack` version than the Plugin, messages cannot be deserialized.

**How to avoid:**
1. After upgrading SDK, run `dotnet restore` for ALL projects and regenerate all `packages.lock.json` files.
2. Verify `MemoryPack` version is consistent across all project lock files.
3. Run the protocol round-trip tests (if they exist) or perform a manual IPC test with CLI + Plugin.
4. If API 15 introduces new `MemoryPack` transitive dependency constraints, pin it explicitly in Directory.Packages.props.

**Warning signs:**
- `packages.lock.json` files show different `MemoryPack` versions
- IPC messages fail with "unknown discriminator" or deserialization errors
- CLI connects to pipe but all responses are garbled

**Phase to address:**
Phase 1 (SDK & Assembly Upgrade) — validated during integration testing.

---

### Pitfall 9: Unused/Removed NuGet Dependencies From SharpDX Legacy

**What goes wrong:**
In API 14, SharpDX dependencies were removed (replaced by TerraFX). In API 15, there may be additional dependency removals. If the plugin or any of its operations reference a removed package (e.g., via transitive dependency or direct reference), compilation breaks or runtime `FileNotFoundException` occurs.

This is particularly relevant for the screenshot operation (`GameScreenshotOperation.cs`) which uses `System.Drawing.Common` via GDI interop — `System.Drawing.Common` was removed in API 14.

**Why it happens:**
Dalamud historically prunes unmaintained dependencies with each API level. API 14 removed `SharpDX.Direct3D11`, `SharpDX.Mathematics`, `System.Collections.Immutable`, `System.Drawing.Common`, and `System.Resources.Extensions`. API 15 may continue this cleanup.

**How to avoid:**
1. Check the API 15 changelog for any dependency removals.
2. Audit direct and transitive dependencies for any that overlap with known Dalamud-removed packages.
3. Explicitly reference any dependency that was previously resolved transitively through Dalamud.
4. For `System.Drawing.Common` — the screenshot operation uses GDI `BitBlt` which doesn't require this package, but verify no code path accidentally depends on it.

**Warning signs:**
- `FileNotFoundException` for assemblies like `SharpDX.*`, `System.Drawing.Common`
- Warnings about "dependency resolution" during build
- Transitive dependency versions changing after restore

**Phase to address:**
Phase 2 (API Compatibility Audit) — dependency audit alongside API surface audit.

---

### Pitfall 10: FFXIVClientStructs Version Mismatch

**What goes wrong:**
API 15 ships with FFXIVClientStructs updated for Patch 7.5. The plugin operations that use `unsafe` FFXIVClientStructs pointers (AddonInputOperation, AddonSelectMenuItemOperation, InteractWithTargetOperation, etc.) depend on exact memory layouts. If the FFXIVClientStructs version in API 15 changes struct layouts, these operations will read garbage memory, crash, or corrupt game state.

**Why it happens:**
The `Dalamud.NET.Sdk/15.0.0` transitively references a specific version of `FFXIVClientStructs`. If the project also has a direct `FFXIVClientStructs` reference (or an outdated `packages.lock.json`), there can be a version conflict. Even if resolved, the struct definitions may have changed for Patch 7.5.

**How to avoid:**
1. Check if FFXIVClientStructs version is explicitly pinned in the project or resolved transitively.
2. If directly referenced, update to the version that ships with API 15.
3. Run all unsafe operation tests against Patch 7.5 game data.
4. Do not assume struct layouts are backwards-compatible — test each operation manually.

**Warning signs:**
- `EntryPointNotFoundException` or `AccessViolationException` in unsafe operations
- Operations return garbled data (wrong field offsets)
- Game crash when invoking addon operations

**Phase to address:**
Phase 4 (Operation Validation) — runtime testing of all unsafe operations.

---

## Technical Debt Patterns

Shortcuts that seem reasonable but create long-term problems during API migration.

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Skipping clean build after SDK upgrade | Faster iteration | Resolved stale assemblies may cause runtime failures | NEVER during migration |
| Bumping `DalamudApiLevel` without testing all operations | Quick manifest change | Broken operations shipped to users silently | NEVER |
| Only building Debug, not Release | Faster turnaround | Release config may resolve dependencies differently | Only for initial compilation check |
| Relying on `DALAMUD_HOME` default path | No config needed | Wrong assemblies if user has multiple XIVLauncher installs | Acceptable with validation step |
| Not regenerating `packages.lock.json` | Saves 30 seconds | Version drift between CLI and Plugin protocol | NEVER |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| `DALAMUD_HOME` path resolution | Assuming environment variable is set or pointing to correct API level | Explicitly verify path contents before building |
| SDK version vs API level | Confusing `Dalamud.NET.Sdk` version (15.0.0) with `DalamudApiLevel` (15) — they must match | Both are semantically "15" but check both independently |
| Plugin project exclusion from CI | Assuming CI tests cover Plugin project | CI excludes Plugin entirely — must test locally |
| Third-party IPC plugins (Lifestream, Vnavmesh) | Assuming they work with API 15 immediately | Third-party plugins may not be API 15 compatible at same time; add graceful fallback |
| Protocol version negotiation | Bumping `MemoryPack` version but not `ProtocolContract.CurrentVersion` | Update `ProtocolContract.CurrentVersion` if wire format changes |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Sync-over-async blocking framework thread | Plugin freezes on load or during operations | Replace `.GetAwaiter().GetResult()` with async patterns | Immediately if API 15 adds thread affinity checks |
| `Thread.Sleep` in startup polling | Startup delay blocks game frame | Replace with `Task.Delay` + async | Always — it's already a concern in the codebase |
| Full rebuild of source generator on every compilation | Slow build iteration | Add incremental generator support | Every build |
| Not using `IFramework.RunOnTick` for thread marshaling | `InvalidOperationException` from thread-sensitive APIs | Check `IsInFrameworkUpdateThread` before accessing game state | API 12+ behavior, exacerbated by API 15 |

---

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Forgetting plugin manifest is no longer overwritten by repo (API 15 change) | Stale `InternalName.json` in zip can bypass API level check | Remove stale manifest from build output before packaging |
| Not verifying AssemblyLoadContext isolation | Old Dalamud DLLs in plugin output folder cause "Nothing inherits from IDalamudPlugin" error | Set `<Private>false</Private>` on all Dalamud references |
| Exposing API level mismatch to user as silent failure | User has no way to diagnose why plugin won't load | Surface error via Dalamud's crash handler or log |

---

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| Plugin fails to load after API upgrade with no UI message | User thinks plugin is broken/unmaintained | Add `TestingDalamudApiLevel` to manifest for gradual rollout |
| Third-party IPC plugins incompatible with API 15 | Teleport, navigation operations silently fail | Add runtime detection of IPC plugin version compatibility |
| API 15 changes behavior of game operations | User notices different results without understanding why | Document behavioral changes in plugin changelog |

---

## "Looks Done But Isn't" Checklist

Things that appear complete but are missing critical pieces.

- **[SDK Upgrade]:** Often missing `packages.lock.json` regeneration — verify all 6 lock files have consistent version pins.
- **[Manifest Update]:** Often missing `DalamudApiLevel` change — verify `DalamudMCP.json` line 14 shows `15`.
- **[API Audit]:** Often missing `IClientState` usage audit in operations — verify all 24+ files that inject it handle any changed members.
- **[Thread Safety]:** Often missing sync-over-async fix — verify all 5 `.GetAwaiter().GetResult()` sites are converted to async.
- **[Protocol Compatibility]:** Often missing CLI ↔ Plugin round-trip test — verify IPC still works after dependency version changes.
- **[Unsafe Operations]:** Often missing FFXIVClientStructs version validation — verify struct layouts match Patch 7.5.
- **[Third-Party IPC]:** Often missing fallback behavior — verify Lifestream/Vnavmesh-dependent operations degrade gracefully if those plugins haven't updated to API 15.

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Sync-over-async deadlock | LOW (code change) | Convert to `await`, test with task scheduler simulation |
| Manifest API level wrong | LOW (repackage) | Fix JSON, rebuild, repackage |
| `IClientState` member changed | LOW (code change) | Update member access, recompile |
| MemoryPack version mismatch | MEDIUM (regenerate + retest) | Regenerate all lock files, verify version consistency |
| FFXIVClientStructs layout wrong | HIGH (research + test) | Check struct diffs for Patch 7.5, update access patterns, re-test all unsafe ops |
| `DALAMUD_HOME` pointing to wrong runtime | LOW (environment fix) | Update env var, clean build |
| Plugin fails to load silently | MEDIUM (diagnose + fix) | Check Dalamud log, verify manifest, verify SDK version |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| Silently broken IClientState dependencies | Phase 1 (SDK & Assembly Upgrade) | Clean build against API 15 assemblies, run all operation tests |
| XivChatType semantic break | Phase 2 (API Compatibility Audit) | Code review all chat type usage, test chat events |
| IClientState API surface changes | Phase 2 (API Compatibility Audit) | Trace each IClientState member access, verify against API 15 docs |
| Sync-over-async deadlock | Phase 3 (Thread Safety Fixes) | Convert all 5 sync-over-async sites, test with async stress |
| Plugin manifest mismatch | Phase 1 (SDK & Assembly Upgrade) | Verify `DalamudMCP.json` API level, check packaged zip |
| DALAMUD_HOME wrong runtime | Phase 0 (Prerequisite Verification) | Verify path points to API 15, validate assembly versions |
| CI cannot validate migration | Phase 0 (Prerequisite Verification) | Create local validation checklist, document CI gap |
| MemoryPack protocol out of sync | Phase 1 (SDK & Assembly Upgrade) | Compare versions across all lock files, test IPC round-trip |
| Removed NuGet dependencies | Phase 2 (API Compatibility Audit) | Audit direct and transitive dependencies |
| FFXIVClientStructs mismatch | Phase 4 (Operation Validation) | Run all unsafe operations against Patch 7.5 game client |

---

## Sources

- Official Dalamud v14 → v15 changelog: https://dalamud.dev/versions/v15/ (MEDIUM confidence — page not directly fetchable, content from web search)
- Official Dalamud v13 → v14 changelog: https://dalamud.dev/versions/v14/ (MEDIUM confidence — historical migration pattern verified)
- Dalamud Updates FAQ: https://dalamud.dev/faq/updates/ (MEDIUM confidence)
- Dalamud IFramework API docs: https://dalamud.dev/api/Dalamud.Plugin.Services/Interfaces/IFramework/ (MEDIUM confidence — thread safety patterns)
- Dalamud plugin development community knowledge (MEDIUM confidence — gathered from multiple web search results)
- Codebase concerns documented in CONCERNS.md (HIGH confidence — first-hand audit)

---
*Pitfalls research for: Dalamud API 14 → 15 Migration (DalamudMCP)*
*Researched: 2026-04-30*
