# Codebase Concerns

**Analysis Date:** 2026-04-30

## Tech Debt

### Dual Solution File Maintenance

- Issue: Two solution files (`DalamudMCP.slnx` and `DalamudMCP.CI.slnx`) must be kept in sync manually. The CI solution excludes the Plugin project and its tests (which require Dalamud SDK), but any project added to one solution but not the other will silently break builds or testing.
- Files: `E:/卫月插件/DalamudMCP/DalamudMCP.slnx`, `E:/卫月插件/DalamudMCP/DalamudMCP.CI.slnx`
- Impact: Risk of drift between CI and local build configurations. New test projects or source projects may not be added to both solutions.
- Fix approach: Either use a single solution with conditional build targets, or add a CI validation step that verifies both solutions are consistent.

### Source Generator as Largest File

- Issue: `OperationDescriptorGenerator.cs` is the largest file in the codebase at 1,584 lines. This Roslyn source generator is complex, hard to debug (no runtime stepping), and difficult to modify without breaking all generated operation code.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Framework.Generators/OperationDescriptorGenerator.cs`
- Impact: High cognitive load for maintenance. Generator bugs are hard to reproduce and diagnose since they manifest only at compile time with generated code.
- Fix approach: Split the generator into smaller focused partial classes (one for candidate extraction, one for code emission, one for diagnostics).

### Duplicate Service Resolution Logic

- Issue: Both `OperationBinding.cs` and `McpBinding.cs` implement identical service resolution logic (`GetRequiredService` / `GetRequiredServiceOrThrow`). This is duplicated code.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Framework/OperationBinding.cs`, `E:/卫月插件/DalamudMCP/src/DalamudMCP.Framework.Mcp/McpBinding.cs`
- Impact: Changes to service resolution behavior must be made in two places. Increases maintenance surface.
- Fix approach: Consolidate into a single shared utility in the Framework project, reference from Mcp project.

### Sync-over-Async Patterns

- Issue: Several locations block on async operations using `.GetAwaiter().GetResult()`, risking deadlocks in the Dalamud framework thread context.
  - `PluginEntryPoint.cs:61` -- `compositionRoot.StartAsync().GetAwaiter().GetResult()`
  - `PluginEntryPoint.cs:97` -- `compositionRoot.DisposeAsync().AsTask().GetAwaiter().GetResult()`
  - `PluginMcpServerController.cs:195` -- `response.Content.ReadAsStringAsync().GetAwaiter().GetResult()`
  - `PluginMcpServerController.cs:559` -- `response.Content.ReadAsStringAsync().GetAwaiter().GetResult()`
  - `CliMcpServerRunner.cs:40-43` -- `Task.Run(async () => ...).GetAwaiter().GetResult()`
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/PluginEntryPoint.cs`, `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/Hosting/PluginMcpServerController.cs`, `E:/卫月插件/DalamudMCP/src/DalamudMCP.Cli/CliMcpServerRunner.cs`
- Impact: Potential deadlocks in SynchronizationContext-bound environments. The blocking pattern in `PluginEntryPoint` constructor (line 61) runs during Dalamud plugin construction which may be on the framework thread.
- Fix approach: Refactor plugin entry point to use async initialization pattern. Convert `PluginMcpServerController.ProbeEndpoint` to fully async.

### Thread.Sleep for Polling

- Issue: `PluginMcpServerController` uses `Thread.Sleep(100)` in `WaitForAvailability` (line 391) and `Thread.Sleep(150)` in `TryTerminateStaleEndpoint` (line 448). These block the calling thread rather than using async timers.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/Hosting/PluginMcpServerController.cs`
- Impact: Blocking the thread during startup polling could delay game frame processing if triggered on a sensitive thread.
- Fix approach: Replace with `Task.Delay` or use a timer-based approach with `await`.

### Game Screenshots Stored as Uncompressed BMP

- Issue: `GameScreenshotOperation.cs` saves screenshots as raw 32-bit BMP files via GDI BitBlt. These are large (e.g., ~8MB for a 1080p screenshot), and the files are stored on disk without cleanup management.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/Operations/GameScreenshotOperation.cs` (lines 370-395, `WriteBitmapFile`)
- Impact: Users taking multiple screenshots will accumulate large files in the `captures/` directory with no cleanup mechanism.
- Fix approach: Add PNG compression via `System.Drawing.Common` or `ImageSharp`. Add a configurable retention/cleanup policy.

## Security Considerations

### Arbitrary Plugin IPC Invocation

- Risk: The `UnsafeInvokePluginIpcOperation` allows invoking any Dalamud plugin IPC function callgate by name with arbitrary typed arguments. This is a developer escape hatch that bypasses all type safety and API contracts. Although gated by `EnableUnsafeOperations` (default off), if a user enables it, any MCP client can call arbitrary plugin IPC functions.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/Operations/UnsafeInvokePluginIpcOperation.cs`
- Current mitigation: `PluginOperationExposurePolicy.UnsafeOperationIds` (line 21-23) hardcodes the single unsafe operation ID. The `EnableUnsafeOperations` config flag must be explicitly enabled.
- Recommendations: Add a confirmation dialog or one-time approval when unsafe operations are enabled. Log all invocations of unsafe operations with full parameter details.

### No Authentication on Named Pipe

- Risk: `NamedPipeProtocolServer` creates named pipe instances with `NamedPipeServerStream.MaxAllowedServerInstances` and no explicit pipe security/access control. Any local process on the same machine can potentially connect to the pipe if it can discover the pipe name. Pipe names follow a predictable pattern (`DalamudMCP.{ProcessId}.{InstanceId}`).
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Protocol/NamedPipeProtocolServer.cs` (lines 79-84), `E:/卫月插件/DalamudMCP/src/DalamudMCP.Protocol/NamedPipeProtocolClient.cs`
- Current mitigation: The pipe name includes a random 8-character hex suffix (`Guid.NewGuid().ToString("N")[..8]`) and process ID, making it non-trivial to guess.
- Recommendations: Add a `PipeSecurity` object with DACL restricting access to the current user only. Consider adding a shared secret exchanged out-of-band.

### No Authentication on HTTP MCP Endpoint

- Risk: The HTTP MCP server (`CliHttpServerRunner`) binds to `127.0.0.1:{port}` with no authentication. Any local process on the machine can send MCP requests to control the game -- targeting entities, teleporting, using duty actions, interacting with targets, and sending addon input.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Cli/CliHttpServerRunner.cs` (lines 82-86)
- Current mitigation: Binds only to `127.0.0.1` (loopback only, no remote access).
- Recommendations: Add a bearer-token or shared-secret authentication mechanism for the HTTP MCP endpoint. Consider optional CORS enforcement.

### IPC Discovery File Exposure

- Risk: `ProtocolClientDiscovery.Write` writes an `active-instance.json` file to the XIVLauncher plugin config directory containing the pipe name and process ID. Any local process can read this file to discover how to connect to the named pipe.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Protocol/ProtocolClientDiscovery.cs`
- Current mitigation: The file path is in the user's AppData directory which has default filesystem permissions.
- Recommendations: Restrict the discovery file permissions to the current user only. Or encrypt the pipe name in the discovery file.

### Game Window Capture via GDI

- Risk: `GameScreenshotOperation` uses GDI `BitBlt` and `PrintWindow` to capture the game window. This works by reading the game's DirectX backbuffer. These operations can potentially be invoked without the user's awareness (if auto-start is enabled).
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/Operations/GameScreenshotOperation.cs`
- Current mitigation: Screenshot operations require the `game.screenshot` operation to be invoked explicitly.
- Recommendations: Add a visual indicator in-game when a screenshot is captured via MCP. Consider a cooldown or confirmation for remote screenshot requests.

## Performance Bottlenecks

### Named Pipe Single-Request-Response Model

- Problem: The named pipe protocol enforces a strict request-response per-connection model. Each connection is created, sends one request, receives one response, and then the server closes the connection (5-second idle timeout). This means high-latency operations (like teleporting or moving) block the pipe for the duration.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Protocol/NamedPipeProtocolServer.cs` (lines 103-133)
- Cause: The `HandleConnectionAsync` method reads exactly one frame and sends exactly one response per connection.
- Improvement path: Consider persistent connections with multiplexed request IDs for concurrent operations. Or at minimum, keep connections alive for the duration of a session.

### Generator Compilation Cost

- Problem: The `OperationDescriptorGenerator` runs on every compilation, processing all types with `[Operation]` attributes. With 30+ operation classes, this adds measurable overhead to each build.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Framework.Generators/OperationDescriptorGenerator.cs`
- Cause: The generator uses `ForAttributeWithMetadataName` which triggers on every attributed type. The `Execute` method is called with all candidates in a single batch.
- Improvement path: Add caching or incrementalization to only reprocess changed types. Add `[GeneratorSupportsIncrementalGeneration]` optimizations if not already present.

### HTTP Server Startup Probe

- Problem: `PluginMcpServerController` uses a polling loop with `Thread.Sleep(100)` for up to 20 iterations (2 seconds) waiting for the HTTP server to become available. During this time, the thread is blocked.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/Hosting/PluginMcpServerController.cs` (lines 380-395)
- Cause: The synchronous `Thread.Sleep` pattern and synchronous `ProbeEndpoint` call.
- Improvement path: Use async `Task.Delay` and truly async HTTP probing (the `ProbeHttpClient` is shared but the probe method uses `.Send()` sync variant, line 185).

## Fragile Areas

### Source Generator Coupling to Framework Attributes

- Why fragile: The generator (`OperationDescriptorGenerator.cs`) uses hardcoded metadata names for all framework attributes (`OperationAttribute`, `OptionAttribute`, `ArgumentAttribute`, etc.). Any rename or restructuring of the attributes in the Framework project will silently break code generation without compile-time errors until the generator is rebuilt.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Framework.Generators/OperationDescriptorGenerator.cs` (lines 12-25)
- Safe modification: Always verify both the attribute class names in the Framework project AND the metadata name strings in the generator are updated in tandem.
- Test coverage: The `DalamudMCP.Framework.Generators.Tests` project tests generator diagnostics and output, but may not catch all edge cases.

### PluginMcpServerController State Machine

- Why fragile: This class manages a complex state machine (process lifecycle, endpoint probing, stale detection, cached availability) with multiple overlapping state fields (`process`, `cachedEndpointAvailable`, `endpointUri`, `nextProbeAtUtc`, `probeTask`). The `syncRoot` lock guards only probe task creation, not all state mutations.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/Hosting/PluginMcpServerController.cs` (entire file, 637 lines)
- Safe modification: Be extremely careful with the `IsRunning` property and `Start()`/`Stop()` methods -- they have implicit ordering requirements. Always test start-stop-start sequences.
- Test coverage: `PluginMcpServerControllerTests.cs` exists (167 lines) but may not cover all state machine edge cases.

### Operation Files with FFXIVClientStructs Dependencies

- Why fragile: Operations like `AddonInputOperation`, `AddonSelectMenuItemOperation`, `AddonEventOperation`, `InteractWithTargetOperation` use raw `FFXIVClientStructs` pointers with `unsafe` blocks. These structures depend on exact game memory layouts and may break on game patches.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/Operations/AddonInputOperation.cs`, `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/Operations/AddonSelectMenuItemOperation.cs`, `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/Operations/InteractWithTargetOperation.cs`
- Safe modification: Never change the struct member access patterns without validating against the game's actual memory layout. These are inherently tied to specific game versions.
- Test coverage: Operation tests exist but cannot be run in CI (excluded from `DalamudMCP.CI.slnx`).

## Test Coverage Gaps

### Plugin Operations Tests Not in CI

- What's not tested in CI: All `DalamudMCP.Plugin.Operations.Tests` (~2,000+ lines, 23 test files) and `DalamudMCP.Plugin.Tests` (~1,300 lines, 6 test files) are excluded from the CI solution. These tests cover the core game-interfacing operations: teleporting, targeting, addon interaction, movement, inventory, screenshots, and IPC invocation.
- Files: All tests under `E:/卫月插件/DalamudMCP/tests/DalamudMCP.Plugin.Operations.Tests/` and `E:/卫月插件/DalamudMCP/tests/DalamudMCP.Plugin.Tests/`
- Risk: Operation logic changes or regressions will only be caught by local manual testing, never in CI.
- Priority: High

### CI Solution Excludes Plugin Project

- What's not tested in CI: The entire `DalamudMCP.Plugin` project is excluded from CI because it requires the Dalamud SDK. This means the plugin composition root, configuration store, UI window, and service wiring are never validated in CI builds.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/` (all files)
- Risk: Compilation errors in the plugin project are not caught by CI. Integration issues between the plugin and the protocol/Cli layers can go unnoticed.
- Priority: Medium

### Test-to-Source Ratio

- The codebase has ~6,145 lines of test code vs ~15,299 lines of source code (~40% ratio). However, most tests are unit tests with mocked dependencies. There are no integration or E2E tests.
- Risk: Cross-component integration (e.g., protocol client <-> server, generator <-> framework) is not validated automatically.
- Priority: Low (acceptable for early-stage project)

## Dependencies at Risk

### Dalamud SDK Version Coupling

- Risk: `DalamudMCP.Plugin` uses `Dalamud.NET.Sdk/14.0.2` and targets Dalamud API Level 14 (`DalamudMCP.json` line 14). Both the SDK version and API level are tightly coupled to a specific Dalamud release. When Dalamud updates, the plugin cannot load without updating both.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj`, `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/DalamudMCP.json`
- Impact: A Dalamud update that bumps the API level will break the plugin. There is no graceful degradation or version negotiation.
- Migration plan: Monitor Dalamud API level changes. Maintain a version compatibility table.

### MemoryPack Version Lock

- Risk: `MemoryPack` version 1.21.4 is locked in `packages.lock.json` across multiple projects. If the protocol's binary serialization format changes in a MemoryPack upgrade, all messages between the CLI and Plugin will fail.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj`, `E:/卫月插件/DalamudMCP/src/DalamudMCP.Protocol/DalamudMCP.Protocol.csproj`
- Impact: All six `packages.lock.json` files must be in sync for restore to succeed. The protocol contract is versioned independently (`ProtocolContract.CurrentVersion = "2.0.0"`), but MemoryPack binary compatibility is assumed.
- Migration plan: Pin MemoryPack version and add wire-format compatibility tests during upgrades.

### Third-Party Plugin IPC Dependencies

- Risk: Operations like `TeleportToAetheryteOperation` and `MoveToEntityOperation` have hard runtime dependencies on external plugins (Lifestream, Vnavmesh) via IPC. If users do not have these plugins installed, these operations silently fall back or produce misleading errors.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/Operations/TeleportToAetheryteOperation.cs` (lines 378-398), `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/Operations/MoveToEntityOperation.cs`
- Impact: Users may be confused when teleport "succeeds" but only falls back to a non-functional Lifestream IPC. The fallback logic catches all exceptions (`catch { }`) in `TryStartLifestreamAethernetTeleport` (line 330-334), potentially hiding errors.
- Migration plan: Surface the availability of third-party IPC dependencies in the session status. Clearly report when Lifestream/Vnavmesh is unavailable.

## Missing Critical Features

### No Graceful Dalamud API Level Mismatch

- Problem: If the plugin is installed on a Dalamud version with a different API level, it will fail to load entirely with no user-facing error message explaining why. The `DalamudApiLevel` field is read at load time.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/DalamudMCP.json`
- Blocks: Users on older/newer Dalamud versions cannot use the plugin.

### No Screenshot Cleanup Mechanism

- Problem: `GameScreenshotOperation` saves BMP files to the `captures/` directory (under the plugin config directory) but there is no mechanism to clean up old captures. Over time, this directory will accumulate large BMP files.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/Operations/GameScreenshotOperation.cs`
- Blocks: Long-term usage of the screenshot feature will consume disk space.

### Configuration Migration Not Implemented

- Problem: `PluginUiConfiguration` has `Version = 3` but the codebase contains no migration logic. If a user upgrades from an older version with a different config schema, the `IPluginConfiguration.GetPluginConfig()` may return a deserialized object with default values, silently losing settings.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/Configuration/PluginUiConfiguration.cs`, `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/Configuration/PluginUiConfigurationStore.cs`
- Blocks: Safe configuration evolution across plugin versions.

### No Rate Limiting on Operations

- Problem: MCP operations (especially action operations like teleport, addon input, interact) have no rate limiting, throttling, or cooldown enforcement. A malicious or buggy MCP client could spam operations rapidly.
- Files: `E:/卫月插件/DalamudMCP/src/DalamudMCP.Plugin/Hosting/OperationProtocolDispatcher.cs`, `E:/卫月插件/DalamudMCP/src/DalamudMCP.Cli/RemoteMcpToolService.cs`
- Blocks: Protection against rapid-fire operations that could disrupt gameplay.

---

*Concerns audit: 2026-04-30*
