# Codebase Structure

**Analysis Date:** 2026-04-30

## Directory Layout

```
DalamudMCP/
├── .github/workflows/     # CI pipeline definitions (GitHub Actions)
├── .planning/             # Planning documents and codebase maps
│   └── codebase/          # Architecture/structure/concerns docs
├── build/                 # PowerShell build/restore/test/format scripts
│   ├── build.ps1
│   ├── restore.ps1
│   ├── test.ps1
│   ├── format.ps1
│   ├── quality.ps1
│   ├── architecture.ps1
│   ├── Get-DotNetCommand.ps1
│   └── Use-DalamudHome.ps1
├── src/                   # Source projects
│   ├── DalamudMCP.Framework/              # Core abstractions (no deps)
│   ├── DalamudMCP.Protocol/               # Named pipe IPC protocol
│   ├── DalamudMCP.Framework.Cli/          # CLI application engine
│   ├── DalamudMCP.Framework.Mcp/          # MCP binding helpers
│   ├── DalamudMCP.Framework.Generators/   # Roslyn source generator
│   ├── DalamudMCP.Cli/                    # Standalone CLI executable
│   └── DalamudMCP.Plugin/                 # Dalamud plugin
│       ├── Configuration/                 # UI settings store
│       ├── Hosting/                       # DI, dispatcher, server mgmt
│       ├── Operations/                    # 20+ FFXIV game operations
│       ├── Readers/                       # Reader status interface
│       ├── Ui/                            # ImGui configuration window
│       └── Properties/                    # Assembly metadata
├── tests/                  # Test projects (one per source project)
│   ├── DalamudMCP.Framework.Tests/
│   ├── DalamudMCP.Protocol.Tests/
│   ├── DalamudMCP.Framework.Cli.Tests/
│   ├── DalamudMCP.Framework.Mcp.Tests/
│   ├── DalamudMCP.Framework.Generators.Tests/
│   ├── DalamudMCP.Cli.Tests/
│   ├── DalamudMCP.Plugin.Tests/
│   └── DalamudMCP.Plugin.Operations.Tests/
├── .tools/                 # Tool manifests/managers
├── CLAUDE.md               # Points to AGENTS.md
├── AGENTS.md               # Project-level agent instructions
├── DalamudMCP.slnx         # Solution file (new .slnx format)
├── DalamudMCP.CI.slnx      # CI-specific solution file
├── LICENSE                 # MIT License
├── THIRD_PARTY_NOTICES.md  # Third-party license attributions
└── README.md               # Project readme
```

## Directory Purposes

### `src/DalamudMCP.Framework/`
- **Purpose:** Pure abstraction layer with zero dependencies. Defines the attribute-based operation model.
- **Contains:** Interfaces (`IOperation<TRequest,TResult>`, `IResultFormatter<T>`, `IOperationInvoker`), attributes (`OperationAttribute`, `OptionAttribute`, `ArgumentAttribute`, `AliasAttribute`, `CliNameAttribute`, `McpNameAttribute`, `CliCommandAttribute`, `McpToolAttribute`, `CliOnlyAttribute`, `McpOnlyAttribute`, `FromServicesAttribute`, `ResultFormatterAttribute`), models (`OperationDescriptor`, `ParameterDescriptor`, `OperationContext`, `OperationInvocationResult`), enums (`InvocationSurface`, `OperationVisibility`, `ParameterSource`), helpers (`OperationBinding`)
- **Key files:**
  - `IOperation.cs` — Core operation interface
  - `OperationAttribute.cs` — Operation identifier attribute
  - `ParameterAttributes.cs` — All parameter/command binding attributes (99 lines)
  - `DescriptorModels.cs` — Operation and parameter descriptor records
  - `OperationContext.cs` — Context object passed to every operation
  - `OperationInvoker.cs` — `IOperationInvoker` interface + `OperationInvocationResult` record

### `src/DalamudMCP.Protocol/`
- **Purpose:** Wire protocol for IPC over named pipes. Uses MemoryPack for serialization.
- **Contains:** Protocol contract, server/client implementations, discovery file, operation catalog models, attributes, `AssemblyMarker`
- **Key files:**
  - `ProtocolContract.cs` — (263 lines) Envelope serialization/deserialization, response creation, version compatibility check
  - `NamedPipeProtocolServer.cs` — (224 lines) Async named pipe server with accept loop, length-prefixed framing
  - `NamedPipeProtocolClient.cs` — (218 lines) Async named pipe client with caching, request type resolution, discovery
  - `ProtocolClientDiscovery.cs` — (89 lines) File-based discovery mechanism (`active-instance.json`)
  - `ProtocolOperationCatalog.cs` — (69 lines) Protocol-level operation descriptor models (MemoryPackable records)
  - `IProtocolOperationClient.cs` — Client interface
  - `ProtocolOperationAttribute.cs` — Links request types to operation IDs
  - `LegacyBridgeRequestAttribute.cs` — Legacy request type mapping

### `src/DalamudMCP.Framework.Cli/`
- **Purpose:** CLI argument parsing, command resolution, and invocation engine.
- **Contains:** `CliApplication` (full CLI engine with help, argument parsing, command matching), `CliBinding` (value conversion helpers), `ICliInvoker`, exit codes, result types
- **Key files:**
  - `CliApplication.cs` — (401 lines) Main CLI engine: argument parser, command tree matching, help generation, result output
  - `CliBinding.cs` — (189 lines) Type conversion (string->int/bool/Guid/etc.), option lookup, service resolution
  - `ICliInvoker.cs` — Interface for delegating operation invocation
  - `CliExitCodes.cs` — Exit code constants (Success=0, UnhandledFailure=1, UsageError=2, Unavailable=3)
  - `CliInvocationResult.cs` — Result record with Result, ResultType, Text, RawJsonPayload

### `src/DalamudMCP.Framework.Mcp/`
- **Purpose:** Thin utility binding for MCP integration.
- **Contains:** `McpBinding` with `GetRequiredService` overloads
- **Key files:**
  - `McpBinding.cs` — (33 lines) Service resolution from IServiceProvider

### `src/DalamudMCP.Framework.Generators/`
- **Purpose:** Roslyn incremental source generator that automatically discovers operations and generates `GeneratedOperationRegistry` and `GeneratedOperationInvoker`.
- **Contains:** Single massive generator file
- **Key files:**
  - `OperationDescriptorGenerator.cs` — (~1700 lines) Incremental generator with 5 diagnostic descriptors (DMCF001-DMCF005), attribute scanning, code emission for registry and invoker
- **Important:** Consumed as Analyzer by Plugin (not as a normal project reference)

### `src/DalamudMCP.Cli/`
- **Purpose:** Standalone executable with three operating modes.
- **Contains:** Entry point, mode dispatch, server runners, protocol command/mcp builders
- **Key files:**
  - `Program.cs` — (10 lines) Minimal entry point, delegates to `CliProgram.RunAsync`
  - `CliProgram.cs` — (85 lines) Mode dispatch based on parsed args (DirectCli / ServeMcp / ServeHttp)
  - `CliRuntimeOptions.cs` — (242 lines) Argument parsing for `--pipe`, `serve mcp`, `serve http`, pipe name resolution (env var, discovery file)
  - `CliHttpServerRunner.cs` — (147 lines) ASP.NET Minimal API with StreamableHttpServerTransport MCP transport
  - `CliMcpServerRunner.cs` — (45 lines) IHost-based stdio MCP server
  - `RemoteMcpToolService.cs` — (288 lines) MCP tool handler that delegates to protocol client, with periodic catalog refresh
  - `RemoteCliInvoker.cs` — (79 lines) ICliInvoker implementation that delegates to protocol client
  - `ProtocolOperationDescriptorMapper.cs` — (91 lines) Maps protocol operation descriptors to CLI operation descriptors
  - `ProtocolOperationRequestFactory.cs` — (374 lines) Builds JSON payloads from CLI args or MCP JSON arguments
  - `CliServiceCollectionExtensions.cs` — (19 lines) DI registration for protocol client
  - `PooledBufferStream.cs` — (173 lines) Array-pool-backed write-only stream for MCP response buffering

### `src/DalamudMCP.Plugin/`
- **Purpose:** The primary Dalamud plugin that reads FFXIV game state and serves operations via named pipe.
- **Contains:** Entry point, DI composition root, hosting infrastructure, 20+ operations, configuration, UI
- **Key files:**
  - `PluginEntryPoint.cs` — (123 lines) IDalamudPlugin lifecycle: constructs composition root, starts protocol server, writes discovery record, creates server controller, hooks UI
  - `PluginCompositionRoot.cs` — (100 lines) DI container setup, wraps ServiceProvider + ProtocolServer
  - `PluginRuntimeOptions.cs` — (34 lines) Pipe name generation (`DalamudMCP.{PID}.{shortGUID}`), working/capture directories

  **Hosting/:**
  - `OperationProtocolDispatcher.cs` — (166 lines) Central request dispatcher: maps request type to operation, applies exposure policy, serializes response
  - `ProtocolOperationCatalog.cs` — (110 lines) Transforms Framework `OperationDescriptor` to Protocol `ProtocolOperationDescriptor`
  - `PluginMcpServerController.cs` — (637 lines) CLI subprocess lifecycle management: path resolution, process start, health probing, stale endpoint detection/kill
  - `PluginCliPathResolver.cs` — (141 lines) Binary path probing: bundled `server/` dir, repo build output
  - `PluginServiceCollectionExtensions.cs` — (69 lines) Builds DI container with all Dalamud services, generated operations, protocol dispatcher, named pipe server
  - `PluginGeneratedOperationRegistration.cs` — (41 lines) Registers each generated operation type + formatter in DI
  - `PluginOperationExposurePolicy.cs` — (71 lines) Defines action and unsafe operation tiers, filtering logic

  **Operations/:** (20+ files)
  - `PlayerContextOperation.cs` — Example operation showing the full pattern (attribute, partial class, request record, formatter, Dalamud executor)
  - `InventorySummaryOperation.cs`, `MoveToEntityOperation.cs`, `TeleportToAetheryteOperation.cs`, `AddonInputOperation.cs`, `AddonEventOperation.cs`, etc.

  **Configuration/:**
  - `PluginUiConfiguration.cs` — Configuration model
  - `PluginUiConfigurationStore.cs` — File-based JSON config persistence
  - `IPluginUiConfigurationAccessor.cs` — Read-only configuration access interface

  **Ui/:**
  - `PluginConfigWindow.cs` — ImGui draw logic for settings window
  - `PluginConfigWindowModel.cs` — UI state model

### `tests/`
- **Purpose:** One test project per source project, following the standard .NET test pattern.
- **Key tests:**
  - `DalamudMCP.Cli.Tests/` — Tests for CLI argument parsing, MCP server runners, protocol request factory, pooled buffer, remote MCP tool service
  - `DalamudMCP.Framework.Cli.Tests/` — Tests for `CliApplication` command resolution and usage generation
  - `DalamudMCP.Framework.Generators.Tests/` — Snapshot tests for source generator output (`GeneratedOperationRegistryTests`, `OperationDescriptorGeneratorDiagnosticsTests`)
  - `DalamudMCP.Framework.Mcp.Tests/` — Tests for generated MCP tools
  - `DalamudMCP.Framework.Tests/` — Tests for core framework models and attributes
  - `DalamudMCP.Plugin.Tests/` — Tests for protocol dispatcher, path resolver, server controller, config window model
  - `DalamudMCP.Plugin.Operations.Tests/` — Tests for individual operation classes (20+ test files)
  - `DalamudMCP.Protocol.Tests/` — Tests for named pipe client/server and discovery

### `build/`
- **Purpose:** PowerShell scripts for common development workflows.
- **Key files:**
  - `build.ps1` — Build solution
  - `restore.ps1` — Restore NuGet packages
  - `test.ps1` — Run tests
  - `format.ps1` — Run `dotnet format`
  - `quality.ps1` — Run quality checks (build + format --verify-no-changes + test)
  - `architecture.ps1` — Architecture validation (build + format + test)
  - `Get-DotNetCommand.ps1` — Helper to locate `dotnet` binary (local `.dotnet/` or PATH)
  - `Use-DalamudHome.ps1` — Helper for locating Dalamud SDK

## Naming Conventions

**Files:**
- PascalCase for all `.cs` files: `PlayerContextOperation.cs`, `CliApplication.cs`, `NamedPipeProtocolServer.cs`
- File name matches primary type name consistently
- `packages.lock.json` for NuGet package lock files (present in every project)

**Namespaces:**
- `DalamudMCP.{ProjectName}` for each project: `DalamudMCP.Framework`, `DalamudMCP.Protocol`, `DalamudMCP.Cli`, `DalamudMCP.Plugin`
- Sub-namespaces within Plugin: `DalamudMCP.Plugin.Hosting`, `DalamudMCP.Plugin.Operations`, `DalamudMCP.Plugin.Configuration`, `DalamudMCP.Plugin.Ui`, `DalamudMCP.Plugin.Readers`

**Types:**
- PascalCase for all types
- Interfaces are prefixed with `I`: `IOperation`, `IResultFormatter`, `IProtocolOperationClient`, `ICliInvoker`, `IPluginReaderStatus`
- Records use PascalCase: `OperationDescriptor`, `ProtocolRequestEnvelope`, `PlayerContextSnapshot`
- Attributes use `Attribute` suffix: `OperationAttribute`, `OptionAttribute`, `ProtocolOperationAttribute`
- Static utility classes are named for their domain: `CliBinding`, `ProtocolContract`, `ProtocolOperationRequestFactory`

**Methods:**
- PascalCase with Async suffix for async methods: `ExecuteAsync`, `InvokeAsync`, `DispatchAsync`, `DescribeOperationsAsync`
- Factory methods prefixed with `Create`, `Build`, or `For`: `ForCli()`, `ForMcp()`, `CreateFromDalamud()`, `BuildHttpServer()`

**Parameters:**
- camelCase parameter names

## Where to Add New Code

### New Feature (new operation for FFXIV state)
- Implementation: `src/DalamudMCP.Plugin/Operations/{Name}Operation.cs`
- Request/response models: inline in the same file as nested `[MemoryPackable]` records
- Tests: `tests/DalamudMCP.Plugin.Operations.Tests/{Name}OperationTests.cs`
- Registration: Automatic (source generator scans for `[Operation]` attribute). No manual registration needed.
- Exposure policy: If the operation modifies game state, add its `OperationId` to `PluginOperationExposurePolicy.ActionOperationIds`

### New CLI Command Mode
- Add new `CliCommandMode` enum value in `src/DalamudMCP.Cli/CliRuntimeOptions.cs`
- Add parsing branch in `CliRuntimeOptions.TryParse`
- Create runner method in `src/DalamudMCP.Cli/CliProgram.cs` switch expression
- Create runner class following pattern of `CliMcpServerRunner` or `CliHttpServerRunner`

### New Protocol Feature
- Protocol contract: `src/DalamudMCP.Protocol/ProtocolContract.cs`
- New envelope type if needed: `src/DalamudMCP.Protocol/` as `[MemoryPackable]` records
- Client method: `src/DalamudMCP.Protocol/NamedPipeProtocolClient.cs`
- Server dispatch: `src/DalamudMCP.Protocol/NamedPipeProtocolServer.cs` or via `OperationProtocolDispatcher` in Plugin

### New Configuration Option
- Add property to `PluginUiConfiguration` in `src/DalamudMCP.Plugin/Configuration/`
- Add accessor to `IPluginUiConfigurationAccessor` if exposing as read-only
- Add UI element in `PluginConfigWindow` / `PluginConfigWindowModel`
- Default value in `PluginUiConfigurationStore`

### Utility / Shared Code
- Framework-level abstractions: `src/DalamudMCP.Framework/`
- CLI-specific utilities: `src/DalamudMCP.Framework.Cli/` (if reusable) or `src/DalamudMCP.Cli/` (if CLI-specific)

## Special Directories

### `.tools/`
- **Purpose:** Local tool manifest directory for `dotnet tool` commands.
- **Generated:** Yes (by `dotnet tool install`)
- **Committed:** Yes (standard .NET tool manifest)

### `build/`
- **Purpose:** PowerShell build script helpers for common development workflows.
- **Generated:** No
- **Committed:** Yes
- **Note:** Not intended for production deployment scripts. CI uses GitHub Actions workflows in `.github/workflows/`.

### `tests/*/Samples/`
- **Purpose:** Sample input files for source generator snapshot tests.
- **Generated:** No (hand-written test fixtures)
- **Committed:** Yes

---

*Structure analysis: 2026-04-30*
