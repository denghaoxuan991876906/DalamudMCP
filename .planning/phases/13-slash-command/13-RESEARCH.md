# Phase 13: 斜杠命令调度 — 技术研究

**研究日期：** 2026-05-01
**阶段目标：** AI 客户端能够通过 MCP 工具发送 Dalamud 注册的斜杠命令到游戏内
**需求：** SLASH-01

---

## 1. 核心 API：`ICommandManager.ProcessCommand()`

Phase 12 的 `PluginReloadOperation` 已成功使用 `ICommandManager.ProcessCommand()` 触发 `/xlreload`。Phase 13 的 `SlashCommandOperation` 遵循相同模式——区别在于输入是任意用户提供的命令字符串而非硬编码命令。

```csharp
// Dalamud API (已在 Phase 12 中验证可用)
namespace Dalamud.Plugin.Services;

public interface ICommandManager
{
    bool ProcessCommand(string content);  // 派发斜杠命令到游戏内，返回 bool 表示是否被某个处理器接收
}
```

**关键特性（来自 Phase 12 经验）：**
- `ProcessCommand()` 在 **Framework 线程上调用时行为正常**，非 Framework 线程调用可能导致未定义行为
- 返回 `bool`——`true` 表示命令被某个处理器接收，`false` 表示无处理器匹配（包括游戏原生命令）
- 仅处理 **Dalamud 注册的命令**（`/xlreload`、`/ping`、各插件的自定义命令等），游戏原生命令（`/echo`、`/tell` 等）无法通过此 API 执行

## 2. 线程安全要求

### 2.1 Framework 线程约束

Phase 12 已建立成熟的 Framework 线程编排模式（`PluginReloadOperation.cs:108-118`）：

```csharp
if (framework.IsInFrameworkUpdateThread)
{
    commandManager.ProcessCommand(command);
}
else
{
    await framework.RunOnFrameworkThread(() =>
    {
        commandManager.ProcessCommand(command);
    }).ConfigureAwait(false);
}
```

### 2.2 Fire-and-Forget 与线程占用

- `ProcessCommand()` 是同步调用，在 Framework 线程上执行
- Fire-and-forget 模式下无需等待命令执行结果（命令可能修改游戏状态，但无返回值）
- 不阻塞游戏主线程（Framework 线程而已，非主渲染线程）

## 3. 输入验证策略

CONTEXT.md 的两个关键决策：

| 决策 | 内容 | 影响 |
|------|------|------|
| **D-01** | 宽松验证：命令必须以 `/` 开头，长度上限 256 字符 | 实现只需 2 个条件检查 |
| **D-02** | 不滤除特殊字符——让 `ICommandManager` 自然拒绝无效命令 | 不进行控制字符/换行/null 字节过滤 |

**实现要点：**
- 第一个字符必须是 `/`（`string.StartsWith("/", StringComparison.Ordinal)`）
- 长度 ≤ 256 字符（`request.Command.Length <= 256`）
- 不 Trim 命令字符串（保留用户的空格意图）
- 不解析命令结构（不区分命令名和参数）
- 无预检测（不维护 Dalamud 注册命令列表——脆弱且易过时）

## 4. 响应设计

### 4.1 Fire-and-Forget 语义下的响应模型

CONTEXT.md 指定：「参考 Phase 12 的 PluginReloadResult 模式」，但状态码更简单——fire-and-forget 模式下命令执行结果不可知：

| 状态码 | 含义 | 触发条件 |
|--------|------|---------|
| `command_sent` | 命令已派发 | `ProcessCommand()` 返回 true |
| `validation_failed` | 输入校验失败 | 命令不以 `/` 开头或长度超过 256 字符 |

**不需要** `command_failed` 状态码——`ProcessCommand()` 返回 false 时（无处理器匹配）仍然记为 `command_sent`，让 AI 自行判断。D-02 的「让 ICommandManager 自然拒绝」策略也适用。

### 4.2 响应结构

采用 Phase 12 的 Record 模式（`PluginReloadResult`），使用 MemoryPack 序列化：

```csharp
[MemoryPackable]
public sealed partial record SlashCommandResult(
    string Command,         // 原始命令字符串
    bool Success,           // 是否成功派发
    string Status,          // status code
    string SummaryText      // 人类可读摘要（CLI 格式化输出）
);
```

**与 Phase 12 的区别：** 无 `ErrorMessage` 字段——validation_failed 已包含在 Status/SummaryText 中，无需分离错误字段。无 `PluginName`——通用命令调度不需要插件上下文。

### 4.3 MCP 工具设计

工具名：`slash_command`

根据 CONTEXT.md，工具需要：
- 参数：`command`（string，必须以 `/` 开头）
- 描述中包含「仅支持 Dalamud 注册的命令，游戏原生命令（`/echo`、`/tell`、`/say` 等）无法通过此工具执行，将返回 `command_sent` 但命令不会生效」
- 归类为 `unsafe` 操作（与 Phase 12 的 `reload_plugin` 同级）

## 5. 与 Phase 11 基础设施的集成

### 5.1 DI 依赖

Phase 11 提供的服务：
- `IFramework` — Framework 线程编排（已注册 DI 单例）
- `IDalamudPluginInterface` — **不需要**（SlashCommand 不操作插件列表）
- `IPluginIpcGateway` — **不需要**（斜杠命令不通过 IPC）

**新操作的构造函数：**
```csharp
public SlashCommandOperation(IFramework framework, ICommandManager commandManager)
```

比 PluginReloadOperation 少一个参数（不需要 `IDalamudPluginInterface`）——斜杠命令操作不涉及插件发现。

### 5.2 DI 注册

`AddGeneratedPluginOperations()` 源生成器自动发现 `[Operation]` 属性标记的类，无需手动修改 `PluginServiceCollectionExtensions.cs`。

但是：`BuildDalamudServiceProvider()` 没有注入 `ICommandManager` 到 DI 容器。Phase 12 的 PluginReloadOperation 通过构造注入使用 `ICommandManager`，所以该服务的注入已在其他地方处理，或通过 PluginCompositionRoot 处理。

**验证：** `PluginReloadOperation` 编译成功且测试通过 → `ICommandManager` 在 DI 中可用（Phase 12-01-SUMMARY 确认）。新操作无需额外 DI 注册。

### 5.3 暴露策略

在 `PluginOperationExposurePolicy.cs` 的 `UnsafeOperationIds` HashSet 中添加 `"command.slash"`（操作 ID）：

```csharp
private static readonly HashSet<string> UnsafeOperationIds =
[
    "unsafe.invoke.plugin-ipc",
    "plugin.reload",
    "command.slash"  // 新增
];
```

## 6. 测试策略

### 6.1 单元测试

复用 Phase 12 建立的测试基础设施：
- **测试框架：** xUnit v3 + NSubstitute 5.3.0
- **测试项目：** `tests/DalamudMCP.Plugin.Operations.Tests/`
- **Mock 工厂：** 复用 Phase 12 的 `CreateFramework()` 和 `CreateCommandManager()` 辅助方法

### 6.2 测试用例

**输入验证测试：**

| 测试场景 | 预期行为 |
|----------|---------|
| 有效命令 `/echo hello` | `Success=true`, `Status=command_sent` |
| 不以 `/` 开头 `hello` | `Success=false`, `Status=validation_failed`, SummaryText 包含错误原因 |
| 空字符串 | `Success=false`, `Status=validation_failed` |
| 恰好 256 字符 | `Success=true`, `Status=command_sent` |
| 超过 256 字符 | `Success=false`, `Status=validation_failed` |
| 仅 `/` | `Success=true`, `Status=command_sent`（D-02 不预检有效性） |
| 包含换行符 | `Success=true`, `Status=command_sent`（D-02 不预检——ICommandManager 自行处理） |

**线程测试：**

| 测试场景 | 预期行为 |
|----------|---------|
| 已在 Framework 线程 | 直接调用 ProcessCommand，不使用 RunOnFrameworkThread |
| 不在 Framework 线程 | 通过 RunOnFrameworkThread 编排 |

**构造函数验证：**

| 测试场景 | 预期行为 |
|----------|---------|
| null IFramework | `ArgumentNullException("framework")` |
| null ICommandManager | `ArgumentNullException("commandManager")` |
| null Request | `ArgumentNullException` (在 ExecuteAsync 中) |

### 6.3 测试桩

不需要 Fake 类——操作仅依赖 `IFramework` 和 `ICommandManager`，两者均可通过 NSubstitute 直接 mock（Phase 12 已建立此模式）。无需创建新的测试桩文件。

## 7. 文件结构规划

基于 Phase 12 模式：

```
src/DalamudMCP.Plugin/Operations/SlashCommandOperation.cs  — 操作实现 + Request + Result
```

修改文件：
```
src/DalamudMCP.Plugin/Hosting/PluginOperationExposurePolicy.cs  — 添加 command.slash 到 UnsafeOperationIds
```

测试文件：
```
tests/DalamudMCP.Plugin.Operations.Tests/SlashCommandOperationTests.cs  — 单元测试
```

## 8. 与 Phase 12 的差异总结

| 方面 | Phase 12 (PluginReload) | Phase 13 (SlashCommand) |
|------|------------------------|------------------------|
| 核心 API | `ICommandManager.ProcessCommand("/xlreload")` | `ICommandManager.ProcessCommand(anyCommand)` |
| DI 参数 | pluginInterface + framework + commandManager | framework + commandManager (少一个) |
| 输入验证 | ArgumentException.ThrowIfNullOrWhiteSpace | D-01/D-02：`/` 前缀 + 256 字符上限 |
| 状态码 | 4 个（重载专有语义） | 2 个（fire-and-forget 通用语义） |
| 插件查找 | 需要（InstalledPlugins） | 不需要 |
| 自身保护 | self_reload_blocked | 不适用（无自身操作概念） |
| Result 字段 | PluginName + Success + Status + ErrorMessage + SummaryText | Command + Success + Status + SummaryText |
| 测试桩 | 需要 mock IExposedPlugin | 不需要测试桩（两个接口均可 NSubstitute） |

## 9. 风险与缓解

| 风险 | 可能性 | 影响 | 缓解 |
|------|--------|------|------|
| 游戏原生命令被用户请求但无效果 | 高 | 低 | MCP 工具描述中明确说明限制；返回 `command_sent` 表示已处理（AI 需自行理解不可用性） |
| D-02 特殊字符被 ICommandManager 异常处理 | 低 | 低 | try-catch ProcessCommand 调用；异常时返回 command_sent + 错误信息在 SummaryText |
| 长命令（接近 256 字符）性能影响 | 低 | 极低 | ProcessCommand 内部处理，不在本操作层面优化 |
| 命令在非 Framework 线程执行导致 Dalamud 崩溃 | 低 | 高 | 强制执行 Framework.IsInFrameworkUpdateThread 检查 + RunOnFrameworkThread（Phase 12 已验证） |

## 10. 总结

**核心发现：**
1. Phase 12 已完全验证 `ICommandManager.ProcessCommand()` + Framework 线程编排模式——Phase 13 是该模式的直接应用
2. 输入验证极简（D-01/D-02），无需预检测 Dalamud 注册命令列表
3. Fire-and-forget 模式使响应模型比 Phase 12 更简单（2 状态码 vs 4 状态码）
4. 不需要 IDalamudPluginInterface 或 IPluginIpcGateway——比 PluginReloadOperation 少一个 DI 依赖
5. 测试无需新测试桩——两个接口均可直接 NSubstitute mock

**就绪状态：** ✅ 可进入规划阶段。Phase 12 已验证所有必要条件，Phase 13 是 Phase 12 模式的直接扩展。

---

*研究完成：2026-05-01*
*下一阶段：Phase 13 规划（PLAN.md 创建）*
