# Coding Conventions

**Analysis Date:** 2026-04-30

## Naming Patterns

**Files:**
- PascalCase that matches the primary type name: `OperationContext.cs`, `NamedPipeProtocolClient.cs`
- Source generator output uses `.g.cs` suffix: `GeneratedOperationRegistry.g.cs`
- Test files use `<ClassUnderTest>Tests.cs`: `CliApplicationTests.cs`, `OperationContextTests.cs`

**Classes:**
- PascalCase. Sealed by default unless inheritance is intended: `public sealed class OperationContext`, `public class OperationAttribute` (non-sealed)
- Samples/test helpers use `internal sealed class`: `internal sealed class WeatherPreviewFormatter`

**Interfaces:**
- PascalCase with `I` prefix: `IOperation<TRequest, TResult>`, `IResultFormatter<TResult>`, `IProtocolOperationClient`, `ICliInvoker`, `IOperationInvoker`

**Methods:**
- PascalCase. Async methods use `Async` suffix: `ExecuteAsync`, `DescribeOperationsAsync`, `ForCli`, `GetRequiredService`
- Test methods use descriptive snake_case: `ForCli_CreatesCliContext`, `GetService_ReturnsRegisteredService`, `Constructor_RejectsEmptyOperationId`

**Variables (local):**
- camelCase: `operationId`, `normalizedRequestType`, `requestBytes`

**Fields (private):**
- camelCase with `this.` prefix in constructors: `this.pipeName`, `this.handler`, `this.connectionIdleTimeout`
- No `_` prefix convention observed in production code (unlike some C# conventions)

**Parameters:**
- camelCase: `string operationId`, `InvocationSurface surface`, `IServiceProvider? services`

**Types/Records:**
- PascalCase for records, record structs, enums: `OperationDescriptor`, `ProtocolRequestEnvelope`, `InvocationSurface`
- Record fields use PascalCase: `ProtocolRequestPayload.Format`, `ProtocolResponseEnvelope.Success`

## Code Style

**Formatting:**
- Tool: `dotnet format` (no `.editorconfig`-based style analysis, run as `dotnet format` from CLI)
- Indent: 4 spaces (configured in `.editorconfig`: `indent_size = 4`, `indent_style = space`)
- Line endings: CRLF (`.editorconfig`: `end_of_line = crlf`)
- UTF-8 encoding (`.editorconfig`: `charset = utf-8`)
- Final newline required (`.editorconfig`: `insert_final_newline = true`)
- Trim trailing whitespace (`.editorconfig`: `trim_trailing_whitespace = true`)
- New line before open brace for all constructs (`.editorconfig`: `csharp_new_line_before_open_brace = all`)

**Enforced via .editorconfig (severity = error):**
- Accessibility modifiers must be explicit: `dotnet_style_require_accessibility_modifiers = always:error`
- File-scoped namespaces: `csharp_style_namespace_declarations = file_scoped:error`
- `using` directives outside namespace: `csharp_using_directive_placement = outside_namespace:error`
- Sort `System.*` using directives first: `dotnet_sort_system_directives_first = true`
- Remove unused imports (IDE0005): `dotnet_diagnostic.IDE0005.severity = error`
- File-scoped namespace (IDE0161): `dotnet_diagnostic.IDE0161.severity = error`

**Enforced via Directory.Build.props:**
- Nullable enabled: `<Nullable>enable</Nullable>`
- Treat warnings as errors: `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- Nullable warnings as errors: `<WarningsAsErrors>$(WarningsAsErrors);nullable</WarningsAsErrors>`
- Code style enforced in build: `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`
- Analysis level: `<AnalysisLevel>latest-recommended</AnalysisLevel>`
- .NET analyzers enabled: `<EnableNETAnalyzers>true</EnableNETAnalyzers>`
- Documentation file generation: `<GenerateDocumentationFile>true</GenerateDocumentationFile>` (with 1591 suppressed)

**Suppressed Diagnostics:**
- CA1707 (none): Underscore in member name
- CA1716 (none): Identifiers should not match keywords
- CA1724 (none): Type names should not match namespaces
- CA1812 (none): Uninstantiated internal classes
- CA2007 (none): ConfigureAwait (codebase uses `.ConfigureAwait(false)` deliberately)
- IDE0005 (error): unused imports must be removed
- IDE0161 (error): must use file-scoped namespace

**Enforced as error:**
- CA2211 (error): Non-constant fields should not be visible
- CA2227 (error): Collection properties should be read only
- CA2252 (error): Opt in to preview features

## Import Organization

**Order:**
1. `System.*` namespaces first (enforced by `dotnet_sort_system_directives_first = true`)
2. Third-party/DalamudMCP namespaces second
3. Blank line separating groups

**Global Usings:**
- Test projects use `<Using Include="Xunit" />` in `.csproj` for global `Xunit` import
- Production code relies on `<ImplicitUsings>enable</ImplicitUsings>` for `System.*` namespaces

**Path Aliases:**
- No custom path aliases used (no `$rootNamespace` or folder-based aliasing)

## Error Handling

**Patterns:**
- Argument validation at method entry: `ArgumentNullException.ThrowIfNull(name)` or `ArgumentException.ThrowIfNullOrWhiteSpace(name)`
- Runtime state errors: `throw new InvalidOperationException("...")`
- Argument range errors: `throw new ArgumentOutOfRangeException(nameof(position), "...")`
- Exception filters with `when` for targeted catching:
  ```csharp
  catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
  catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
  catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
  ```
- Silent catch for cancellation: `catch (OperationCanceledException) { }` in cleanup paths
- Custom source generator diagnostics via `DiagnosticDescriptor` with `DMCF001`-`DMCF005` IDs
- Service resolution: `throw new InvalidOperationException($"Required service '{type.FullName}' was not available.")` pattern in `OperationBinding`, `McpBinding`, `CliBinding`

## Logging

**Framework:**
- CLI layer uses `ILoggerFactory` / `ILogger<T>` via `Microsoft.Extensions.Logging` (`CliHttpServerRunner.cs`)
- Protocol layer and Framework layer do not use logging -- errors propagate as exceptions
- No centralized logging abstraction or dependency

**Patterns:**
- `builder.Logging.ClearProviders()` -- default providers cleared in HTTP server mode
- No structured logging framework (Serilog, NLog, etc.)

## Comments

**When to Comment:**
- Sparse comments in library code; code is self-documenting via descriptive identifiers
- Generated files include `// <auto-generated/>` header
- No XML doc comments visible on public API surface (suppressed via `NoWarn 1591`)

**JSDoc/TSDoc:**
- Not applicable (C# project). XML doc generation is enabled (`GenerateDocumentationFile = true`) but 1591 (missing XML comment) is suppressed.

## Function Design

**Size:**
- Methods tend to be focused and moderate in length (20-80 lines typical)
- Larger methods exist in source generator (`OperationDescriptorGenerator.cs` has methods up to ~150 lines) and `CliApplication.cs`

**Parameters:**
- 2-6 parameters typical
- `CancellationToken` is always the last parameter, with `= default`
- Dictionary overloads for IServiceProvider accept services as nullable last parameter

**Return Values:**
- `ValueTask<T>` preferred for synchronous+async paths; `Task<T>` used in some cases
- `.ConfigureAwait(false)` used consistently on all await calls
- `bool Try*` pattern used with `out` parameter for lookup operations: `TryFind`, `TryInvoke`, `TryResolveOperation`

## Module Design

**Exports:**
- Public types in each namespace act as the API surface
- Internal types reserved for test samples and internal helpers
- Static utility classes for binding helpers: `OperationBinding`, `McpBinding`, `CliBinding`

**Barrel Files:**
- Not used. Each type in its own file.
- `AssemblyMarker.cs` in `DalamudMCP.Protocol` is a marker type for assembly scanning.

**Source Generator Integration:**
- Generator project `DalamudMCP.Framework.Generators` produces types in `DalamudMCP.Framework.Generated` namespace
- Generator referenced as Analyzer: `<ProjectReference OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`
- Generated files: `GeneratedOperationRegistry.g.cs`, `GeneratedOperationInvoker.g.cs`, `GeneratedCliInvoker.g.cs`, `GeneratedMcpTools.g.cs`

## Constructor Patterns

**Primary Constructors:**
- Used extensively throughout the codebase (C# 12+ feature):
  ```csharp
  public sealed class NamedPipeProtocolClient(string pipeName, TimeSpan? connectTimeout = null) : IProtocolOperationClient
  public class OperationAttribute(string operationId) : Attribute
  ```
- Parameter validation happens in field/property initializers:
  ```csharp
  private readonly string pipeName = string.IsNullOrWhiteSpace(pipeName)
      ? throw new ArgumentException("...", nameof(pipeName))
      : pipeName.Trim();
  ```

**Static Factory Methods:**
- Preferred over constructor overloading for semantic clarity:
  ```csharp
  public static OperationContext ForCli(string? operationId = null, ...)
  public static OperationContext ForMcp(string? operationId = null, ...)
  public static OperationContext ForProtocol(string? operationId = null, ...)
  ```

**Records:**
- Positional records for DTOs:
  ```csharp
  public sealed record ParameterDescriptor(string Name, Type ParameterType, ...);
  ```
- `readonly record struct` for lightweight value types:
  ```csharp
  public readonly record struct ProtocolInvocationResult(...)
  ```
- `sealed partial record` for MemoryPack-serializable types:
  ```csharp
  [MemoryPackable]
  public sealed partial record ProtocolRequestEnvelope(...)
  ```

## Property Patterns

- `{ get; init; }` for request DTO properties (immutable after construction)
- `{ get; }` for computed or primary-constructor-backed properties
- Auto-properties with simple getters/setters
- Expression-bodied properties for simple computed values

---

*Convention analysis: 2026-04-30*
