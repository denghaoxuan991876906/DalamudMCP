# Phase 9: 可切换界面语言 — Research

**Researched:** 2026-05-01
**Domain:** Dalamud plugin UI localization / i18n
**Confidence:** HIGH

## Summary

Phase 9 makes the plugin UI switchable between Chinese and English. The Phase 8 predecessor already replaced all UI text strings in `PluginConfigWindow.cs` and `PluginConfigWindowModel.cs` with hardcoded Chinese. Phase 9 must undo this hardcoding and introduce a localization system that selects the display language at runtime based on a persisted user preference.

The recommended approach is a custom **JSON-based localization dictionary** with a DI-registered service, avoiding heavyweight dependencies like `XIVConfigUI` or satellite assemblies. Five categories of strings need localization: (1) ImGui config window labels/buttons, (2) model state strings, (3) operation row-derived text, (4) operation descriptions/summaries from attributes, and (5) operation result text from `IResultFormatter`. The CLI help text localization (criterion 5) is partially addressable within the plugin's config window display but requires protocol changes for the standalone CLI binary.

**Primary recommendation:** Implement a lightweight `IUiLocalization` interface + `JsonLocalization` implementation that loads `lang/en.json` and `lang/zh.json` at startup. Wire it through DI. Add `SelectedLanguage` to `PluginUiConfiguration` for persistence. Replace all `ImGui.Text("literal")` calls with `ImGui.Text(localization["key"])`. Add a language combo box in the config window header. Hook a `LanguageChanged` event to force model refresh on switch.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| UI text display | Plugin (ImGui draw) | — | All ImGui rendering lives in `PluginConfigWindow` |
| State/status text | Plugin (model) | — | `PluginConfigWindowModel` owns derived status strings |
| Language persistence | Plugin (config) | — | `PluginUiConfiguration` stores preference, `PluginUiConfigurationStore` persists it |
| Operation descriptions | Plugin (attributes) | Framework (definitions) | `[Operation]` and `[Option]` attributes define English descriptions in the Plugin project |
| Operation result text | Plugin (formatters) | Framework (interface) | `IResultFormatter<T>.FormatText` is implemented per-operation in Plugin |
| CLI binary help text | Standalone CLI | Framework.Cli (engine) | `CliApplication.GetUsage()` runs in the CLI process, not the plugin |

---

## Current UI Architecture

### Source File Map

| File | Purpose | Lines of UI Strings |
|------|---------|---------------------|
| `src/DalamudMCP.Plugin/Ui/PluginConfigWindow.cs` | ImGui draw logic — all labels, buttons, table headers, tooltips, tags | ~40 string literals |
| `src/DalamudMCP.Plugin/Ui/PluginConfigWindowModel.cs` | State-to-text conversion — status lines, exposure text, reader status | ~20 string literals |
| `src/DalamudMCP.Plugin/Configuration/PluginUiConfiguration.cs` | Config model — currently 3 bool properties | Needs 1 new property |
| `src/DalamudMCP.Plugin/PluginEntryPoint.cs` | DI composition — wires config window to services | No strings |
| `src/DalamudMCP.Framework.Cli/CliApplication.cs` | CLI help text generation | ~8 string literals |

### String Categories and Density

**Category 1 — ImGui config window text** (PluginConfigWindow.cs, ~40 strings):
- Window title: `"DalamudMCP 设置"`
- Header: `"FFXIV 观察、动作与 MCP 暴露的实时桥接。"`
- Badges: `"管道在线"`, `"管道离线"`, `"HTTP 在线"`, `"HTTP 已停"`
- Panel titles: `"运行时"`, `"命令台"`, `"HTTP 服务器"`, `"高级详情"`, `"操作"`
- Status line labels: `"发现"`, `"命名管道"`, `"读取器"`, `"动作工具"`, `"非安全工具"`
- Checkbox labels: `"启用 CLI/MCP 动作操作"`, `"启用非安全集成工具（仅开发者）"`, `"插件加载时自动启动 MCP HTTP 服务器"`
- Button labels: `"启动 MCP HTTP 服务器"`, `"停止 MCP HTTP 服务器"`, `"复制 MCP 端点"`, `"复制 MCP 服务器命令"`
- Table headers: `"操作"`, `"访问"`, `"状态"`, `"摘要"`
- Tags: `"动作"`, `"观察"`, `"非安全"`, `"可暴露"`
- Filters: `"搜索"`, `"仅显示已限制"`, `"仅显示有读取器"`
- Command card titles: `"CLI 快速检查"`, `"MCP 服务"`
- Command card button: `"复制玩家上下文命令"`, `"复制 MCP 服务命令"`
- Empty state: `"无操作匹配当前筛选条件。"`
- Help text lines (wrapped text below checkboxes): 3 strings
- Advanced panel prefix labels: `"管道"`, `"CLI 命令"`, `"MCP 服务"`, `"HTTP 命令"`

**Category 2 — Model state strings** (PluginConfigWindowModel.cs, ~20 strings):
- Protocol server status: `"服务器状态: 运行中"`, `"服务器状态: 已停止"`
- MCP server status: `"状态: 运行中"`, `"状态: 已停止"`
- Action operations: `"动作操作: 已启用"`, `"动作操作: 已禁用"`
- Unsafe operations: `"非安全操作: 已启用"`, `"非安全操作: 已禁用"`
- Reader status format: `"读取器状态: {n}/{m} 就绪"`
- Exposure status: `"暴露: 已禁用，等待非安全操作启用"`, `"暴露: 已禁用，等待动作操作启用"`
- Row reader status format: `"读取器: 就绪"`, `"读取器: 未就绪"`, `"读取器: {readiness} ({detail})"`
- Row prefix labels: `"CLI: "`, `"MCP: "`
- PipeName text prefix: `"当前管道 (高级): "`
- Endpoint text prefix: `"端点: "`
- Error text prefix: `"最近错误: "`

**Category 3 — Operation attribute strings** (20+ operation files):
- `[Operation(Description = "...", Summary = "...")]` — e.g., `"Gets the current player context."`, `"Gets player context."`
- `[Option("name", Description = "...")]` — e.g., `"Addon name to target."`

**Category 4 — Operation result text** (via IResultFormatter, ~20 formatters):
- `PlayerContextOperation.TextFormatter`: `$"{result.CharacterName} @ {result.HomeWorld} ({result.JobName} {result.JobLevel})"`
- `SessionStatusOperation.TextFormatter`: delegates to `result.SummaryText`
- `InventorySummaryOperation.TextFormatter`: delegates to `result.SummaryText`
- Other formatters produce various English text

**Category 5 — CLI help text** (CliApplication.cs):
- `"Usage:"`, `"Options:"`
- `"  --help                Show help for the application or a specific command."`
- `"  --json                Emit machine-readable JSON instead of text."`
- `"Unknown command '{command}'."`
- `"Usage: "`, `"  Aliases: "`

### Configuration Storage

`PluginUiConfiguration` currently has 3 bool properties (version 3):
```csharp
public int Version { get; set; } = 3;
public bool AutoStartHttpServerOnLoad { get; set; }
public bool EnableActionOperations { get; set; }
public bool EnableUnsafeOperations { get; set; }
```

Persisted via `PluginUiConfigurationStore` using `IDalamudPluginInterface.SavePluginConfig()`.

`PluginConfigWindow` receives config via DI constructor injection: `PluginUiConfigurationStore`.

---

## Available Localization Approaches

### Approach A: Custom JSON-based Localization Service (RECOMMENDED)

**How it works:**
- Define `IUiLocalization` interface with `GetString(key)` and `CurrentLanguage` / event
- Implement `JsonLocalization` that loads `lang/en.json` and `lang/zh.json` from assembly-embedded resources
- Register as singleton in DI
- Language change triggers an event; `PluginConfigWindow.RefreshModel` re-reads all localized strings

**Pros:**
- Zero new NuGet dependencies
- Full control over string format
- Works with DI infrastructure already in place
- JSON files are easy to edit for translators
- Can embed as `EmbeddedResource` or deploy alongside plugin DLL
- Event-driven refresh pattern matches existing `RefreshModel(force: true)` mechanism

**Cons:**
- Need to maintain JSON files
- Won't cover CLI binary help text without protocol changes

**File structure:**
```
src/DalamudMCP.Plugin/
├── Ui/
│   ├── Localization/
│   │   ├── IUiLocalization.cs      # Interface
│   │   └── JsonLocalization.cs     # Implementation
│   └── lang/
│       ├── en.json                 # English strings
│       └── zh.json                 # Chinese strings
```

**Pattern:**
```csharp
// Usage in PluginConfigWindow
ImGui.Text(localization["window.title"]);
ImGui.Button(localization["button.start_server"]);
```

### Approach B: .resx Resource Files

**How it works:** Standard .NET resource files (`Strings.resx` as default, `Strings.zh.resx` for Chinese). Accessed via `Strings.ResourceManager`. Language switching via `CultureInfo.CurrentUICulture`.

**Pros:** Standard .NET pattern, designer support in Visual Studio, compile-time type safety with generated accessors.

**Cons:**
- Requires satellite assemblies or resource fallback configuration for plugins
- Dynamic switching at runtime requires `CultureInfo` change which can have thread-wide side effects
- RESX format is harder to version-control and review than JSON
- Over-engineered for a 2-language switch

**Verdict:** Not recommended for this use case.

### Approach C: XIVConfigUI Library

**How it works:** Third-party NuGet library (`XIVConfigUI`) providing UI components + localization + config for Dalamud plugins.

**Pros:** Purpose-built for Dalamud + ImGui, includes pre-built UI components, actively maintained.

**Cons:** Adds external dependency (version 1.0.5, .NET 8), need to learn library API, includes features (SVG rendering) well beyond what Phase 9 needs, may conflict with project conventions (zero external dependencies beyond core NuGet packages).

**Verdict:** Over-engineered for this phase. Worth revisiting if the project adds more complex UI later.

### Approach D: Dalamud Loc.Localize

**How it works:** Use Dalamud's built-in `Loc.Localize("key", "fallbackEnglish")` static method. Loc is Dalamud-internal.

**Pros:** Zero code — already in Dalamud runtime.

**Cons:**
- `Loc.Localize` uses the FFXIV **game client** language (ClientLanguage enum: Japanese=0, English=1, German=2, French=3)
- No Chinese value in `ClientLanguage` enum — Chinese FFXIV is a separate client version
- Cannot be switched independently of game language
- No event for language changes — tied to game restart
- Would make the feature unusable for Chinese players on the global client
- The `Loc` class is Dalamud-internal; usage patterns suggest it's for Dalamud's own UI, not guaranteed as a public plugin API

**Verdict:** Not suitable — fails the core requirement of configurable, game-language-independent switching.

### Comparison Table

| Criteria | A: JSON Service | B: .resx | C: XIVConfigUI | D: Loc.Localize |
|----------|:---:|:---:|:---:|:---:|
| No new NuGet deps | YES | YES | NO | YES |
| Dynamic runtime switch | YES | Complex | YES | NO |
| Supports zh/en independently | YES | YES | YES | NO |
| Works with project DI | YES | YES | YES | N/A |
| Easy to add new strings | YES | Studio | Library API | N/A |
| CLI help text coverage | Partial | Partial | No | No |
| Learning curve | Low | Low | Medium | Low |

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| (none new) | — | — | Custom JSON localization service avoids external dependencies; project conventions prefer minimal NuGet surface |

### Supporting
| Tool | Purpose |
|------|---------|
| `System.Text.Json` | Already a project dependency (via ASP.NET/MCP); used to deserialize language JSON files |
| `EmbeddedResource` MSBuild item | Bundles `lang/*.json` files into plugin assembly |

### Installation
```xml
<!-- No new NuGet packages needed. System.Text.Json is already transitive. -->
```

---

## Architecture Patterns

### Recommended Project Structure (new files)
```
src/DalamudMCP.Plugin/
├── Ui/
│   ├── Localization/
│   │   ├── IUiLocalization.cs          # public interface
│   │   └── JsonLocalization.cs         # internal sealed class
│   ├── PluginConfigWindow.cs           # Modified to use localization
│   ├── PluginConfigWindowModel.cs      # Modified to use localization
│   └── (existing files)
├── Configuration/
│   ├── PluginUiConfiguration.cs        # +SelectedLanguage property
│   └── (existing files)
├── lang/
│   ├── en.json                        # English string table
│   └── zh.json                        # Chinese string table
├── PluginEntryPoint.cs                 # +DI registration for IUiLocalization
└── (existing files)
```

### Pattern 1: Interface-based Localization Service

**What:** A DI-registered singleton service that provides string lookup by key and emits a change event.

**When to use:** Any UI component that needs locale-aware text.

**Example:**
```csharp
// IUiLocalization.cs
namespace DalamudMCP.Plugin.Ui.Localization;

public interface IUiLocalization
{
    string this[string key] { get; }
    string GetString(string key);
    string CurrentLanguage { get; }
    void SetLanguage(string language);
    event Action? LanguageChanged;
}
```

```csharp
// JsonLocalization.cs
using System.Text.Json;

namespace DalamudMCP.Plugin.Ui.Localization;

internal sealed class JsonLocalization : IUiLocalization, IDisposable
{
    private Dictionary<string, string> en = new();
    private Dictionary<string, string> zh = new();
    private string currentLanguage = "zh";

    public string CurrentLanguage => currentLanguage;
    public event Action? LanguageChanged;

    public JsonLocalization()
    {
        en = LoadFromResource("DalamudMCP.Plugin.lang.en.json");
        zh = LoadFromResource("DalamudMCP.Plugin.lang.zh.json");
    }

    public string GetString(string key) =>
        currentLanguage switch
        {
            "zh" => zh.TryGetValue(key, out var v) ? v
                  : en.GetValueOrDefault(key, key),
            _    => en.GetValueOrDefault(key, key)
        };

    public string this[string key] => GetString(key);

    public void SetLanguage(string language)
    {
        if (currentLanguage == language) return;
        currentLanguage = language;
        LanguageChanged?.Invoke();
    }

    private static Dictionary<string, string> LoadFromResource(string resourceName)
    {
        var assembly = typeof(JsonLocalization).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource: {resourceName}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? new Dictionary<string, string>();
    }

    public void Dispose() { /* cleanup if needed */ }
}
```

**DI Registration in PluginServiceCollectionExtensions:**
```csharp
services.AddSingleton<IUiLocalization, JsonLocalization>();
```

**Consumption in PluginConfigWindow:**
```csharp
public sealed class PluginConfigWindow
{
    private readonly IUiLocalization localization;
    private bool languageSwitchPending;

    public PluginConfigWindow(
        /* ... existing params ... */
        IUiLocalization localization)
    {
        this.localization = localization;
        localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        languageSwitchPending = true;
        RefreshModel(force: true);
    }

    public void Draw()
    {
        if (languageSwitchPending)
        {
            // model will re-read all localized strings on next refresh
            languageSwitchPending = false;
        }
        // ... existing draw logic using localization["key"] ...
        ImGui.Text(localization["runtime.panel_title"]);
    }
}
```

### Pattern 2: Language Switch UI in Config Window Header

**What:** A combo box in the config window header allowing immediate language switching.

**When to use:** The primary user-facing touchpoint for Phase 9.

**Example:**
```csharp
private void DrawLanguageSelector()
{
    string current = localization.CurrentLanguage;
    string[] languages = ["zh", "en"];
    string[] labels = ["中文", "English"];
    int selectedIndex = Array.IndexOf(languages, current);
    if (selectedIndex < 0) selectedIndex = 0;

    ImGui.SetNextItemWidth(120f);
    if (ImGui.Combo("##lang", ref selectedIndex, labels, labels.Length))
    {
        configurationStore.Update(config =>
            config.SelectedLanguage = languages[selectedIndex]);
        localization.SetLanguage(languages[selectedIndex]);
        RefreshModel(force: true);
    }
}
```

### Pattern 3: Operation Description Localization

**What:** A method that localizes operation descriptions at display time, using the operation ID as the lookup key.

**When to use:** In the operations table column that shows summaries.

**Example:**
```csharp
// In PluginConfigOperationRow or the window draw method:
string localizedSummary = localization.GetString(
    $"op.{operation.OperationId}.summary")
    ?? operation.Summary  // fallback to attribute value (English)
    ?? operation.Description;
```

**JSON key convention:**
```json
{
  "op.player.context.summary": "获取玩家上下文。",
  "op.player.context.description": "获取当前玩家上下文。",
  "op.inventory.summary.summary": "获取库存摘要。",
  "op.inventory.summary.description": "获取当前库存摘要。"
}
```

### Anti-Patterns to Avoid

- **Static singleton Localization.Instance**: The project uses DI throughout; a static singleton breaks testability and the existing injection pattern. Use interface + DI.
- **CultureInfo.CurrentUICulture switching**: Changes are thread-wide and affect .NET framework components. The plugin draws on the game thread; changing the thread culture can have unpredictable side effects for Dalamud internals.
- **Mixing config language and game language**: The language selector is independent of `ClientLanguage`. Do not read `clientState.ClientLanguage` to determine UI language.
- **Replacing string literals with method calls in every ImGui call**: A helper extension method on `IUiLocalization` (`loc["key"]`) is cleaner than `localization.GetString("key")` everywhere.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Localization dictionary serialization | Custom parser/formatter | `System.Text.Json` (already in project) | JSON is well-understood, easy to edit, already a dependency |
| Config language persistence | Custom file I/O | `PluginUiConfigurationStore.SavePluginConfig()` | Existing Dalamud config API handles file location, serialization, and version migration |
| Language change notification | Polling | Event on `IUiLocalization` | Push model avoids per-frame string comparison; matches existing `RefreshModel(force: true)` |

---

## Common Pitfalls

### Pitfall 1: Missing Keys at Runtime
**What goes wrong:** A localization key is misspelled in code or missing from JSON. The UI shows the key name instead of the expected text (e.g., `"runtime.panel_title"` instead of `"运行时"`).
**Why it happens:** No compile-time checking of JSON keys against usage sites.
**How to avoid:** Implement `GetString` to log a warning (via `Serilog` or `ILogger`) when a key is missing, and fall back to key name or English. Add a test case that enumerates all expected keys from the English JSON and verifies they exist in the Chinese JSON.
**Warning signs:** UI showing raw key names like `"op.player.context.summary"`.

### Pitfall 2: Language Switch Doesn't Update All Text Immediately
**What goes wrong:** After switching languages, some text stays in the old language until the config window is reopened.
**Why it happens:** Some strings are computed once (e.g., in `PluginConfigOperationRow` constructor) and cached. `PluginConfigWindowModel` caches status texts.
**How to avoid:** Ensure all cached text properties are recomputed on language change. The `LanguageChanged` event should trigger `RefreshModel(force: true)` which calls `ApplyStatus` again. Row-level text (`CLI: {cmd}`, `MCP: {tool}`) in `PluginConfigOperationRow` constructor needs a refresh method too, or move those prefix strings to the draw-time localization call.
**Warning signs:** Status texts update but row texts don't after language switch.

### Pitfall 3: Test Assertions Hardcoded to Chinese
**What goes wrong:** `PluginConfigWindowModelTests` asserts on Chinese strings like `"动作操作: 已禁用"` — after Phase 9 replaces these with localization lookups, the tests break.
**Why it happens:** Phase 8 changed the model's string literals to Chinese, and the tests were not updated (currently still assert English strings, e.g. line 238: `"Action operations: disabled"`).
**How to avoid:** Refactor the model to take `IUiLocalization` as a constructor parameter. Tests can inject a fake localization that returns predictable strings. Test the behavior (enabled vs disabled), not the literal text.
**Warning signs:** Test failures after Phase 9 changes.

### Pitfall 4: Over-localizing Game Data
**What goes wrong:** Localization attempts translate game-native strings like job names, world names, item names.
**Why it happens:** The distinction between UI text and game data is unclear.
**How to avoid:** Only localize strings that are defined in the plugin's own source code (config window labels, status text, help text). Game data (character names, job names, item names) comes from FFXIV game data and should be displayed as-is.
**Warning signs:** Localization dictionaries containing `"白魔导士"` entries.

---

## Code Examples

### JSON Language File Structure

```json
// src/DalamudMCP.Plugin/lang/en.json
{
  "window.title": "DalamudMCP Settings",
  "header.subtitle": "Real-time bridge for FFXIV observation, actions, and MCP exposure.",
  "badge.pipe_online": "Pipe Online",
  "badge.pipe_offline": "Pipe Offline",
  "badge.http_online": "HTTP Online",
  "badge.http_stopped": "HTTP Stopped",
  "badge.exposed": "{0}/{1} exposed",
  "runtime.panel_title": "Runtime",
  "runtime.panel_subtitle": "Connection health, discovery, and exposure status.",
  "runtime.discovery": "Discovery",
  "runtime.pipe": "Named Pipe",
  "runtime.reader": "Reader",
  "runtime.action_tools": "Action Tools",
  "runtime.unsafe_tools": "Unsafe Tools",
  "runtime.operations": "Operations",
  "runtime.enable_actions": "Enable CLI/MCP Action Operations",
  "runtime.enable_actions_hint": "Observe tools remain online. Actions are off by default and must be explicitly exposed here before use.",
  "runtime.enable_unsafe": "Enable Unsafe Integration Tools (Developers Only)",
  "runtime.enable_unsafe_hint": "Unsafe tools can invoke arbitrary plugin IPC. Keep disabled unless debugging other plugins.",
  "quickstart.panel_title": "Command Console",
  "quickstart.panel_subtitle": "Copy the two most common entry point commands.",
  "quickstart.cli_card_title": "CLI Quick Check",
  "quickstart.cli_card_desc": "Read a live player snapshot from the active plugin instance.",
  "quickstart.cli_card_button": "Copy Player Context Command",
  "quickstart.mcp_card_title": "MCP Service",
  "quickstart.mcp_card_desc": "Start a local MCP bridge via the plugin-discovered pipe.",
  "quickstart.mcp_card_button": "Copy MCP Service Command",
  "server.panel_title": "HTTP Server",
  "server.panel_subtitle": "Stable MCP endpoint for clients that don't care about pipe names.",
  "server.endpoint": "Endpoint",
  "server.http_status": "HTTP Status",
  "server.auto_start": "Auto-start MCP HTTP Server on Plugin Load",
  "server.start_button": "Start MCP HTTP Server",
  "server.stop_button": "Stop MCP HTTP Server",
  "server.copy_endpoint": "Copy MCP Endpoint",
  "server.copy_command": "Copy MCP Server Command",
  "advanced.header": "Advanced Details",
  "advanced.pipe": "Pipe",
  "advanced.cli_command": "CLI Command",
  "advanced.mcp_service": "MCP Service",
  "advanced.http_command": "HTTP Command",
  "operations.panel_title": "Operations",
  "operations.panel_subtitle": "Filter exposed interfaces before handing the plugin to other clients.",
  "operations.catalog": "Catalog",
  "operations.search": "Search",
  "operations.show_blocked": "Show Blocked Only",
  "operations.show_reader": "Show Reader-Backed Only",
  "operations.table_header_operation": "Operation",
  "operations.table_header_access": "Access",
  "operations.table_header_status": "Status",
  "operations.table_header_summary": "Summary",
  "operations.empty": "No operations match current filters.",
  "operations.tag_action": "Action",
  "operations.tag_observe": "Observe",
  "operations.tag_unsafe": "Unsafe",
  "operations.tag_exposed": "Exposable",
  "status.server_running": "Server Status: Running",
  "status.server_stopped": "Server Status: Stopped",
  "status.http_running": "Status: Running",
  "status.http_stopped": "Status: Stopped",
  "status.actions_enabled": "Action Operations: Enabled",
  "status.actions_disabled": "Action Operations: Disabled",
  "status.unsafe_enabled": "Unsafe Operations: Enabled",
  "status.unsafe_disabled": "Unsafe Operations: Disabled",
  "status.reader_format": "Reader Status: {0}/{1} Ready",
  "status.exposure_unsafe_pending": "Exposure: Disabled, Waiting for Unsafe Operations to be Enabled",
  "status.exposure_action_pending": "Exposure: Disabled, Waiting for Action Operations to be Enabled",
  "status.reader_ready": "Reader: Ready",
  "status.reader_not_ready": "Reader: Not Ready",
  "status.reader_detail": "Reader: {0} ({1})",
  "label.cli_prefix": "CLI: ",
  "label.mcp_prefix": "MCP: ",
  "label.pipe_name": "Active Pipe (Advanced): ",
  "label.endpoint": "Endpoint: ",
  "label.last_error": "Last Error: "
}
```

### Embedding JSON Files in csproj

```xml
<!-- In DalamudMCP.Plugin.csproj -->
<ItemGroup>
  <EmbeddedResource Include="lang\en.json" />
  <EmbeddedResource Include="lang\zh.json" />
</ItemGroup>
```

### Language-Aware Operation Row Refresh

The `PluginConfigOperationRow` constructor currently sets `CliCommandText` and `McpToolText` with hardcoded `"CLI: "` / `"MCP: "` prefixes. These need to become lazy or refreshable:

```csharp
// An approach: compute at draw time
internal sealed class PluginConfigOperationRow
{
    // ... existing fields ...
    
    // Remove CliCommandText and McpToolText as computed properties
    // Instead, add a method that takes the localization service:
    public string GetCliCommandText(IUiLocalization loc) =>
        string.IsNullOrWhiteSpace(CliCommand)
            ? null
            : loc["label.cli_prefix"] + CliCommand;

    public string GetMcpToolText(IUiLocalization loc) =>
        string.IsNullOrWhiteSpace(McpToolName)
            ? null
            : loc["label.mcp_prefix"] + McpToolName;
}
```

But this changes the window's draw loop. A simpler approach: store the localization keys and format at draw time in the window:

```csharp
// In PluginConfigWindow.DrawOperations():
ImGui.TableSetColumnIndex(1);
string cliPrefix = localization["label.cli_prefix"];
if (!string.IsNullOrWhiteSpace(operation.CliCommand))
    ImGui.TextUnformatted(cliPrefix + operation.CliCommand);
string mcpPrefix = localization["label.mcp_prefix"];
if (!string.IsNullOrWhiteSpace(operation.McpToolName))
    ImGui.TextUnformatted(mcpPrefix + operation.McpToolName);
```

### PluginConfigWindowModel Localization Integration

The model currently stores pre-compiled Chinese text strings. Refactored to use localization:

```csharp
internal sealed class PluginConfigWindowModel
{
    private readonly IUiLocalization loc;

    private PluginConfigWindowModel(
        IUiLocalization loc,
        string pipeName,
        string cliCommand,
        string mcpCommand,
        PluginConfigOperationRow[] operations,
        IPluginReaderStatus?[] readerStatuses)
    {
        this.loc = loc;
        PipeName = pipeName;
        PipeNameText = loc["label.pipe_name"] + pipeName;
        // ...
    }

    public string ProtocolServerStatusText =>
        ProtocolServerRunning
            ? loc["status.server_running"]
            : loc["status.server_stopped"];
    
    public string ActionOperationsStatusText =>
        ActionOperationsEnabled
            ? loc["status.actions_enabled"]
            : loc["status.actions_disabled"];
    
    // ... etc for all status texts
}
```

The key insight: make status text properties **non-caching** (computed on each get via the localization service) rather than storing a cached string. This ensures language switching takes effect immediately on the next ImGui draw. The existing `ApplyStatus` method only needs to toggle boolean state, not recompute text.

For the `ReaderStatusText` which is more complex:
```csharp
public string? ReaderStatusText
{
    get
    {
        if (readerCount <= 0) return null;
        return string.Format(
            loc["status.reader_format"],
            readyReaderCount, readerCount);
    }
}
```

### Language Selection Persistence

```csharp
// PluginUiConfiguration.cs
public sealed class PluginUiConfiguration : IPluginConfiguration
{
    public int Version { get; set; } = 4;  // Increment for migration
    // ... existing properties ...
    public string SelectedLanguage { get; set; } = "zh";  // Default to Chinese
}
```

```csharp
// In PluginEntryPoint constructor, after loading config:
if (!string.IsNullOrWhiteSpace(configurationStore.Current.SelectedLanguage))
{
    localization.SetLanguage(configurationStore.Current.SelectedLanguage);
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| English hardcoded strings | Chinese hardcoded strings (Phase 8) | Phase 8 (not gated) | Both are static; Phase 9 makes them dynamic |
| — | JSON-based localization service | Phase 9 | Enables runtime language switching |
| — | Language combo box in config header | Phase 9 | Primary user interaction for language switch |

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `System.Text.Json` is already available as a transitive dependency in the Plugin project | Standard Stack | True — confirmed by checking csproj which depends on ModelContextProtocol (uses System.Text.Json). Low risk. |
| A2 | Phase 8 has already replaced all strings in PluginConfigWindow.cs and PluginConfigWindowModel.cs with Chinese | Current UI Architecture | Files read during research confirm Chinese strings are present. If Phase 8 is reverted before Phase 9 starts, the baseline changes. |
| A3 | The `jsonLocalization.LoadFromResource` method using `Assembly.GetManifestResourceStream` will work with Dalamud's assembly loading | Pattern 1 | Dalamud loads plugins via assembly load context; embedded resources should resolve normally. Verified by common Dalamud plugin patterns. |
| A4 | Tests in PluginConfigWindowModelTests currently assert Chinese strings | Common Pitfalls | Tests were written for English strings. Since Phase 9 hasn't been gated, tests may still assert English (as seen in the test file). Need verification during planning. |

---

## Open Questions (RESOLVED)

1. **CLI binary help text localization — how far do we go? — RESOLVED**
   - What we know: `CliApplication.GetUsage()` in `Framework.Cli` generates help text for the standalone CLI binary. It uses operation attribute descriptions and built-in strings like `"Usage:"`. This code runs in the CLI process, not the plugin.
   - What's unclear: Whether criterion 5 ("CLI 帮助文本随语言切换更新") requires the standalone CLI binary to output localized text, or just the CLI help text displayed in the plugin config window. The former requires adding language preference to the named pipe protocol and modifying `CliApplication` to accept a localization source — a non-trivial cross-project change.
   - Recommendation: Interpret criterion 5 as "the CLI command text displayed in the plugin config window updates with language" (this is automatic since it's rendered via the model). Defer standalone CLI binary localization to a follow-up. Add a note in the plan that this is deferred.

2. **Should operation `Description`/`Summary` attributes be localized? — RESOLVED**
   - What we know: These are compile-time constants in `[Operation]` attributes on each operation class. Phase 8 left them in English.
   - What's unclear: Whether the config window operations table should show localized descriptions. The current window renders `operation.Summary` in the table column.
   - Recommendation: Localize operation summaries in the config window table using a key convention (`op.{operationId}.summary`). Do not modify the attribute values (they serve as the English fallback and are used by the CLI binary).

3. **Configuration version migration from 3 to 4? — RESOLVED**
   - What we know: `PluginUiConfiguration.Version` is currently 3. Adding `SelectedLanguage` requires version increment.
   - What's unclear: Whether the existing configuration store handles version migration (e.g., `Upgrade()` method).
   - Recommendation: Check `PluginUiConfigurationStore` — currently it does NOT have an `Upgrade` method. Add one that sets defaults for new properties if `Version < 4`.

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK 10.0 | Build/compile | Confirmed by Phase 1 | 10.0.201+ | — |
| Dalamud runtime | Plugin testing | Via DALAMUD_HOME | API 15 | — |
| System.Text.Json | Localization deserialization | Transitive (ASP.NET/MCP) | Included in .NET 10.0 | — |

**Missing dependencies with no fallback:** None — the approach uses zero new NuGet packages.

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit v3 (3.2.2) with Microsoft Testing Platform |
| Config file | `xunit.runner.json` (solution root) |
| Quick run command | `dotnet test tests/DalamudMCP.Plugin.Tests/ --filter "PluginConfigWindowModel"` |
| Full suite command | `./build/test.ps1` |

### Phase Requirements to Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| L10N-01 | Language switch persists across restart | unit | `dotnet test --filter "PluginUiConfigurationStore"` | Needs creation |
| L10N-02 | Language change forces model refresh | unit | `dotnet test --filter "PluginConfigWindowModel"` | Modify existing |
| L10N-03 | All localized keys exist in both en and zh | unit | New test in Plugin.Tests | Needs creation |
| L10N-04 | Fallback to English when Chinese key missing | unit | New test | Needs creation |

### Sampling Rate
- **Per task commit:** `dotnet test tests/DalamudMCP.Plugin.Tests/ --filter "PluginConfigWindowModel" --no-restore`
- **Per wave merge:** `./build/test.ps1 -NoBuild`
- **Phase gate:** Full suite green before `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `tests/DalamudMCP.Plugin.Tests/PluginConfigWindowModelTests.cs` — needs refactoring: tests currently assert English string literals; must be updated to use fake localization service
- [ ] `tests/DalamudMCP.Plugin.Tests/JsonLocalizationTests.cs` — new file for localization dictionary loading and fallback tests
- [ ] `tests/DalamudMCP.Plugin.Tests/PluginUiConfigurationStoreTests.cs` — existing file; add test for `SelectedLanguage` persistence roundtrip

---

## Security Domain

> Security enforcement is enabled (config.json: `workflow.nyquist_validation: true`).

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V5 Input Validation | yes | Localization JSON files are embedded resources with trusted content; loaded via `JsonSerializer.Deserialize` which has no code execution risk. Language selection is constrained to "en"/"zh" via combo box (not free text). |
| V8 Data Protection | no | Language preference is not sensitive data. |
| V12 File & Resources | yes | Embedded resources loaded from own assembly — no external file loading. |

### Known Threat Patterns for Localization

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Resource injection via embedded resource | Tampering | Resources are embedded at build time; runtime only reads from own assembly. No user-writable paths involved. |
| Language config validation | Spoofing | `SetLanguage` accepts only known values ("en", "zh"); reject unknown. |

---

## Sources

### Primary (HIGH confidence)
- **Source code read directly**: `src/DalamudMCP.Plugin/Ui/PluginConfigWindow.cs`, `PluginConfigWindowModel.cs`, `Configuration/PluginUiConfiguration.cs`, `PluginEntryPoint.cs` — verified all UI string locations
- **Source code read directly**: `src/DalamudMCP.Framework.Cli/CliApplication.cs` — verified CLI help text structure
- **Source code read directly**: `src/DalamudMCP.Plugin/Operations/*.cs` — verified operation attribute patterns and result formatters
- **Source code read directly**: `tests/DalamudMCP.Plugin.Tests/PluginConfigWindowModelTests.cs` — verified test string expectations

### Secondary (MEDIUM confidence)
- [WebSearch: Dalamud `ClientLanguage` enum](https://dalamud.dev/api/api15/Dalamud.Game/Enums/ClientLanguage/) — confirmed 4 values (Japanese, English, German, French); no Chinese/Korean
- [WebSearch: Dalamud `Localization.LocalizationChangedDelegate`](https://dalamud.dev/api/Dalamud/Delegates/Localization.LocalizationChangedDelegate/) — confirmed delegate signature `void (string langCode)`
- [WebSearch: XIVConfigUI](https://www.nuget.org/packages/XIVConfigUI/) — identified as alternative localization library; rejected for Phase 9 scope
- [WebSearch: Dalamud `Loc.Localize` pattern](https://github.com/goatcorp/Dalamud/pull/1662/files) — confirmed usage pattern but not suitable for Chinese

### Tertiary (LOW confidence)
- [WebSearch: Multiple Dalamud plugin localization patterns] — general community patterns for JSON dictionary approach; no single authoritative source

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — zero new dependencies confirmed by reading project files
- Architecture: HIGH — current UI structure confirmed by direct source reading
- Pitfalls: HIGH — derived from reading existing test patterns and model caching behavior

**Research date:** 2026-05-01
**Valid until:** 2026-06-01 (stable project)
