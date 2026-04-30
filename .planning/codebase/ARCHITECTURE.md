<!-- refreshed: 2026-04-30 -->
# Architecture

**Analysis Date:** 2026-04-30

## System Overview

The system is a local MCP (Model Context Protocol) bridge for FFXIV (Final Fantasy XIV). It follows a **bridge/proxy architecture** where a Dalamud plugin acts as a backend that reads FFXIV game state, and one or more external processes (the CLI binary) act as protocol bridges between the plugin and MCP-compatible AI clients.

```
┌──────────────────────────────────────────────────────────────────────┐
│                        EXTERNAL CLIENTS                              │
│  MCP-compatible AI clients (Claude Desktop, etc.)                    │
│  or CLI shell                                                        │
└──────────┬───────────────────────────────────────────────────────────┘
           │ MCP Stdio / Streamable HTTP
           ▼
┌──────────────────────────────────────────────────────────────────────┐
│                   DalamudMCP.Cli (Standalone Binary)                  │
│  `src/DalamudMCP.Cli/`                                               │
│                                                                      │
│  Modes: DirectCli mode (CLI shell), ServeMcp (stdio), ServeHttp      │
│                                                                      │
│  Composed of: RemoteMcpToolService, RemoteCliInvoker,                │
│               CliApplication, CliHttpServerRunner, CliMcpServerRunner│
└──────────┬───────────────────────────────────────────────────────────┘
           │ Named Pipe IPC (length-prefixed MemoryPack-serialized envelopes)
           │ ProtocolContract v2.0.0
           ▼
┌──────────────────────────────────────────────────────────────────────┐
│                DalamudMCP.Plugin (Dalamud Plugin)                     │
│  `src/DalamudMCP.Plugin/`                                            │
│                                                                      │
│  PluginEntryPoint -> PluginCompositionRoot -> DI Container           │
│    ├── NamedPipeProtocolServer (listens for CLI connections)         │
│    ├── OperationProtocolDispatcher (routes requests to operations)   │
│    ├── PluginMcpServerController (manages CLI subprocess lifecycle)  │
│    └── 20+ Operations (read FFXIV game state via Dalamud APIs)       │
└──────────┬───────────────────────────────────────────────────────────┘
           │ Depends on Framework + Protocol + Generators
           ▼
┌──────────────────────────────────────────────────────────────────────┐
│                FRAMEWORK LAYER (Pure Abstractions)                    │
│  `src/DalamudMCP.Framework/`                                         │
│  IOperation<TRequest,TResult>, OperationAttribute, ParameterAttrs,   │
│  DescriptorModels, IOperationInvoker, IResultFormatter               │
└──────────────────────────────────────────────────────────────────────┘
```

## Component Responsibilities

| Component | Responsibility | File |
|-----------|----------------|------|
| `DalamudMCP.Framework` | Core abstractions and attribute-based operation model | `src/DalamudMCP.Framework/` |
| `DalamudMCP.Protocol` | Named pipe IPC layer with MemoryPack serialization | `src/DalamudMCP.Protocol/` |
| `DalamudMCP.Framework.Cli` | CLI application engine, argument parsing, binding helpers | `src/DalamudMCP.Framework.Cli/` |
| `DalamudMCP.Framework.Mcp` | MCP binding utility (service resolution) | `src/DalamudMCP.Framework.Mcp/` |
| `DalamudMCP.Framework.Generators` | Roslyn source generator for operation registration | `src/DalamudMCP.Framework.Generators/` |
| `DalamudMCP.Cli` | Standalone executable bridging CLI/MCP to Plugin via named pipes | `src/DalamudMCP.Cli/` |
| `DalamudMCP.Plugin` | Dalamud plugin hosting operations that read FFXIV state | `src/DalamudMCP.Plugin/` |

## Pattern Overview

**Overall:** Bridge/Proxy with Attribute-based Operation Model + Source Generation

**Key Characteristics:**
- **Attribute-driven operation model:** Operations are declared with `[Operation]`, `[CliCommand]`, `[McpTool]` attributes on classes implementing `IOperation<TRequest, TResult>`. A Roslyn source generator (`OperationDescriptorGenerator`) scans all assemblies at build time and generates `GeneratedOperationRegistry` and `GeneratedOperationInvoker`.
- **Plugin as IPC server:** The Dalamud plugin runs a `NamedPipeProtocolServer` and is the primary source of truth for all operations. The CLI binary is always a client.
- **CLI binary as proxy:** The CLI executable has no direct game state access. It connects to the plugin's named pipe and delegates all operations to it.
- **Multiple surface modes for CLI:** The same CLI binary can serve as a direct CLI tool, a stdio MCP server, or a Streamable HTTP MCP server.
- **Source-generated registration:** The `DalamudMCP.Framework.Generators` project produces `GeneratedOperationRegistry` and `GeneratedOperationInvoker` at compile time, eliminating manual operation registration.

## Layers

### Framework Layer (`src/DalamudMCP.Framework/`)
- **Purpose:** Define the abstract operation model and contracts that all other layers build upon.
- **Location:** `src/DalamudMCP.Framework/`
- **Contains:** `IOperation<TRequest,TResult>` interface, `OperationContext`, `OperationAttribute`, `ParameterAttributes` (`OptionAttribute`, `ArgumentAttribute`, `AliasAttribute`, `CliNameAttribute`, `McpNameAttribute`, etc.), `DescriptorModels` (`OperationDescriptor`, `ParameterDescriptor`), `IOperationInvoker`, `IResultFormatter<TResult>`
- **Depends on:** Nothing (pure .NET abstractions)
- **Used by:** Protocol, Framework.Cli, Framework.Mcp, Framework.Generators, Plugin, Cli

### Protocol Layer (`src/DalamudMCP.Protocol/`)
- **Purpose:** Define the wire protocol for IPC between the plugin and CLI processes over named pipes.
- **Location:** `src/DalamudMCP.Protocol/`
- **Contains:** `ProtocolContract` (serialization, envelope creation), `NamedPipeProtocolServer`, `NamedPipeProtocolClient`, `ProtocolClientDiscovery` (file-based discovery), `ProtocolOperationAttribute`, `LegacyBridgeRequestAttribute`, `ProtocolOperationCatalog`
- **Depends on:** `MemoryPack` NuGet package (v1.21.4)
- **Used by:** Cli, Plugin

### Framework CLI Layer (`src/DalamudMCP.Framework.Cli/`)
- **Purpose:** CLI command parsing, invocation, and text formatting engine.
- **Location:** `src/DalamudMCP.Framework.Cli/`
- **Contains:** `CliApplication` (argument parser, command resolver, result writer), `CliBinding` (value conversion, option lookup), `ICliInvoker`, `CliExitCodes`, `CliInvocationResult`
- **Depends on:** `DalamudMCP.Framework`
- **Used by:** Cli executable

### Framework MCP Layer (`src/DalamudMCP.Framework.Mcp/`)
- **Purpose:** Thin utility helpers for MCP integration (service resolution).
- **Location:** `src/DalamudMCP.Framework.Mcp/`
- **Contains:** `McpBinding` (service resolution helpers)
- **Depends on:** `DalamudMCP.Framework`, `ModelContextProtocol` NuGet (v1.1.0)
- **Used by:** Intended for generated MCP tool wrappers

### Framework Generators Layer (`src/DalamudMCP.Framework.Generators/`)
- **Purpose:** Roslyn incremental source generator that scans for `[Operation]`-attributed types and generates `GeneratedOperationRegistry` and `GeneratedOperationInvoker` automatically.
- **Location:** `src/DalamudMCP.Framework.Generators/`
- **Contains:** `OperationDescriptorGenerator` (~1700 lines of incremental generator logic)
- **Depends on:** `Microsoft.CodeAnalysis.CSharp` (v4.14.0), `netstandard2.0`
- **Used by:** Plugin (consumed as an Analyzer via `OutputItemType="Analyzer"`)

### CLI Executable (`src/DalamudMCP.Cli/`)
- **Purpose:** Standalone executable that serves as protocol bridge. Three modes: DirectCli, ServeMcp (stdio), ServeHttp (streamable HTTP MCP).
- **Location:** `src/DalamudMCP.Cli/`
- **Contains:** `Program.cs` (entry point), `CliProgram` (mode dispatch), `CliApplication` wrapper, `CliRuntimeOptions`, `CliHttpServerRunner`, `CliMcpServerRunner`, `RemoteMcpToolService`, `RemoteCliInvoker`, `ProtocolOperationDescriptorMapper`, `ProtocolOperationRequestFactory`, `PooledBufferStream`
- **Depends on:** `DalamudMCP.Protocol`, `DalamudMCP.Framework.Cli`, `ModelContextProtocol` (v1.1.0), `Microsoft.AspNetCore.App`
- **Used by:** End users (direct CLI), AI clients via MCP, plugin subprocess management

### Plugin (`src/DalamudMCP.Plugin/`)
- **Purpose:** Dalamud plugin that hosts all FFXIV game state operations. Runs a named pipe server for CLI communication.
- **Location:** `src/DalamudMCP.Plugin/`
- **Contains:**
  - `PluginEntryPoint` (IDalamudPlugin lifecycle), `PluginCompositionRoot` (DI setup)
  - `Hosting/`: `OperationProtocolDispatcher`, `PluginMcpServerController`, `PluginCliPathResolver`, `PluginServiceCollectionExtensions`, `PluginGeneratedOperationRegistration`, `PluginOperationExposurePolicy`, `ProtocolOperationCatalog`
  - `Operations/`: 20+ operation classes (PlayerContext, InventorySummary, MoveToEntity, etc.)
  - `Configuration/`: Plugin UI configuration
  - `Readers/`: Reader status interface
  - `Ui/`: ImGui config window
- **Depends on:** `DalamudMCP.Framework`, `DalamudMCP.Protocol`, `DalamudMCP.Framework.Generators` (as Analyzer), `Dalamud.NET.Sdk/14.0.2`, `MemoryPack`, `Microsoft.Extensions.DependencyInjection`
- **Used by:** Dalamud plugin loader (FFXIV)

## Data Flow

### Primary Request Path (Direct CLI)

1. User runs `dalamudmcp --pipe <name> player context` via CLI binary (`src/DalamudMCP.Cli/CliProgram.cs:43`)
2. `CliProgram.RunAsync` parses args via `CliRuntimeOptions`, discovers pipe name, selects `RunDirectCliAsync` mode
3. `RunDirectCliAsync` creates `NamedPipeProtocolClient`, fetches `DescribeOperationsResponse` from plugin (`src/DalamudMCP.Cli/CliProgram.cs:75-76`)
4. `CliApplication.ExecuteAsync` parses command tokens, matches to `OperationDescriptor` by `CliCommandPath` (`src/DalamudMCP.Framework.Cli/CliApplication.cs:155-192`)
5. `RemoteCliInvoker.TryInvoke` constructs protocol request payload via `ProtocolOperationRequestFactory` (`src/DalamudMCP.Cli/RemoteCliInvoker.cs:47-54`)
6. `NamedPipeProtocolClient.InvokeAsync` sends request through named pipe (`src/DalamudMCP.Protocol/NamedPipeProtocolClient.cs:78-127`)
7. Plugin's `NamedPipeProtocolServer` receives request, calls `OperationProtocolDispatcher.DispatchAsync` (`src/DalamudMCP.Plugin/Hosting/OperationProtocolDispatcher.cs:24-103`)
8. Dispatcher deserializes payload, calls `IOperationInvoker.TryInvoke`, which dispatches to the generated invoker for the target operation
9. Operation executes (e.g., reads FFXIV game state via Dalamud APIs), returns result
10. Response flows back through named pipe to CLI, result is formatted as text/JSON

### MCP Server Path (HTTP or Stdio)

1. CLI binary started with `serve http` or `serve mcp` command
2. Runner connects to plugin's named pipe, fetches catalog (`src/DalamudMCP.Cli/CliMcpServerRunner.cs:16-17`, `CliHttpServerRunner.cs:26-27`)
3. Creates `RemoteMcpToolService` with catalog and protocol client
4. Registers MCP server handlers (`ListToolsHandler`, `CallToolHandler`) that delegate to `RemoteMcpToolService`
5. AI client calls MCP tool -> `RemoteMcpToolService.CallToolAsync` maps tool name to operation, builds payload via `ProtocolOperationRequestFactory.CreateFromMcp`, sends through named pipe
6. Result from plugin is converted to `CallToolResult` with text + structured content

### Plugin Subprocess Management Flow

1. Plugin's `PluginMcpServerController.Start()` resolves CLI binary path via `PluginCliPathResolver` (`src/DalamudMCP.Plugin/Hosting/PluginMcpServerController.cs:117-148`)
2. Launches CLI binary as child process with `--pipe <name> serve http --port 38473`
3. Probes HTTP endpoint for availability and correct MCP tool catalog
4. Manages process lifecycle (start, stop, health check, stale endpoint termination via `netstat`)

### State Management
- **Plugin:** Mutable game state read from Dalamud on each operation invocation (no caching)
- **CLI server:** Tool catalog is periodically refreshed (every 2 seconds) via `RemoteMcpToolService`
- **Plugin configuration:** Stored as `DalamudMCP.json` in Dalamud config directory, managed by `PluginUiConfigurationStore`
- **Protocol discovery:** Plugin writes `active-instance.json` with pipe name and PID to allow external processes to discover it

## Key Abstractions

### `IOperation<TRequest, TResult>`
- **Purpose:** The core operation contract. All FFXIV game state reads/writes implement this interface.
- **Examples:** `PlayerContextOperation`, `InventorySummaryOperation`, `MoveToEntityOperation` in `src/DalamudMCP.Plugin/Operations/`
- **Pattern:** Attribute-decorated partial class with nested request record, optional formatter, and factory method for executor

```csharp
// Pattern from src/DalamudMCP.Plugin/Operations/PlayerContextOperation.cs
[Operation("player.context", Description = "...")]
[CliCommand("player", "context")]
[McpTool("get_player_context")]
public sealed partial class PlayerContextOperation
    : IOperation<PlayerContextOperation.Request, PlayerContextSnapshot>
{ ... }
```

### `OperationDescriptor` / `ProtocolOperationDescriptor`
- **Purpose:** Metadata classes describing an operation's signature, parameters, CLI path, MCP tool name, visibility, and binding info.
- **Source:** Generated at compile time by `OperationDescriptorGenerator` for the Framework model. Transformed to `ProtocolOperationDescriptor` for wire transfer.
- **Mapping:** `src/DalamudMCP.Cli/ProtocolOperationDescriptorMapper.cs` converts Protocol descriptors to Cli descriptors. `src/DalamudMCP.Plugin/Hosting/ProtocolOperationCatalog.cs` converts Framework descriptors to Protocol descriptors.

### `ProtocolContract` (envelope protocol)
- **Purpose:** Defines the wire format for IPC. Uses length-prefixed frames with MemoryPack-serialized envelopes (`ProtocolRequestEnvelope` / `ProtocolResponseEnvelope`).
- **Location:** `src/DalamudMCP.Protocol/ProtocolContract.cs`
- **Version:** `2.0.0`, major-version check for compatibility

### `OperationProtocolDispatcher`
- **Purpose:** Central request router in the plugin. Receives `ProtocolRequestEnvelope`, dispatches to the correct operation via `IOperationInvoker`, and returns `ProtocolResponseEnvelope`.
- **Location:** `src/DalamudMCP.Plugin/Hosting/OperationProtocolDispatcher.cs`
- **Key behavior:** Handles `__system.describe-operations` system call, applies operation exposure policy (action ops / unsafe ops can be disabled), supports multiple request type aliases per operation

### `PluginMcpServerController`
- **Purpose:** Manages the lifecycle of the CLI subprocess that runs the HTTP MCP server. Supports start, stop, health probing, and stale endpoint cleanup.
- **Location:** `src/DalamudMCP.Plugin/Hosting/PluginMcpServerController.cs`
- **Pattern:** Probe-based availability detection (HTTP POST to `/mcp` with check for correct MCP protocol version and expected tool names)

## Entry Points

| Entry Point | Location | Triggers | Responsibilities |
|-------------|----------|----------|------------------|
| `Program.Main` | `src/DalamudMCP.Cli/Program.cs:5` | User launches executable | Parses args, delegates to `CliProgram.RunAsync` |
| `CliProgram.RunAsync` | `src/DalamudMCP.Cli/CliProgram.cs:9` | Called from `Program.Main` | Mode dispatch: DirectCli/ServeMcp/ServeHttp |
| `CliMcpServerRunner.RunAsync` | `src/DalamudMCP.Cli/CliMcpServerRunner.cs:31` | `serve mcp` mode | Creates `McpServer` with stdio transport |
| `CliHttpServerRunner.RunAsync` | `src/DalamudMCP.Cli/CliHttpServerRunner.cs:21` | `serve http` mode | Creates ASP.NET Minimal API with Streamable HTTP transport |
| `PluginEntryPoint` | `src/DalamudMCP.Plugin/PluginEntryPoint.cs:13` | Dalamud plugin loader | Initializes composition root, starts protocol server, registers UI |
| `PluginCompositionRoot.CreateFromDalamud` | `src/DalamudMCP.Plugin/PluginCompositionRoot.cs:40` | PluginEntryPoint constructor | Builds DI container, creates protocol server |

## Architectural Constraints

- **Threading:** The Plugin runs on FFXIV's main thread (framework thread). Long-running operations use `IFramework.RunOnFrameworkThread` to marshal back. The Named Pipe Server uses async I/O with `Task.Run` for the accept loop. The CLI process uses standard async patterns throughout.
- **IPC exclusivity:** The named pipe protocol is the only inter-process communication channel between the Plugin and CLI processes. No shared memory, no files (except discovery), no sockets (unless HTTP MCP mode).
- **Single-instance CLI binary:** The same `DalamudMCP.Cli` binary supports three modes via command-line arguments. The mode is selected at parse time and fixed for the process lifetime.
- **No direct game access for CLI:** The CLI process has zero dependencies on Dalamud/FFXIV. It is purely a protocol proxy.
- **Exposure policy:** `PluginOperationExposurePolicy` (`src/DalamudMCP.Plugin/Hosting/PluginOperationExposurePolicy.cs`) defines two risk tiers for operations: "action" operations (modify game state) and "unsafe" operations (invoke arbitrary plugin IPC). Both can be independently toggled via the UI configuration.

## Anti-Patterns

### Source Generator as Analyzer Dependency

**What happens:** The `DalamudMCP.Framework.Generators` project is consumed as an Analyzer via `OutputItemType="Analyzer"` with `ReferenceOutputAssembly="false"` in the Plugin's csproj. This means the generator's assembly is NOT referenced as a normal dependency but only used at compile time.

**Why it's problematic:** This is standard Roslyn generator consumption, but it creates an implicit dependency: the generated code references types from `DalamudMCP.Framework`, which must be separately referenced.

**Do this instead:** This is the correct pattern for Roslyn generators. No change needed.

### CLI process resolution via probing

**What happens:** `PluginCliPathResolver` (`src/DalamudMCP.Plugin/Hosting/PluginCliPathResolver.cs`) probes multiple paths to find the CLI binary: first the bundled `server/` directory next to the plugin assembly, then back up through repository structure. This is fragile.

**Why it's wrong:** Path probing creates implicit assumptions about build output layout. If the build output structure changes, the resolver silently fails.

**Do this instead:** The bundling is intentional (see `BundleCliOutput` MSBuild target in `DalamudMCP.Plugin.csproj`). The probing should be made more explicit or use a known environment variable.

## Error Handling

**Strategy:** Operations throw `ArgumentException` for invalid input and `InvalidOperationException` for unavailable state. These are caught at the dispatcher/invoker level and converted to appropriate error responses (protocol errors for IPC, usage errors for CLI, tool errors for MCP).

**Patterns:**
- `NamedPipeProtocolServer` catches `InvalidOperationException` and `ArgumentException`, converts to protocol error responses (`src/DalamudMCP.Protocol/NamedPipeProtocolServer.cs:122-131`)
- `CliApplication.ExecuteAsync` catches the same exceptions, maps to exit codes (`src/DalamudMCP.Framework.Cli/CliApplication.cs:108-117`)
- `RemoteMcpToolService.CallToolAsync` catches them, maps to `CallToolResult` with `IsError = true` (`src/DalamudMCP.Cli/RemoteMcpToolService.cs:96-103`)

## Cross-Cutting Concerns

**Logging:** Minimal. The CLI clears default logging providers (`Logging.ClearProviders()`) in both server modes. No structured logging framework is used. Errors propagate through return values and exception handling.

**Validation:** Argument validation uses `ArgumentNullException.ThrowIfNull` (standard .NET 6+ pattern) pervasively. Input validation for CLI/MCP arguments is done at the factory level in `ProtocolOperationRequestFactory`.

**Authentication:** None. The named pipe is accessible to any process on the same machine. The intent is local single-user usage only. No access control is implemented on the pipe.

---

*Architecture analysis: 2026-04-30*
