# Architecture Research: Dalamud API 15 Migration Impact

**Domain:** Dalamud plugin migration (API Level 14 to 15)
**Researched:** 2026-04-30
**Confidence:** MEDIUM (API 15 is pre-release, not yet finalized per dalamud.dev)

## Executive Summary

The DalamudMCP architecture has a **favorable layering** for this migration: six of seven source projects have zero Dalamud dependencies. Only `src/DalamudMCP.Plugin/` touches the Dalamud API surface. API 15's documented breaking changes (`IChatGui` XivChatType split, `IClientState` RowRefs, `IFramework` async-deprecation) **do not affect any code path in the current codebase** at the API level. The migration's architectural impact is therefore limited to:

1. **SDK version pinning** (`Dalamud.NET.Sdk` 14.0.2 -> 15.0.0)
2. **Manifest metadata** (`DalamudApiLevel` 14 -> 15, plus manifest accuracy requirement)
3. **Build infrastructure** (new reference assemblies in `DALAMUD_HOME`)
4. **Potential FFXIVClientStructs struct layout changes** (Patch 7.5, out of scope per PROJECT.md but carries real runtime risk)

No component boundaries, IPC protocols, operation models, or DI composition patterns need structural redesign for API 15 compliance.

---

## Architecture Overview: API 15 Impact Per Component

```
┌──────────────────────────────────────────────────────────────────────┐
│                    PURE LAYERS (No perubahan needed)                   │
│  Tidak ada Dalamud API reference atau ketergantungan pada API Level  │
│                                                                      │
│  ┌────────────────────┐  ┌──────────────────────┐                    │
│  │ DalamudMCP.Framework│  │ DalamudMCP.Protocol  │                    │
│  │ (abstractions, attr)│  │ (named pipe IPC)     │                    │
│  └────────────────────┘  └──────────────────────┘                    │
│                                                                      │
│  ┌────────────────────┐  ┌──────────────────────┐                    │
│  │ DalamudMCP.Framework│  │ DalamudMCP.Framework  │                    │
│  │ .Cli (CLI engine)   │  │ .Mcp (MCP binding)    │                    │
│  └────────────────────┘  └──────────────────────┘                    │
│                                                                      │
│  ┌──────────────────────────────────────────────────────────────┐    │
│  │ DalamudMCP.Framework.Generators (Roslyn source generator)     │    │
│  │ (targets netstandard2.0, tidak ada Dalamud dependency)         │    │
│  └──────────────────────────────────────────────────────────────┘    │
│                                                                      │
│  ┌──────────────────────────────────────────────────────────────┐    │
│  │ DalamudMCP.Cli (standalone binary, zero Dalamud dependency)    │    │
│  │ Hanya bergantung pada Framework + Protocol layers              │    │
│  └──────────────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ (no changes needed)
                                    ▼
┌──────────────────────────────────────────────────────────────────────┐
│                  AFFECTED LAYER (Perubahan terbatas)                   │
│                                                                      │
│  ┌──────────────────────────────────────────────────────────────┐    │
│  │              DalamudMCP.Plugin                                  │    │
│  │                                                                │    │
│  │  Changes required:                                              │    │
│  │   1. SDK: Dalamud.NET.Sdk/14.0.2 -> 15.0.0 (csproj)           │    │
│  │   2. Manifest: DalamudApiLevel 14 -> 15 (DalamudMCP.json)      │    │
│  │   3. packages.lock.json: regenerate                            │    │
│  │   4. Jika DALAMUD_HOME diupdate, pakai referensi API 15         │    │
│  │                                                                │    │
│  │  No code changes needed:                                        │    │
│  │   - Tidak ada penggunaan IChatGui (XivChatType tidak relevan)   │    │
│  │   - Tidak ada subscription ZoneInit (RowRefs tidak relevan)     │    │
│  │   - Tidak ada ActiveFestivals/AktifFestivalPhases               │    │
│  │   - RunOnFrameworkThread hanya pakai overload sync (Func<T>)    │    │
│  │   - Tidak ada penggunaan async Func<Task<T>> overloads          │    │
│  └──────────────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Component Boundary Analysis

### Components With Zero API 15 Impact

| Component | Path | Reason |
|-----------|------|--------|
| `DalamudMCP.Framework` | `src/DalamudMCP.Framework/` | Pure .NET abstractions. No Dalamud dependency. |
| `DalamudMCP.Protocol` | `src/DalamudMCP.Protocol/` | Named pipe IPC + MemoryPack. No Dalamud dependency. |
| `DalamudMCP.Framework.Cli` | `src/DalamudMCP.Framework.Cli/` | CLI argument parsing engine. No Dalamud dependency. |
| `DalamudMCP.Framework.Mcp` | `src/DalamudMCP.Framework.Mcp/` | MCP binding utility. Depends on `ModelContextProtocol` NuGet. No Dalamud. |
| `DalamudMCP.Framework.Generators` | `src/DalamudMCP.Framework.Generators/` | Roslyn source generator. Targets `netstandard2.0`. No Dalamud. |
| `DalamudMCP.Cli` | `src/DalamudMCP.Cli/` | Standalone executable. No Dalamud dependency by design (proxy pattern). |
| All test projects (8) | `tests/` | Test projects mock/fake Dalamud services. May need interface signature updates but no logical changes. |

### Component With Targeted API 15 Impact

| Component | Path | Impact | Details |
|-----------|------|--------|---------|
| `DalamudMCP.Plugin` | `src/DalamudMCP.Plugin/` | Minimal — SDK + manifest only | See detailed analysis below |

#### Detailed: `src/DalamudMCP.Plugin/` Impact Map

| File | API Usage | API 15 Change | Affected? | Action |
|------|-----------|--------------|-----------|--------|
| `DalamudMCP.Plugin.csproj` | Sdk="Dalamud.NET.Sdk/14.0.2" | Sdk version 14 -> 15 | **YES** | Change to `15.0.0` |
| `DalamudMCP.json` | `DalamudApiLevel: 14` | Must be 15 | **YES** | Change to 15 |
| `PluginEntryPoint.cs` | Constructor injection of 11+ Dalamud services | `IDalamudPluginInterface` now implements `IServiceProvider` | NO, but option | No change required; IServiceProvider is additive |
| `PluginEntryPoint.cs` | `pluginInterface.UiBuilder.Draw/OpenConfigUi` | No change | NO | None |
| `PluginEntryPoint.cs` | `pluginInterface.AssemblyLocation` | No change | NO | None |
| `PluginCompositionRoot.cs` | Manual DI registration of each service | No change to DI resolution | NO | None |
| `PluginServiceCollectionExtensions.cs` | Registers singleton services | No change | NO | None |
| `PluginMcpServerController.cs` | Process management, HTTP probing | No Dalamud API dependencies | NO | None |
| `PluginCliPathResolver.cs` | File path probing | No Dalamud API dependencies | NO | None |
| `OperationProtocolDispatcher.cs` | Command dispatch | No Dalamud API dependencies | NO | None |
| `PluginOperationExposurePolicy.cs` | Risk tier definitions | No Dalamud API dependencies | NO | None |
| `PluginGeneratedOperationRegistration.cs` | Source-generated registration | No Dalamud API dependencies | NO | None |
| All Operations (20+) | `IClientState` for `.ClientLanguage` and `.IsLoggedIn` | No change to these properties | NO | None |
| All Operations (20+) | `IFramework.RunOnFrameworkThread` | Only async overloads deprecated; code uses `Func<T>` | NO | None |
| All Operations (20+) | `IFramework.IsInFrameworkUpdateThread` | No change | NO | None |
| All Operations (20+) | `IObjectTable`, `ITargetManager`, `IGameGui`, etc. | No reported changes in API 15 | NO | None |
| `PluginConfigWindow.cs` | ImGui rendering | No Dalamud API dependencies | NO | None |

---

## Data Flow Implications

### Request Path: No Change

```
CLI User Input  ──►  NamedPipeProtocolClient  ──►  NamedPipeProtocolServer
                                                         │
                                                    OperationProtocolDispatcher
                                                         │
                                                    IOperationInvoker.TryInvoke
                                                         │
                                                    GeneratedOperationInvoker
                                                         │
                                                    Specific Operation
                                                         │
                                                    (reads FFXIV state via Dalamud APIs)
                                                         │
                                                    Response ──► Client
```

The request flow is completely unaffected by API 15. The IPC protocol (`ProtocolContract`), dispatcher, source-generated invoker, and operation model have zero Dalamud dependencies. The Dalamud-specific code only exists within each individual operation's `CreateDalamudExecutor` factory method and its `ReadCurrentCore` / `SendEventCore` / etc. static methods.

### Data Type Flow: No Change

The data flowing through the system is:
1. Operation reads raw game state via Dalamud APIs / unsafe structs
2. Transforms into `[MemoryPackable]` snapshot records
3. Serialized via MemoryPack over named pipe

API 15 does not change any Dalamud service return types that are consumed by these operations (at least for the properties actually accessed).

### Marshal-to-Game-Thread Pattern: No Change

The codebase uses a consistent pattern:
```csharp
if (framework.IsInFrameworkUpdateThread)
    return ReadCurrentCore(clientState, ...);
return await framework.RunOnFrameworkThread(() => ReadCurrentCore(clientState, ...))
    .ConfigureAwait(false);
```

This pattern uses the synchronous `RunOnFrameworkThread<T>(Func<T>)` overload, which is NOT deprecated in API 15. Only the async `Func<Task<T>>` and `Func<Task>` overloads are deprecated.

**If the deprecation warnings are emitted at compile time when targeting API 15** (possible but not confirmed), the code should remain warning-free since the sync overloads are used consistently.

---

## DI Composition Analysis

### Current State (API 14)

The plugin uses a **manual DI composition** pattern:
1. `PluginEntryPoint` constructor receives services via Dalamud's built-in IoC (constructor injection)
2. `PluginCompositionRoot.CreateFromDalamud()` receives all services explicitly (11+ parameters)
3. `PluginServiceCollectionExtensions.BuildDalamudServiceProvider()` registers each as a singleton in `Microsoft.Extensions.DependencyInjection`
4. A nested `ServiceProvider` is built for operations and infrastructure

### API 15 Implications

**No change required.** Looking at the API 15 docs:

- `IDalamudPluginInterface` now implements `IServiceProvider` (additive, not breaking)
- Constructor injection via Dalamud's IoC still works (existing patterns continue)
- Manual service registration in `PluginServiceCollectionExtensions` is unaffected

**Optional improvement** (not required for migration, but worth noting):
Since `IDalamudPluginInterface` is now an `IServiceProvider`, the 11+ parameter constructor of `PluginEntryPoint` could theoretically be reduced to just `IDalamudPluginInterface`, with other services resolved via `pluginInterface.GetRequiredService<T>()`. However, this is a refactoring opportunity, not a migration requirement.

**Decision:** Do not restructure DI during API 15 migration. The current pattern works and is more explicit about dependencies.

---

## IPC Layer Analysis

### Current State

```
NamedPipeProtocolServer (Plugin side)
    │
    ├── Accepts connections
    ├── Reads length-prefixed MemoryPack frames
    ├── Dispatches to OperationProtocolDispatcher
    └── Returns ProtocolResponseEnvelope

NamedPipeProtocolClient (CLI side)
    │
    ├── Connects to pipe
    ├── Sends ProtocolRequestEnvelope
    ├── Receives ProtocolResponseEnvelope
    └── Returns deserialized result
```

### API 15 Implications: NONE

The IPC layer (`src/DalamudMCP.Protocol/`) has zero Dalamud dependencies. It depends only on:
- `MemoryPack` (NuGet) — version stays at 1.21.4
- `Microsoft.Extensions.DependencyInjection.Abstractions` — version unchanged
- Standard .NET types (`System.IO.Pipes`, `System.IO.MemoryStream`, etc.)

No changes needed. The IPC protocol is versioned internally (`ProtocolContract` v2.0.0) with a major-version compatibility check, independent of Dalamud API Level.

---

## Operation Model Analysis

### Current State

```
[Operation("player.context")]
[CliCommand("player", "context")]
[McpTool("get_player_context")]
public sealed partial class PlayerContextOperation
    : IOperation<PlayerContextOperation.Request, PlayerContextSnapshot>
{
    // Constructor: receives Dalamud services via DI
    // CreateDalamudExecutor: factory that returns a delegate
    // ReadCurrentCore: static method that reads game state
}
```

### API 15 Implications: NONE

The operation model (attributes, `IOperation<TRequest,TResult>` interface, `GeneratedOperationRegistry`, `GeneratedOperationInvoker`) is defined in `DalamudMCP.Framework` which has zero Dalamud dependencies. The source generator in `DalamudMCP.Framework.Generators` targets `netstandard2.0` and is also unaffected.

Each operation's `CreateDalamudExecutor` factory captures Dalamud services (like `IClientState`, `IFramework`) as closures. These service interfaces maintain the same method signatures for the properties/methods used by this codebase.

---

## Source Generator Analysis

### Current State

The `OperationDescriptorGenerator` (~1700 lines) is a Roslyn incremental source generator that:
1. Scans for `[Operation]`-attributed types
2. Generates `GeneratedOperationRegistry` (list of all operation descriptors)
3. Generates `GeneratedOperationInvoker` (switch dispatch for operation execution)

### API 15 Implications: NONE

The generator consumes `Microsoft.CodeAnalysis.CSharp` (v4.14.0) and targets `netstandard2.0`. It has no awareness of Dalamud API levels. The generated code references types from `DalamudMCP.Framework`, not from Dalamud itself.

---

## Build Infrastructure Analysis

### Current State

```
DalamudMCP.Plugin.csproj:
  <Project Sdk="Dalamud.NET.Sdk/14.0.2">
  <PackageReference Include="DalamudPackager" Version="14.0.2" />

DalamudMCP.json:
  "DalamudApiLevel": 14

DalamudPackager.targets:
  DalamudApiLevel="$(DalamudApiLevel)"

build/*.ps1:
  Uses Get-DotNetCommand to find dotnet
  Uses Use-DalamudHome to find DALAMUD_HOME
```

### API 15 Changes Required

| Element | API 14 Value | API 15 Value | File to Change |
|---------|-------------|-------------|----------------|
| SDK version | `Dalamud.NET.Sdk/14.0.2` | `Dalamud.NET.Sdk/15.0.0` | `src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj` line 1 |
| Packager version | 14.0.2 (transitive via SDK) | 15.0.0 (transitive via SDK) | No explicit reference; auto-updated via SDK |
| API level | 14 | 15 | `src/DalamudMCP.Plugin/DalamudMCP.json` line 14 |
| Build environment | `DALAMUD_HOME` points to API 14 assemblies | `DALAMUD_HOME` must point to API 15 assemblies | Environment setup |
| NuGet lock files | Reflect API 14 | Must reflect API 15 | Regenerate via `dotnet restore --locked-mode` |

### Build Order Impact

The `BundleCliOutput` MSBuild target in `DalamudMCP.Plugin.csproj` builds `DalamudMCP.Cli` as a dependency during Plugin build. This target has no Dalamud dependencies, so it is unaffected.

The test projects reference the Plugin project (or its interfaces). Any interface changes from API 15 would cascade, but since there are no interface changes needed, tests should pass without modification.

---

## Migration Build Order (Recommended)

```
Phase 1: SDK + Manifest (no code changes)
────────────────────────────────────────────
  Step 1: Update csproj SDK version
          File: src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj
          Change: Dalamud.NET.Sdk/14.0.2 -> Dalamud.NET.Sdk/15.0.0

  Step 2: Update manifest API level
          File: src/DalamudMCP.Plugin/DalamudMCP.json
          Change: "DalamudApiLevel": 14 -> "DalamudApiLevel": 15

  Step 3: Update DALAMUD_HOME environment
          Must point to directory with API 15 reference assemblies
          (built from api15 branch of goatcorp/Dalamud)

Phase 2: Build Validation (compile + fix)
────────────────────────────────────────────
  Step 4: Restore packages
          Command: ./build/restore.ps1
          Expected: packages.lock.json updated

  Step 5: Build solution
          Command: ./build/build.ps1
          Outcome: 
            - Jika kompilasi sukses → API 15 tidak memiliki breaking
              changes yang mempengaruhi kode ini
            - Jika kompilasi gagal → periksa pesan error (kemungkinan:
              perubahan signature interface, metode obsolete)

  Step 6: Fix any compilation errors
          Most likely candidates (in order of probability):
            a. IFramework.RunOnFrameworkThread async overloads deprecated
               → but current codebase does NOT use these
            b. Property renames / type changes on injected services
               → belum ada laporan untuk API yang digunakan
            c. Perubahan struct layout di FFXIVClientStructs
               → out of scope, tetapi bisa muncul sebagai runtime failure

Phase 3: Test Validation
────────────────────────────
  Step 7: Run unit tests
          Command: ./build/test.ps1
          Expected: all tests pass (no Dalamud API changes that affect mocks)

  Step 8: Build release package
          Command: dotnet build ... -c Release (see AGENTS.md)
          Expected: valid API 15 plugin zip

Phase 4: Runtime Validation
────────────────────────────
  Step 9: Manual test in-game with API 15 Dalamud
          Verify: plugin loads, named pipe starts, CLI connects
          Verify: all 20+ operations return correct data
          Verify: IPC protocol works bidirectionally

  Step 10: Regression check for unsafe operations
           Operations using FFXIVClientStructs may have struct offsets
           changed by Patch 7.5. Test thoroughly.
```

---

## Dependency Graph: What Changes Propagate

```
Dalamud.NET.Sdk 14.0.2 -> 15.0.0
  │
  ├──► DalamudMCP.Plugin.csproj (direct Sdk attribute change)
  │
  ├──► DalamudPackager (transitive; new version includes API 15 packaging)
  │     │
  │     └──► DalamudMCP.json (Akurasi manifest sekarang diperlukan)
  │           └──► "DalamudApiLevel": 14 -> 15
  │
  ├──► Reference assemblies berubah (di DALAMUD_HOME)
  │     │
  │     ├──► IDalamudPluginInterface implements IServiceProvider (optional)
  │     ├──► IChatGui.XivChatType split (not used)
  │     ├──► IClientState.ZoneInitEventArgs (not used)
  │     ├──► IFramework async overloads obsolete (not used)
  │     └──► Other interface changes (none reported)
  │
  └──► NuGet package lock files (regenerate)

NO propagation to:
  ├──► DalamudMCP.Framework (zero deps)
  ├──► DalamudMCP.Protocol (zero deps)
  ├──► DalamudMCP.Framework.Cli (zero deps)
  ├──► DalamudMCP.Framework.Mcp (zero deps)
  ├──► DalamudMCP.Framework.Generators (zero deps)
  ├──► DalamudMCP.Cli (zero deps)
  └──► All test projects (zero logical changes)
```

---

## Risk Assessment: Runtime vs. Compile-Time

| Risk | Severity | Likelihood | Mitigation |
|------|----------|------------|------------|
| API 15 introduces interface changes beyond documented ones | Medium | Low | Build first, then test; compilation errors will surface interface mismatches |
| FFXIVClientStructs struct layout changes (Patch 7.5) | **High** | **High** (every patch) | Out of scope per PROJECT.md, but operations using unsafe struct access will silently corrupt data or crash. Must be tested in-game with Patch 7.5. |
| `IDalamudPluginInterface` as `IServiceProvider` changes DI resolution behavior | Low | Low | Current code uses manual DI; not affected |
| SDK 15.0.0 not yet available as NuGet package | Blocking | Medium (pre-release) | Butuh `DALAMUD_HOME` pointing to locally built API 15 assemblies |
| `packages.lock.json` version conflicts | Low | Medium | Regenerate with `dotnet restore --force-evaluate` if existing locks are incompatible |

---

## Architectural Anti-Patterns to Avoid During Migration

### 1. Mengganti seluruh DI hanya karena API 15 menambahkan IServiceProvider

**Jangan lakukan:** Refactor `PluginEntryPoint` untuk menghapus semua constructor parameters dan menggunakan `pluginInterface.GetRequiredService<T>()` di mana-mana.

**Alasan:** Ini menghilangkan explicit dependency declaration yang membuat kode mudah dipahami dan di-test. Constructor injection adalah pattern yang lebih baik daripada service location.

**Lakukan sebaliknya:** Pertahankan constructor injection untuk saat ini. Jika nanti ada alasan kuat untuk refactoring (misalnya jumlah parameter terus bertambah), lakukan secara bertahap.

### 2. Meng-upgrade NuGet dependencies secara bersamaan

**Jangan lakukan:** Upgrade MemoryPack, Microsoft.Extensions.DependencyInjection, atau ModelContextProtocol bersamaan dengan migrasi API 15.

**Alasan:** API 15 migration seharusnya minimal. Jika upgrade dependency lain gagal, sulit membedakan apakah error berasal dari API 15 atau upgrade tersebut.

**Lakukan sebaliknya:** Hanya upgrade `Dalamud.NET.Sdk`. Biarkan dependency lain di versi saat ini.

### 3. Mengubah IPC protocol karena API 15

**Jangan lakukan:** Modifikasi `ProtocolContract`, envelope types, atau protocol version.

**Alasan:** IPC layer tidak bergantung pada Dalamud sama sekali. Protocol version (`v2.0.0`) bersifat independent.

---

## Conclusion

The DalamudMCP project's architecture is well-isolated for this migration. The bridge/proxy pattern intentionally keeps Dalamud dependencies contained to the `Plugin` project. API 15's documented breaking changes affect APIs that this project does not consume. The migration is essentially:

1. **Three configuration changes** (csproj SDK, manifest API level, DALAMUD_HOME)
2. **Build and test validation** (compilation check, unit tests, in-game testing)
3. **Runtime verification with Patch 7.5** (unsafe struct operations)

The FFXIVClientStructs changes for Patch 7.5 represent the highest real risk, but they are explicitly scoped out of this migration milestone per PROJECT.md.

---

## Sources

- [What's New in Dalamud v15](https://dalamud.dev/versions/v15/) — Medium confidence (pre-release, not finalized)
- [IDalamudPluginInterface API 15 docs](https://dalamud.dev/api/api15/Dalamud.Plugin/Interfaces/IDalamudPluginInterface/) — Medium confidence
- [IClientState API 15 docs](https://dalamud.dev/api/api15/Dalamud.Plugin.Services/Interfaces/IClientState/) — Medium confidence
- [IFramework API 15 docs](https://dalamud.dev/api/api15/Dalamud.Plugin.Services/Interfaces/IFramework/) — Medium confidence
- Codebase analysis of `src/DalamudMCP.Plugin/` — HIGH confidence (verified against actual source)
- WebSearch: "Dalamud v15 breaking changes" multiple queries — LOW-MEDIUM confidence (search results contradict each other on API 15 existence)

---
*Architecture research for: DalamudMCP API Level 15 migration*
*Researched: 2026-04-30*
