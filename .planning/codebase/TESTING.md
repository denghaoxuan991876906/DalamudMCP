# Testing Patterns

**Analysis Date:** 2026-04-30

## Test Framework

**Runner:**
- xUnit v3 (3.2.2) with Microsoft Testing Platform runner
- Package: `xunit.v3.mtp-v2`
- Config file: `xunit.runner.json` (link from solution root via `Directory.Build.props`)
- Runner config in `global.json`: `"runner": "Microsoft.Testing.Platform"`

**Assertion Library:**
- xUnit's built-in `Assert` class (global import via `<Using Include="Xunit" />` in each test `.csproj`)

**Coverage (optional):**
- `coverlet.MTP` version 8.0.0
- Enforced via `<CoverageThreshold>90</CoverageThreshold>` in each test project `.csproj`

**Run Commands:**
```bash
./build/test.ps1                              # Run all test projects
./build/test.ps1 -Solution DalamudMCP.CI.slnx  # Run using CI solution
./build/test.ps1 -NoBuild                      # Skip build phase
./build/quality.ps1                            # Full: restore -> build -> format (verify) -> test -> architecture checks
```

**CI:**
- GitHub Actions workflow (`.github/workflows/ci.yml`): runs `./build/quality.ps1 -Solution ./DalamudMCP.CI.slnx` on `windows-latest`
- Steps: checkout v5, setup-dotnet v5 (with global.json, cache enabled), run quality script

## Test File Organization

**Location:**
- All tests live under `tests/` directory at repository root
- Directory structure mirrors `src/` project layout:
```
tests/
├── DalamudMCP.Framework.Tests/
├── DalamudMCP.Framework.Cli.Tests/
├── DalamudMCP.Framework.Mcp.Tests/
├── DalamudMCP.Framework.Generators.Tests/
├── DalamudMCP.Cli.Tests/
├── DalamudMCP.Protocol.Tests/
├── DalamudMCP.Plugin.Tests/
├── DalamudMCP.Plugin.Operations.Tests/
```

**Naming:**
- Test classes: `<ClassName>Tests` (e.g., `OperationContextTests`, `CliApplicationTests`)
- Test files: `<ClassName>Tests.cs` matching class name
- Sample directories: `Samples/` subdirectory within each test project (e.g., `tests/DalamudMCP.Framework.Cli.Tests/Samples/SampleCliOperations.cs`)

**Structure:**
```
DalamudMCP.Framework.Cli.Tests/
├── CliApplicationTests.cs
├── Samples/
│   └── SampleCliOperations.cs
├── DalamudMCP.Framework.Cli.Tests.csproj
└── packages.lock.json
```

## Test Structure

**Suite Organization:**
- All test classes are `public sealed class`
- All test methods use `[Fact]` attribute (no `[Theory]` with `[InlineData]` observed)
- Test methods return `void` for synchronous tests, `async Task` for async tests
- Test class namespace matches project root namespace: `namespace DalamudMCP.Framework.Tests;`

**Patterns:**
```csharp
namespace DalamudMCP.Framework.Tests;

public sealed class OperationContextTests
{
    [Fact]
    public void ForCli_CreatesCliContext()
    {
        OperationContext context = OperationContext.ForCli("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("hello", context.OperationId);
        Assert.Equal(InvocationSurface.Cli, context.Surface);
    }

    [Fact]
    public void GetRequiredService_ThrowsWhenMissing()
    {
        OperationContext context = OperationContext.ForMcp("hello", cancellationToken: TestContext.Current.CancellationToken);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            context.GetRequiredService<TestService>());

        Assert.Contains(exception.Message, typeof(TestService).FullName, StringComparison.Ordinal);
    }
}
```

**Standard xUnit lifecycle:**
- No shared class fixtures or collection fixtures used beyond the serial group (see below)
- `IDisposable` implemented for cleanup when needed (e.g., `CliProgramTests`)

**Test method naming convention:**
- Verb_ExpectedBehavior: `ForCli_CreatesCliContext`, `Constructor_RejectsEmptyOperationId`
- Full sentence: `RunAsync_returns_usage_error_for_missing_required_argument`
- Descriptive assertion: `Generated_tools_type_exposes_only_mcp_visible_operations`

## Mocking

**Framework:**
- No mocking library (no Moq, NSubstitute, FakeItEasy, etc.)
- Hand-written fake/stub implementations as `private sealed class` within the test file

**Patterns:**
```csharp
// From tests/DalamudMCP.Cli.Tests/ProtocolBackedSourcesTests.cs
private sealed class FakeProtocolOperationClient : IProtocolOperationClient
{
    private ProtocolInvocationResult response;
    private Exception? exception;
    private bool hasResponse;

    public string? LastRequestType { get; private set; }
    public ProtocolRequestPayload LastPayload { get; private set; }

    public FakeProtocolOperationClient WithInvocationResult(ProtocolInvocationResult value)
    {
        response = value;
        hasResponse = true;
        exception = null;
        return this;
    }

    public FakeProtocolOperationClient WithException(Exception value)
    {
        exception = value;
        hasResponse = false;
        return this;
    }

    public ValueTask<ProtocolInvocationResult> InvokeAsync(...)
    {
        if (exception is not null)
            throw exception;
        if (!hasResponse)
            throw new InvalidOperationException("No protocol response was configured.");
        LastRequestType = requestType;
        LastPayload = request;
        return ValueTask.FromResult(response);
    }
    // ...
}
```

**Pattern for service provider stubs:**
```csharp
// From tests/DalamudMCP.Framework.Tests/OperationContextTests.cs
private sealed class DictionaryServiceProvider(Dictionary<Type, object> services) : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return services.TryGetValue(serviceType, out object? service) ? service : null;
    }
}
```

**What to Mock:**
- External dependencies (protocol clients, service providers, configuration stores)
- `ICliInvoker` interface for testing `CliApplication` error paths
- `IProtocolOperationClient` for testing remote CLI invokers
- `IOperationInvoker` for testing protocol dispatchers

**What NOT to Mock:**
- Framework value types (`OperationContext`, `OperationDescriptor`)
- Simple utility classes
- Generated types (`GeneratedCliInvoker`, `GeneratedOperationRegistry` are used directly)

## Fixtures and Factories

**Test Data:**
- Sample operations defined as `internal static` classes in `Samples/` directories
- Sample class-based operations directly in test files for focused tests
- Inline anonymous objects used for JSON payloads in integration-style tests

**Sample Operations pattern:**
```csharp
// From tests/DalamudMCP.Framework.Cli.Tests/Samples/SampleCliOperations.cs
internal static class SampleCliOperations
{
    [Operation("math.add", Description = "Add two integers.")]
    [CliCommand("math", "add")]
    [Alias("sum", "calc plus")]
    public static Task<int> AddAsync(
        [Argument(0, Name = "x", Description = "Left operand")] int x,
        [Argument(1, Name = "y", Description = "Right operand")] int y,
        [FromServices] IMathOffsetProvider offsets,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(x + y + offsets.Offset);
    }
}
```

**Location:**
- `tests/*.Tests/Samples/` -- shared sample classes consumed by generator/runner tests
- `tests/*.Tests/*.cs` -- inline test helpers (private sealed classes)

## Coverage

**Requirements:**
- 90% threshold enforced via `<CoverageThreshold>90</CoverageThreshold>` in each test `.csproj`
- Enforced by `coverlet.MTP` package during test execution

**View Coverage:**
```bash
dotnet test --collect "Code Coverage"
```
(Standard `dotnet test` with coverage collection via coverlet)

## Test Types

**Unit Tests:**
- Primary test approach. Every project is tested independently.
- Focus on individual classes and their behavior.
- External dependencies replaced with fakes/stubs.
- Source files under `src/DalamudMCP.Framework/` tested by `tests/DalamudMCP.Framework.Tests/`

**Integration Tests (with real infrastructure):**
- Named pipe client/server round trips (e.g., `NamedPipeProtocolClientServerTests`)
- HTTP endpoint serving via ASP.NET (e.g., `CliHttpServerRunnerTests`)
- Uses real `NamedPipeServerStream` / `NamedPipeClientStream` at OS level
- Discovery environment scoping uses temp directories and env vars

**Generator Tests:**
- `OperationDescriptorGeneratorDiagnosticsTests` uses Roslyn APIs to compile source strings and run the source generator
- Pattern: `CSharpCompilation.Create()` + `CSharpGeneratorDriver.Create()` + `RunGenerators()` + assert diagnostics
- `GeneratedOperationRegistryTests` tests the actual generator output by referencing compiled generated code

**E2E Tests:**
- Not explicitly identified. HTTP server tests (`CliHttpServerRunnerTests`) come closest with real HTTP client requests to a spawned server.

## Test Infrastructure Patterns

**Serialization for environment-dependent tests:**
```csharp
// From tests/DalamudMCP.Cli.Tests/DiscoveryEnvironmentCollection.cs
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DiscoveryEnvironmentSerialGroup
{
    public const string Name = "Discovery environment";
}

// Applied to test class:
[Collection(DiscoveryEnvironmentSerialGroup.Name)]
public sealed class CliProgramTests : IDisposable
{
    // ...
}
```
- `CliProgramTests` uses `[Collection]` attribute to disable parallel execution because tests modify environment variables.
- `DiscoveryEnvironmentScope` manages env var save/restore and temp directory creation/cleanup.

**Project setup for generator tests:**
- Generator referenced as Analyzer with `ReferenceOutputAssembly="true"` and `PrivateAssets="all"` in `DalamudMCP.Framework.Generators.Tests.csproj`
- Generator referenced as Analyzer with `ReferenceOutputAssembly="false"` in other test projects

## Async Testing

**Patterns:**
```csharp
[Fact]
public async Task IOperation_SupportsInstanceBasedExecution()
{
    SampleClassBasedOperation operation = new();

    string result = await operation.ExecuteAsync(
        new SampleClassBasedOperation.Request(),
        OperationContext.ForCli("session.status", cancellationToken: TestContext.Current.CancellationToken));

    Assert.Equal("ok", result);
}
```

**CancellationToken in tests:**
- `TestContext.Current.CancellationToken` is the standard pattern for providing a cancellation token in test methods
- Replaces `CancellationToken.None` usage seen in earlier patterns

**Error Testing:**
```csharp
[Fact]
public void Constructor_RejectsEmptyOperationId()
{
    Assert.Throws<ArgumentException>(() => new OperationAttribute(" "));
}

[Fact]
public void GetRequiredService_ThrowsWhenMissing()
{
    OperationContext context = OperationContext.ForMcp("hello", cancellationToken: TestContext.Current.CancellationToken);

    InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
        context.GetRequiredService<TestService>());

    string expectedName = typeof(TestService).FullName ?? nameof(TestService);
    Assert.Contains(expectedName, exception.Message, StringComparison.Ordinal);
}

[Fact]
public async Task RemoteCliInvoker_propagates_protocol_errors()
{
    // ...
    InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await invocation);
    Assert.Contains("player_not_ready", exception.Message, StringComparison.Ordinal);
}
```

## Dependency Injection in Tests

**Patterns:**
- `ServiceCollection` from `Microsoft.Extensions.DependencyInjection` used for wiring test services
- Test-specific interfaces and implementations (`IMathOffsetProvider`, `ConstantMathOffsetProvider`)
- Service provider built and passed to the system under test:
```csharp
ServiceCollection services = new();
services.AddSingleton<IMathOffsetProvider>(new ConstantMathOffsetProvider(7));
services.AddTransient<MathScaleOperation>();
IServiceProvider serviceProvider = services.BuildServiceProvider();

CliApplication application = new(
    GeneratedOperationRegistry.Operations,
    new GeneratedCliInvoker(),
    serviceProvider);
```

## MemoryPack Serialization in Tests

- Test records are decorated with `[MemoryPackable]` and `[ProtocolOperation("...")]`
- `MemoryPack` serialization is tested via round-trip client/server patterns
- Inline creation of `ProtocolResponseEnvelope` / `ProtocolRequestEnvelope` for test scenarios

## Assert.Collection Pattern

Used extensively for asserting multiple items with individual validators:
```csharp
Assert.Collection(
    addDescriptor.Parameters,
    static parameter =>
    {
        Assert.Equal("x", parameter.Name);
        Assert.Equal(ParameterSource.Argument, parameter.Source);
        Assert.True(parameter.Required);
    },
    static parameter =>
    {
        Assert.Equal("y", parameter.Name);
        Assert.Equal(ParameterSource.Argument, parameter.Source);
        Assert.True(parameter.Required);
    });
```

---

*Testing analysis: 2026-04-30*
