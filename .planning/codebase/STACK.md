# Technology Stack

**Analysis Date:** 2026-04-30

## Languages

**Primary:**
- C# (LangVersion=latest, aligned with .NET 10.0) - All application source code across 7 projects
- PowerShell - All build/CI scripts in `build/`

**Secondary:**
- XML - Solution file (`.slnx`), project files (`.csproj`), MSBuild props/targets
- JSON - Plugin manifest (`DalamudMCP.json`), config files

## Runtime

**Environment:**
- .NET SDK 10.0.201 (from `global.json` with `rollForward: latestFeature`)
- .NET 10.0 runtime target (`net10.0`)

**Package Manager:**
- NuGet via `dotnet restore` / `dotnet build`
- All projects use `RestorePackagesWithLockFile=true` (lock files at `**/packages.lock.json`)
- No local dotnet tool manifest (`.config/dotnet-tools.json` not found)

## Frameworks

**Core:**
- .NET 10.0 (`net10.0`) - Target framework for all projects except source generators
- .NET Standard 2.0 (`netstandard2.0`) - Target for `DalamudMCP.Framework.Generators` (Roslyn source generator)
- `net10.0-windows7.0` - Target for `DalamudMCP.Plugin` and its tests (Windows-specific for Dalamud FFXIV plugin)
- Microsoft.Extensions.DependencyInjection 10.0.0 - Dependency injection container
- Microsoft.Extensions.Hosting - Hosting abstractions for CLI server runner

**MCP Framework:**
- ModelContextProtocol 1.1.0 - Official Microsoft MCP (Model Context Protocol) SDK for server implementation in `DalamudMCP.Framework.Mcp` and `DalamudMCP.Cli`

**Serialization:**
- MemoryPack 1.21.4 - High-performance binary serialization for inter-process protocol payloads (used in `DalamudMCP.Protocol`)
- System.Text.Json - JSON serialization for CLI operations and MCP protocol data

**Web/HTTP:**
- Microsoft.AspNetCore.App (framework reference) - ASP.NET Core hosting for the HTTP MCP server runner in `DalamudMCP.Cli`

**Roslyn / Source Generators:**
- Microsoft.CodeAnalysis.CSharp 4.14.0 - Used by `DalamudMCP.Framework.Generators` for incremental source generation
- Microsoft.CodeAnalysis.Analyzers 3.11.0 - Analyzer infrastructure for the source generator

**Plugin Framework:**
- Dalamud.NET.Sdk 14.0.2 - MSBuild SDK for FFXIV Dalamud plugin development (API Level 14)
- Dalamud runtime assemblies (not NuGet, resolved via `DALAMUD_HOME`):
  - `Dalamud.dll` - Core Dalamud plugin API
  - `Dalamud.Bindings.ImGui.dll` - ImGui bindings
  - `Lumina.dll` / `Lumina.Excel.dll` - FFXIV game data reader
  - `FFXIVClientStructs.dll` - FFXIV client structure definitions
  - `InteropGenerator.Runtime.dll` - Interop code generation runtime
  - `Newtonsoft.Json.dll` - JSON framework (legacy)
  - `Serilog.dll` - Structured logging
  - `Microsoft.Extensions.ObjectPool.dll` - Object pooling

**Testing:**
- xUnit v3 (xunit.v3.mtp-v2 3.2.2) - Test framework via Microsoft Testing Platform runner
- Microsoft Testing Platform (MTP) - Test runner configured via `UseMicrosoftTestingPlatformRunner`
- coverlet.MTP 8.0.0 - Code coverage collection integrated with MTP
- All test projects enforce a minimum 90% coverage threshold (via `CoverageThreshold` property)
- xunit.analyzers 1.27.0 - Roslyn analyzers for xUnit best practices

**Build/Dev:**
- `dotnet format` - Code formatting (no third-party formatter; no `.config/dotnet-tools.json` means no local tools)
- .editorconfig - Style rules enforced at build time with `EnforceCodeStyleInBuild=true` and `AnalysisLevel=latest-recommended`
- Deterministic builds enabled (`<Deterministic>true</Deterministic>`)
- Warnings as errors enabled with nullable warnings as errors

## Key Dependencies

**Critical:**
- ModelContextProtocol 1.1.0 - Core MCP server SDK; enables AI tool integration with LLM clients
- MemoryPack 1.21.4 - Binary serialization for local named-pipe IPC between plugin and CLI process
- Dalamud.NET.Sdk 14.0.2 - Enables FFXIV plugin loading within Dalamud launcher

**Infrastructure:**
- Microsoft.Extensions.DependencyInjection 10.0.0 - DI container for service composition across projects
- Microsoft.Extensions.Hosting - Background service lifecycle for MCP server
- Microsoft.AspNetCore.App - HTTP endpoint hosting for alternative MCP transport

## Configuration

**Environment:**
- `DALAMUD_HOME` environment variable - Points to Dalamud reference assembly directory (e.g., `%APPDATA%\XIVLauncher\addon\Hooks\dev`)
- Default paths on Windows: `%APPDATA%\XIVLauncher\addon\Hooks\dev`
- Default paths on Linux: `~/.xlcore/dalamud/Hooks/dev`
- Default paths on macOS: `~/Library/Application Support/XIV on Mac/dalamud/Hooks/dev`

**Build:**
- `Directory.Build.props` - Shared MSBuild properties (target framework, nullable, warnings, code analysis)
- `Directory.Build.targets` - Shared MSBuild targets (coverage threshold validation)
- `global.json` - .NET SDK version pinning (10.0.201)
- `xunit.runner.json` - xUnit v3 runner config (schema reference, defaults implied)

**Code Style:**
- `.editorconfig` - C# formatting and analyzer rules (file-scoped namespaces, accessibility modifiers required, collection expressions preferred)
- CA rules enforced as errors: CA2211, CA2227, CA2252
- Several CA rules suppressed at project level: CA1707, CA1716, CA1724, CA1812, CA2007

## Platform Requirements

**Development:**
- Windows (primary) - FFXIV game client required for full testing; Dalamud dev hooks
- .NET SDK 10.0.201 (or compatible 10.0.x)
- PowerShell (for build scripts)
- Optional: `DALAMUD_HOME` pointing to Dalamud dev hooks directory

**Production:**
- Deployment target: FFXIV Dalamud plugin directory (manual packaging via `DalamudMCP.Plugin`)
- Release output: `src/DalamudMCP.Plugin/bin/Release/DalamudMCP/latest.zip`
- Plugin version: 0.2.0 (`DalamudMCP.json` and assembly version)

## Solution Structure

**Main solution:** `DalamudMCP.slnx` (7 source projects, 8 test projects)
**CI solution:** `DalamudMCP.CI.slnx` (6 source projects excluding Plugin, 6 test projects excluding Plugin.Operations.Tests and Plugin.Tests)

---

*Stack analysis: 2026-04-30*
