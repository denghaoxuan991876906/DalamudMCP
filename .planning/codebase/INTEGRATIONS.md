# External Integrations

**Analysis Date:** 2026-04-30

## APIs & External Services

**FFXIV Dalamud Plugin API:**
- What: FFXIV modding framework by GoatCorp that loads plugins into the game process
- SDK: `Dalamud.NET.Sdk/14.0.2` (API Level 14, for FFXIV Patch 7.x)
- Resolved via: `DALAMUD_HOME` environment variable or default path at `%APPDATA%\XIVLauncher\addon\Hooks\dev`
- Runtime DLLs: `Dalamud.dll`, `Dalamud.Bindings.ImGui.dll`
- Used in: `src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj`
- Plugin manifest: `src/DalamudMCP.Plugin/DalamudMCP.json` (Author: "OpenAI", Punchline: "Local MCP/CLI bridge for FFXIV context.")
- Via Dalamud, the plugin also depends on:
  - `Lumina.dll` / `Lumina.Excel.dll` - Read FFXIV game data (Excel sheets, assets)
  - `FFXIVClientStructs.dll` - Access FFXIV game client memory structures
  - `InteropGenerator.Runtime.dll` - Generate and invoke function hooks

**MCP Protocol (Model Context Protocol):**
- What: Industry standard protocol for AI tool integration, connecting LLM clients to tool servers
- SDK: `ModelContextProtocol` NuGet package version 1.1.0
- Purpose: Expose FFXIV operations (observation and action tools) as MCP tools that LLM clients can discover and invoke
- Implementations:
  - Named pipe transport: `src/DalamudMCP.Protocol/NamedPipeProtocolServer.cs` and `NamedPipeProtocolClient.cs`
  - HTTP transport: `src/DalamudMCP.Cli/CliHttpServerRunner.cs` (ASP.NET Core)
  - STDIO transport: `src/DalamudMCP.Cli/Program.cs` (console stdin/stdout)
  - MCP server construction: `src/DalamudMCP.Cli/RemoteMcpToolService.cs` uses `ModelContextProtocol.Server`
  - Source generators emit MCP server registration code: `src/DalamudMCP.Framework.Generators/OperationDescriptorGenerator.cs`

**FFXIV Game Data (through Dalamud SDK):**
- What: Read-only access to game data via Lumina (Excel sheets, game assets)
- SDK: `Lumina.dll` and `Lumina.Excel.dll` (bundled with Dalamud runtime)
- Accessed via: Dalamud plugin service `IDataManager`
- Not a direct NuGet dependency; resolved at runtime from Dalamud

## Data Storage

**Databases:**
- None detected. No database ORM, no SQL client, no connection strings.

**File Storage:**
- Local filesystem only. Plugin configuration uses Dalamud's `IPluginLog` and `IDataManager`. No cloud storage.

**Caching:**
- None detected. No dedicated caching infrastructure (Redis, MemoryCache, etc.).

## Authentication & Identity

**Auth Provider:**
- None. The MCP bridge operates on localhost only, using named pipes (`\\.\pipe\DalamudMCP-*`) or loopback HTTP.
- No API keys, no OAuth, no identity provider.

## Monitoring & Observability

**Error Tracking:**
- None. No Sentry, Application Insights, or similar service.
- Dalamud provides `IPluginLog` for in-game logging.

**Logs:**
- `Serilog.dll` (via Dalamud runtime) for structured logging in the plugin context.
- `Microsoft.Extensions.Logging` for the CLI MCP server process (configured in `CliMcpServerRunner.cs`).
- No external log aggregation service.

## CI/CD & Deployment

**Hosting:**
- Plugin hosted within FFXIV game process via Dalamud launcher (XIVLauncher).
- CLI `dotnet` tool for standalone execution outside the game.
- No cloud hosting.

**CI Pipeline:**
- GitHub Actions (`.github/workflows/ci.yml`).
- Triggers on push and pull_request.
- Runs on `windows-latest` runner.
- Steps: checkout, setup-dotnet (SDK from `global.json`, cache via `packages.lock.json`), then `./build/quality.ps1` which runs restore + build + format verification + tests + architecture checks.
- Uses `DalamudMCP.CI.slnx` (CI solution excludes Plugin and Plugin test projects since Dalamud runtime is not available in CI).

**Release:**
- Manual process: `dotnet build src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj -c Release`
- Upload `src/DalamudMCP.Plugin/bin/Release/DalamudMCP/latest.zip`
- Plugin version: 0.2.0

## Environment Configuration

**Required environment variables:**
- `DALAMUD_HOME` (optional) - Path to Dalamud reference assemblies. Falls back to default paths per OS if not set.

**No .env files, no secret management, no external credential configuration.**

## IPC / Internal Communication

**Named Pipes (System.IO.Pipes):**
- Purpose: Inter-process communication between Dalamud plugin (in FFXIV process) and CLI process
- Implementation: `src/DalamudMCP.Protocol/NamedPipeProtocolServer.cs` and `NamedPipeProtocolClient.cs`
- Serialization: MemoryPack binary format over named pipe streams
- Protocol: Custom request/response envelopes defined in `src/DalamudMCP.Protocol/ProtocolContract.cs`

**STDIO:**
- Purpose: MCP protocol stdio transport for CLI mode
- Implementation: `src/DalamudMCP.Cli/Program.cs` reads from `Console.OpenStandardOutput()`/`Console.OpenStandardInput()`

## Webhooks & Callbacks

**Incoming:**
- None. No webhook endpoints.

**Outgoing:**
- None. No webhook callbacks.

---

*Integration audit: 2026-04-30*
