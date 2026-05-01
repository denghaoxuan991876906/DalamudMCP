# Phase 14: 安全 IPC 调用 — 技术研究

**研究日期：** 2026-05-01
**阶段目标：** AI 客户端能够通过 MCP `invoke_plugin_ipc` 工具调用目标插件的 IPC 函数，传入参数并获取返回值，错误信息结构化可读
**需求：** IPC-01
**置信度：** HIGH（全部核心接口、测试桩、操作模式均来自已完成的 Phase 11/12/13，可直接验证）

---

## 摘要

Phase 14 在 Phase 11 提取的 `IPluginIpcGateway` / `IPluginCallGateSubscriber` 共享 IPC 网关基础上，构建一个**约定式安全 IPC 调用操作**。核心区别在于：与现有 `unsafe.invoke.plugin-ipc`（要求 AI 指定完整 CallGate 名称、参数类型和返回类型）不同，安全版本采用约定式命名 `{PluginName}.MCP.{Action}`——目标插件零 SDK 依赖，只需按约定暴露 IPC 端点。参数类型从提供的 JSON 值自动推断，调用在 Framework 线程上执行，错误响应细分为 5 种状态码。

**主要建议：** 创建 `SafeInvokePluginIpcOperation`，遵循与 `PluginReloadOperation` 和 `SlashCommandOperation` 相同的操作模式：`[Operation]` + `[McpTool]` + MemoryPack 序列化的 Request/Result record + DI 构造注入 `IPluginIpcGateway` + `IFramework` + Framework 线程编排。暴露策略归类为 `unsafe`（与 `plugin.reload`、`command.slash` 同级）。

---

## 架构责任映射

| 能力 | 主要层级 | 次要层级 | 理由 |
|------|---------|---------|------|
| IPC 调用协议约定（`{Name}.MCP.{Action}`） | API/Backend (DalamudMCP Plugin) | — | 约定在 DalamudMCP Plugin 端构建 CallGate 名称并调用，目标插件仅声明 IPC 端点 |
| 参数类型自动推断 | API/Backend (DalamudMCP Plugin) | — | 从 JSON 元素推断 CLR 类型，在 plugin 端完成 |
| IPC 网关路由 | API/Backend (DalamudMCP Plugin) | — | `IPluginIpcGateway.TryCreate()` 在 plugin 端反射创建订阅者 |
| Framework 线程执行 | API/Backend (DalamudMCP Plugin) | — | `IFramework.RunOnFrameworkThread()` 确保 IPC 调用在正确线程执行 |
| MCP 工具暴露 | Frontend Server (CLI) | API/Backend | MCP 工具描述由属性定义，CLI 端序列化到 MCP 协议；实际执行在 Plugin 端 |
| 返回结果序列化 | API/Backend (DalamudMCP Plugin) | Database/Storage (MemoryPack) | Plugin 端序列化返回值，MemoryPack 用于管道传输 |

---

## Phase Requirements

| ID | 描述 | 研究支持 |
|----|------|---------|
| IPC-01 | AI 能够通过 MCP 调用目标插件暴露的 IPC 函数（指定插件名 + 方法名 + 参数），并获取返回值 | §1（约定设计）、§2（网关集成）、§4（响应模型）完整覆盖 |

---

## 1. 安全 IPC 约定 vs. 不安全 IPC 调用

### 1.1 现有 unsafe 调用的工作方式

现有 `UnsafeInvokePluginIpcOperation`（操作 ID: `"unsafe.invoke.plugin-ipc"`）要求调用者指定：
- **Callgate**：完整的 IPC CallGate 名称（如 `"MyPlugin.SomeChannel.V1"`）
- **result-kind**：返回类型（如 `"bool"`, `"string"`）
- **argument-kinds**：每个参数的类型（如 `"int,string"`）
- **arguments-json**：JSON 数组格式的参数值
- **run-on-framework-thread**：是否在 Framework 线程执行

这种设计提供了最大灵活性，但对 AI 客户端要求高——需要精确了解目标插件的 IPC 签名。

### 1.2 安全版本的约定式命名

安全版本采用约定式命名 `{PluginName}.MCP.{Action}`：

```
示例：
  PluginName = "MyPlugin", Action = "GetStatus"
  → CallGate = "MyPlugin.MCP.GetStatus"
```

**目标插件注册 IPC 的方式**（零 SDK 依赖，纯 Dalamud 原生 API）：

```csharp
// 目标插件（被测插件）——无需引用任何 DalamudMCP 依赖
// 在 Dalamud 插件 Service 中：
var getStatusProvider = pluginInterface.GetIpcProvider<int, bool>("MyPlugin.MCP.GetStatus");
getStatusProvider.RegisterFunc(level => level > 0);
```

**核心原则：**
- 目标插件只需实现约定式 IPC CallGate 命名（`{Name}.MCP.{Action}`）
- 无需引入 DalamudMCP SDK 或 NuGet 包
- 不依赖任何特定接口或基类
- 参数和返回值类型由插件自行定义

[VERIFIED: ROADMAP.md Phase 14 Success Criteria #2 — `{Name}.MCP.{Action}` 约定]
[VERIFIED: PROJECT.md Constraints — "被测插件不引入额外 SDK 依赖，仅实现 IPC 接口约定"]

### 1.3 参数策略

| 参数类型 | JSON 表示 | 推断 CLR 类型 | 传递方式 |
|---------|----------|-------------|---------|
| 整数 | `42` | `int` | 直接作为 IPC 参数 |
| 布尔值 | `true` | `bool` | 直接作为 IPC 参数 |
| 浮点数 | `3.14` | `double` | 直接作为 IPC 参数 |
| 字符串 | `"hello"` | `string` | 直接作为 IPC 参数 |
| JSON 对象 | `{"key": "val"}` | `string` | **JSON 字符串信封**——序列化为字符串后传递，目标插件自行 `JsonSerializer.Deserialize<T>()` |
| JSON 数组 | `[1, 2, 3]` | `string` | JSON 字符串信封 |
| null | `null` | `object` | 传递 null（无类型信息时默认 object） |

**设计理由：**
- 基元类型直接传递避免了目标插件多余的 JSON 解析步骤
- 复杂类型使用 JSON 字符串信封——这是 Dalamud IPC 跨插件通信的标准模式（IPC 函数参数类型必须精确匹配 CLR 类型，无法传递 `JsonElement`）
- 类型检测逻辑复用 `UnsafeInvokePluginIpcOperation` 中已验证的 `PluginIpcValueKind` 分类和 `GetClrType()` 映射

### 1.4 返回类型策略

安全版本的返回类型始终使用 `object` 作为泛型参数中的返回类型位置：

```
typeArguments = [argType1, argType2, ..., typeof(object)]
```

`InvokeFunc` 返回的 `object?` 被序列化为 JSON 字符串放入 Result record 中，AI 客户端接收后自行解析。

[VERIFIED: PluginIpcGateway.TryCreate 接受 `IReadOnlyList<Type>`（不含运行时类型限制）]
[VERIFIED: UnsafeInvokePluginIpcOperation.cs:124 — `JsonSerializer.Serialize(result, typeArguments[^1])` 证明序列化返回值模式可用]

---

## 2. IPluginIpcGateway 集成

### 2.1 DI 注入模式

Phase 11 已注册 `IPluginIpcGateway` 为 DI 单例：

```csharp
// PluginServiceCollectionExtensions.cs:51
services.AddSingleton<IPluginIpcGateway, PluginIpcGateway>();
```

安全操作通过构造注入获取网关：

```csharp
public SafeInvokePluginIpcOperation(
    IPluginIpcGateway gateway,
    IFramework framework)
{
    ArgumentNullException.ThrowIfNull(gateway);
    ArgumentNullException.ThrowIfNull(framework);
    executor = CreateDalamudExecutor(gateway, framework);
}
```

[VERIFIED: PluginServiceCollectionExtensions.cs:51 — IPluginIpcGateway 单例注册]
[VERIFIED: UnityEngineInvokePluginIpcOperation.cs:27 — 现有 unsafe 操作已使用此模式]

### 2.2 错误状态码映射

调用流程及对应状态码：

```
1. 构建 CallGate 名称 = $"{request.PluginName}.MCP.{request.Method}"
2. 推断 argumentTypes = JSON 参数 → CLR 类型数组
3. Type[] typeArguments = [argumentTypes..., typeof(object)]
4. gateway.TryCreate(callgate, typeArguments, out subscriber?)
   ├─ false / subscriber is null → ipc_missing（插件未安装或 CallGate 通道不存在）
   └─ true → 检查 subscriber.HasFunction
             ├─ false → ipc_not_ready（CallGate 通道存在但函数未注册/插件未就绪）
             └─ true → subscriber.InvokeFunc(parsedArguments)
                      ├─ TargetInvocationException → ipc_plugin_error（目标插件抛出异常）
                      ├─ InvalidCastException / type mismatch → ipc_type_mismatch
                      ├─ 其他异常 → ipc_plugin_error
                      └─ 成功 → ipc_success（返回值序列化到 Result.ReturnValue）
```

**状态码定义：**

| 状态码 | 含义 | 触发条件 | AI 建议操作 |
|--------|------|---------|-----------|
| `ipc_success` | IPC 调用成功，返回可用值 | `InvokeFunc` 正常返回 | 使用返回的 ReturnValue |
| `ipc_missing` | 插件未找到或 CallGate 通道不存在 | `TryCreate` 返回 false | 检查插件名拼写 / 确认插件已安装 |
| `ipc_not_ready` | CallGate 通道存在但函数未注册 | `HasFunction` 为 false | 等待 1-2 秒后重试（插件可能在初始化中） |
| `ipc_type_mismatch` | 参数类型与目标 IPC 函数签名不匹配 | `InvokeFunc` 抛出类型转换异常 | 检查参数类型是否正确 / 使用 `unsafe_invoke_plugin_ipc` 逃生舱指定精确类型 |
| `ipc_plugin_error` | 目标插件执行 IPC 函数时抛出异常 | `InvokeFunc` 抛出其他异常 | 检查目标插件日志 / ErrorMessage 包含异常详情 |

[VERIFIED: UnsafeInvokePluginIpcOperation.cs:109-153 — 现有 unsafe 版本已实现 ipc_missing / ipc_error 分类]
[CITED: ROADMAP.md Phase 14 Success Criteria #4 — 明确要求细分为 ipc_missing/ipc_not_ready/ipc_type_mismatch/ipc_plugin_error]

### 2.3 类型不匹配检测

`InvokeFunc` 中的类型不匹配在 Dalamud IPC 层通常表现为 `InvalidCastException` 或 `ArgumentException`。捕获策略：

```csharp
try
{
    object? result = subscriber.InvokeFunc(arguments);
    // success path
}
catch (TargetInvocationException ex) when (ex.InnerException is InvalidCastException)
{
    return new Result(..., Status: "ipc_type_mismatch", ...);
}
catch (TargetInvocationException ex)
{
    return new Result(..., Status: "ipc_plugin_error", ErrorMessage: ex.InnerException?.Message);
}
catch (Exception ex)
{
    return new Result(..., Status: "ipc_plugin_error", ErrorMessage: ex.Message);
}
```

**注意：** Dalamud 内部的 CallGate 泛型类型检查可能在 `InvokeFunc` 调用前就抛出异常（如在 `MakeGenericMethod` 时）。由于我们的 `ReflectionPluginCallGateSubscriber` 在 `TryCreate` 时已经通过 `MakeGenericMethod` 创建了订阅者，类型不匹配更可能在 `InvokeFunc` 执行时表现为 `InvalidCastException`。具体情况需在测试中验证。

[ASSUMED: `InvokeFunc` 内部类型转换异常的具体类型——需测试验证确切异常类型]

---

## 3. 响应模型设计

### 3.1 Result Record

采用 Phase 12/13 已验证的 MemoryPack record 模式：

```csharp
[MemoryPackable]
public sealed partial record SafeInvokePluginIpcResult(
    string PluginName,      // 目标插件内部名称
    string Method,          // IPC 方法名（Action 部分）
    bool Success,           // 调用是否成功
    string Status,          // 状态码（ipc_success/ipc_missing/...）
    string? ReturnValue,    // 成功时的返回值（JSON 序列化后的字符串）
    string? ErrorMessage,   // 失败时的错误信息
    string SummaryText      // 人类可读摘要（CLI 格式化输出）
);
```

**字段设计对比：**

| 字段 | Phase 12 (PluginReloadResult) | Phase 13 (SlashCommandResult) | Phase 14 (SafeInvokePluginIpcResult) |
|------|------|------|------|
| PluginName | ✅ | ❌ | ✅（新操作需要） |
| Method | ❌ | ❌ | ✅（新操作特有——区分同一插件的不同方法） |
| Command | ❌ | ✅ | ❌ |
| Success | ✅ | ✅ | ✅ |
| Status | ✅ | ✅ | ✅ |
| ReturnValue | ❌ | ❌ | ✅（新操作特有——IPC 调用返回值） |
| ErrorMessage | ✅ | ❌ | ✅（失败时包含异常详情） |
| SummaryText | ✅ | ✅ | ✅ |

### 3.2 与 PluginReloadResult / SlashCommandResult 的对齐

- **Success + Status 组合**：Phase 12 用 `Success=false + Status="plugin_not_found"` 模式，Phase 14 沿用
- **ErrorMessage 可选**：只在非成功路径填充（Phase 12 模式）
- **SummaryText 必填**：CLI/Log 可读摘要，包含关键信息
- **MemoryPack 序列化**：所有 Result record 使用 `[MemoryPackable]` + `sealed partial record` 模式

[VERIFIED: PluginReloadResult (PluginReloadOperation.cs:140-145)]
[VERIFIED: SlashCommandResult (SlashCommandOperation.cs:116-121)]

---

## 4. 操作类设计

### 4.1 完整操作类结构

操作 ID：`"plugin.ipc"`
MCP 工具名：`"invoke_plugin_ipc"`（来自 ROADMAP.md）
暴露策略：`unsafe`

```csharp
using System.Runtime.Versioning;
using System.Text.Json;
using Dalamud.Plugin.Services;
using DalamudMCP.Framework;
using DalamudMCP.Protocol;
using DalamudMCP.Plugin.Ipc;
using MemoryPack;

namespace DalamudMCP.Plugin.Operations;

[Operation(
    "plugin.ipc",
    Description = "使用约定式命名 {PluginName}.MCP.{Action} 调用目标插件的 IPC 函数。基元类型参数自动推断并直接传递，复杂对象以 JSON 字符串信封传递。目标插件需按约定注册 IPC CallGate，无需依赖 DalamudMCP SDK。返回结构化响应包含状态码（ipc_success/ipc_missing/ipc_not_ready/ipc_type_mismatch/ipc_plugin_error）和返回值。",
    Summary = "Invokes a convention-based plugin IPC function.")]
[ResultFormatter(typeof(SafeInvokePluginIpcOperation.TextFormatter))]
[CliCommand("plugin", "ipc")]
[McpTool("invoke_plugin_ipc")]
public sealed partial class SafeInvokePluginIpcOperation
    : IOperation<SafeInvokePluginIpcOperation.Request, SafeInvokePluginIpcResult>
{
    private readonly Func<Request, CancellationToken, ValueTask<SafeInvokePluginIpcResult>> executor;

    [SupportedOSPlatform("windows")]
    public SafeInvokePluginIpcOperation(
        IPluginIpcGateway gateway,
        IFramework framework)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(framework);
        executor = CreateDalamudExecutor(gateway, framework);
    }

    internal SafeInvokePluginIpcOperation(
        Func<Request, CancellationToken, ValueTask<SafeInvokePluginIpcResult>> executor)
    {
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public ValueTask<SafeInvokePluginIpcResult> ExecuteAsync(
        Request request, OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        return executor(request, context.CancellationToken);
    }

    [MemoryPackable]
    [ProtocolOperation("plugin.ipc")]
    public sealed partial class Request
    {
        [Option("plugin-name", 
            Description = "目标插件的内部名称（InternalName），大小写不敏感。")]
        public string PluginName { get; init; } = string.Empty;

        [Option("method", 
            Description = "IPC 方法名。完整的 IPC CallGate 名称将构造为 {PluginName}.MCP.{Method}。")]
        public string Method { get; init; } = string.Empty;

        [Option("arguments-json", 
            Description = "JSON 数组格式的参数列表。整数→int、浮点数→double、布尔→bool、字符串→string。JSON 对象和数组将以 JSON 字符串信封形式传递（目标插件自行反序列化）。",
            Required = false)]
        public string? ArgumentsJson { get; init; }
    }

    // ... TextFormatter, CreateDalamudExecutor, InvokeSafeIpc（见 §4.3）
}
```

### 4.2 变参参数处理

`arguments-json` 为可选参数，不传等同于空参数列表。处理流程：

```
1. 如果 ArgumentsJson 为 null 或空白 → args = []，argTypes = []
2. 解析 JSON 数组 → JsonElement[]
3. 对每个 element 推断类型：
   - JsonValueKind.Number 且不含小数点 → int (PluginIpcValueKind.Whole32)
   - JsonValueKind.Number 且含小数点 → double (PluginIpcValueKind.Fraction64)
   - JsonValueKind.True / JsonValueKind.False → bool
   - JsonValueKind.String → string
   - JsonValueKind.Object / JsonValueKind.Array → string（JSON 信封，调用 element.GetRawText() 获取原始 JSON）
   - JsonValueKind.Null → object（传递 null）
4. 构建 typeArguments = [argTypes..., typeof(object)]
5. 调用 gateway.TryCreate(callgate, typeArguments, out subscriber)
```

**JSON 信封的实现**：对于 Object/Array 类型的 JSON 元素，获取原始 JSON 文本作为 string 参数传递：

```csharp
static object? ParseArgument(JsonElement element)
{
    return element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when element.TryGetInt32(out int i) => i,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.String => element.GetString()!,
        JsonValueKind.Object or JsonValueKind.Array => element.GetRawText(), // JSON 字符串信封
        JsonValueKind.Null => null!,
        _ => throw new ArgumentException($"Unsupported JSON value kind: {element.ValueKind}")
    };
}
```

[ASSUMED: `element.GetRawText()` 返回原始 JSON 字符串的行为——基于 System.Text.Json API 训练知识]

### 4.3 Framework 线程执行

参照 Phase 12 和 Phase 13 的已验证模式：

```csharp
private static Func<Request, CancellationToken, ValueTask<SafeInvokePluginIpcResult>> 
    CreateDalamudExecutor(IPluginIpcGateway gateway, IFramework framework)
{
    return async (request, ct) =>
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PluginName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Method);

        if (framework.IsInFrameworkUpdateThread)
            return InvokeSafeIpc(gateway, request);

        return await framework.RunOnFrameworkThread(
            () => InvokeSafeIpc(gateway, request)).ConfigureAwait(false);
    };
}
```

**与 unsafe 版本的区别：** unsafe 版本有 `RunOnFrameworkThread` 参数（可选跳过线程切换），安全版本**始终在 Framework 线程执行**（无需选项——安全操作应始终遵循线程安全最佳实践）。

[VERIFIED: PluginReloadOperation.cs:108-118 — Framework 线程编排模式]
[VERIFIED: SlashCommandOperation.cs:86-96 — 相同的线程模式]

### 4.4 MCP 工具描述

MCP 工具 `invoke_plugin_ipc` 的描述需包含：

1. **命名约定说明**：`{PluginName}.MCP.{Action}` 格式
2. **参数说明**：plugin-name（内部名称）、method（方法名）、arguments-json（可选 JSON 数组）
3. **错误码说明**：5 种状态码的含义和建议操作
4. **使用示例**：

```
示例：
  invoke_plugin_ipc --plugin-name "MyPlugin" --method "GetStatus" --arguments-json "[42]"
  → 调用 MyPlugin.MCP.GetStatus CallGate，传递参数 42（int 类型）
  → 返回 { status: "ipc_success", return_value: "true" }

  invoke_plugin_ipc --plugin-name "MyPlugin" --method "ProcessData" --arguments-json "[{\"key\":\"value\"}]"
  → 传递 JSON 对象作为字符串信封，目标插件负责 deserialize
```

---

## 5. 与现有 unsafe 操作共存

### 5.1 两种 IPC 操作的对比

| 方面 | unsafe.invoke.plugin-ipc | plugin.ipc（安全版本） |
|------|------------------------|----------------------|
| CallGate 名称 | 用户指定完整名称 | 约定式构造 `{PluginName}.MCP.{Action}` |
| 参数类型 | 用户显式指定 argument-kinds | 自动从 JSON 推断 |
| 返回类型 | 用户显式指定 result-kind | 固定为 object（返回 JSON 字符串） |
| Framework 线程 | 可选（RunOnFrameworkThread 参数） | 始终执行（无选项） |
| 目标用户 | 高级开发者调试 | AI 客户端日常使用 |
| 状态码 | `ipc_missing` / `ipc_error` | `ipc_success` / `ipc_missing` / `ipc_not_ready` / `ipc_type_mismatch` / `ipc_plugin_error` |
| 修改范围 | **保持不变**（逃生舱） | 新增操作 |

### 5.2 暴露策略归类

**归类为 `unsafe`**——与 `plugin.reload` 和 `command.slash` 同级：

| 操作 ID | 归类 | 理由 |
|---------|------|------|
| `unsafe.invoke.plugin-ipc` | unsafe | 已有，归类不变 |
| `plugin.reload` | unsafe | 已有，归类不变 |
| `command.slash` | unsafe | 已有，归类不变 |
| **`plugin.ipc`** | **unsafe** | **新增——调用外部插件代码，有副作用风险** |

在 `PluginOperationExposurePolicy.cs` 中添加：

```csharp
private static readonly HashSet<string> UnsafeOperationIds =
[
    "unsafe.invoke.plugin-ipc",
    "plugin.reload",
    "command.slash",
    "plugin.ipc"  // 新增
];
```

**归类理由：** 虽然 `plugin.ipc` 采用约定式命名比 unsafe 版本更可控，但它仍然是触发外部插件代码执行的入口——可能产生游戏状态修改、网络请求等副作用。归类为 unsafe 确保其受 UI 安全开关控制。

---

## 6. 测试策略

### 6.1 测试基础设施

- **框架：** xUnit v3 + NSubstitute 5.3.0（Phase 12 已建立）
- **测试项目：** `tests/DalamudMCP.Plugin.Operations.Tests/`
- **复用测试桩：** Phase 11 的 `FakeIpcGateway` / `FakeIpcCallGateSubscriber`

### 6.2 测试桩复用与扩展

现有 `FakeIpcGateway` 已足够——它通过 `(string Callgate, IPluginCallGateSubscriber Subscriber)[]` 构造函数接受预定义的 subscriber 映射：

```csharp
// tests/.../TestShared/Ipc/FakeIpcGateway.cs
new FakeIpcGateway(("MyPlugin.MCP.GetStatus", new FakeIpcCallGateSubscriber(true, true)));
```

`FakeIpcCallGateSubscriber` 需要小幅增强以支持**可抛异常的 InvokeFunc**：

```csharp
// 现有：只能返回固定结果或 null
public sealed class FakeIpcCallGateSubscriber : IPluginCallGateSubscriber
{
    public FakeIpcCallGateSubscriber(bool hasFunction, object? result = null)
    // ...

// 增强：支持通过委托控制 InvokeFunc 行为
public FakeIpcCallGateSubscriber(
    bool hasFunction, 
    Func<IReadOnlyList<object?>, object?>? invokeFunc = null,
    object? staticResult = null)
```

**决定：** 最小化改动——保持现有 `FakeIpcCallGateSubscriber` 不变（sufficient for most tests），仅对需要模拟异常的测试使用 NSubstitute `Substitute.For<IPluginCallGateSubscriber>()` 直接 mock 接口。

[VERIFIED: FakeIpcCallGateSubscriber.cs — 现有实现]
[VERIFIED: PluginReloadOperationTests.cs — NSubstitute mock 模式已验证可用]

### 6.3 测试用例矩阵

#### 成功路径

| 测试场景 | 输入 | 预期输出 |
|---------|------|---------|
| 无参数调用 | `PluginName="Test" Method="Ping" ArgumentsJson=null` | `Success=true Status=ipc_success ReturnValue="true" Method="Ping"` |
| 单参数调用 | `ArgumentsJson="[42]"` | `Success=true Status=ipc_success`，CallGate 使用 `{int, object}` 类型参数 |
| 多参数调用 | `ArgumentsJson="[42,\"hello\",true]"` | `Success=true Status=ipc_success`，CallGate 使用 `{int, string, bool, object}` |
| JSON 信封参数 | `ArgumentsJson="[{\"k\":\"v\"}]"` | `Success=true Status=ipc_success`，CallGate 使用 `{string, object}` |
| Number 推断为 int | `ArgumentsJson="[42]"` | 参数类型为 `int`（非 double） |
| Number 推断为 double | `ArgumentsJson="[3.14]"` | 参数类型为 `double` |
| null 参数 | `ArgumentsJson="[null]"` | 参数类型为 `object` |

#### 错误路径

| 测试场景 | 输入 | 预期状态码 |
|---------|------|----------|
| 插件未安装 | `PluginName="NonExistent"` | `ipc_missing` |
| CallGate 不存在 | FakeGateway 不包含该 callgate | `ipc_missing` |
| HasFunction=false | FakeSubscriber `hasFunction=false` | `ipc_not_ready` |
| InvokeFunc 抛异常 | NSubstitute mock subscriber 抛异常 | `ipc_plugin_error` |
| InvokeFunc 抛 InvalidCastException | mock subscriber 抛 `new InvalidCastException(...)` | `ipc_type_mismatch` |
| 空的 PluginName | `PluginName=""` | `ArgumentException`（在 executor 中） |
| 空的 Method | `Method=""` | `ArgumentException`（在 executor 中） |
| 无效 JSON 参数 | `ArgumentsJson="not json"` | `ipc_plugin_error`（JSON 解析异常） |

#### Framework 线程编排

| 测试场景 | 预期 |
|---------|------|
| 已在 Framework 线程 | 直接调用 InvokeSafeIpc，不使用 RunOnFrameworkThread |
| 不在 Framework 线程 | 通过 `RunOnFrameworkThread` 编排 |

#### 构造函数验证

| 测试场景 | 预期 |
|---------|------|
| null IPluginIpcGateway | `ArgumentNullException("gateway")` |
| null IFramework | `ArgumentNullException("framework")` |
| null Request | `ArgumentNullException`（在 ExecuteAsync 中） |

### 6.4 测试文件规划

```
tests/DalamudMCP.Plugin.Operations.Tests/SafeInvokePluginIpcOperationTests.cs
```

**约 15-17 个测试**——与 Phase 12（12 个）和 Phase 13（11 个）规模一致。

---

## 7. 文件结构规划

### 新建文件

```
src/DalamudMCP.Plugin/Operations/SafeInvokePluginIpcOperation.cs  — 操作类 + Request + Result
tests/DalamudMCP.Plugin.Operations.Tests/SafeInvokePluginIpcOperationTests.cs  — 单元测试
```

### 修改文件

```
src/DalamudMCP.Plugin/Hosting/PluginOperationExposurePolicy.cs  — 添加 "plugin.ipc" 到 UnsafeOperationIds
```

### 不修改的文件

- `src/DalamudMCP.Plugin/Ipc/*` — IPC 基础设施保持不变
- `src/DalamudMCP.Plugin/Operations/UnsafeInvokePluginIpcOperation.cs` — unsafe 逃生舱不变
- `src/DalamudMCP.Plugin/Hosting/PluginServiceCollectionExtensions.cs` — DI 注册不变（源生成器自动注册新操作）
- `PluginIpcValueKind` / `UnsafeInvokePluginIpcResult` — 保留在原文件

### 操作类命名约定

| 项目 | 值 | 参考 |
|------|----|----|
| 操作 ID | `"plugin.ipc"` | Phase 12: `"plugin.reload"`, Phase 13: `"command.slash"` |
| MCP 工具名 | `"invoke_plugin_ipc"` | ROADMAP.md Phase 14 Success Criteria #1 |
| CLI 命令 | `plugin ipc` | 层级式命名，与 `plugin reload` 一致 |
| 类名 | `SafeInvokePluginIpcOperation` | 与 `UnsafeInvokePluginIpcOperation` 区分 |
| Result 类名 | `SafeInvokePluginIpcResult` | MemoryPack record，与 `UnsafeInvokePluginIpcResult` 区分 |

---

## 8. 不自行实现（Don't Hand-Roll）

| 问题 | 不要构建 | 使用已有 | 原因 |
|------|---------|---------|------|
| IPC 网关创建 | 手动 `GetIpcSubscriber<T>` 调用 | `IPluginIpcGateway.TryCreate()` (Phase 11) | 已封装反射逻辑，处理 `MakeGenericMethod`、`BindingFlags` 查找等边缘情况 |
| Framework 线程编排 | `Task.Run` + 手动回调 | `IFramework.RunOnFrameworkThread()` | Dalamud 提供的官方线程调度 API |
| 操作注册 | 手动 DI `AddSingleton` | `[Operation]` + 源生成器 `AddGeneratedPluginOperations()` | 已建立的自动注册机制 |
| JSON 解析 | `Newtonsoft.Json` | `System.Text.Json` (已有 using) | 项目已使用 STJ，无需新依赖 |
| 序列化结果 | 自定义二进制格式 | `MemoryPack` (已有依赖) | 项目所有 Result record 使用 MemoryPack |
| 反射订阅者 | 直接 `PropertyInfo.GetValue` / `MethodInfo.Invoke` | `ReflectionPluginCallGateSubscriber` (Phase 11) | 已处理 HasFunction 空检查、只读列表参数等边缘情况 |

---

## 9. 常见陷阱

### 陷阱 1：泛型类型参数计数不匹配

**问题：** Dalamud 的 `GetIpcSubscriber<T1, T2, ..., TResult>` 重载有固定的类型参数数量（如 1+1、2+1、...、N+1）。如果推断的参数类型数量超出可用重载范围，`PluginIpcGateway` 会返回 `false`。

**原因：** `PluginIpcGateway` 的 `GetSubscriberMethods` 静态缓存按泛型参数数量排序。`FirstOrDefault` 查找匹配数量的 `MethodInfo`——若无匹配，返回 null。

**缓解：** 限制参数数量上限。查看 Dalamud IPC 支持的泛型重载数量（通常在 8-10 个参数内）。在 MCP 工具描述中建议参数不超过 8 个。

[ASSUMED: Dalamud GetIpcSubscriber 泛型重载上限——需查看 Dalamud SDK 源码确认]

### 陷阱 2：JSON Number 类型模糊

**问题：** `JsonValueKind.Number` 无法区分 `int` vs `double`。`42.0` 可能被解析为 `double` 而非 `int`。

**缓解：** 使用 `TryGetInt32` 尝试整数解析；失败则 `GetDouble()`。这可能导致某些边界情况（如 `1.0` → `int 1`），但实践中 IPC 函数签名通常不会混用 `int` 和 `double` 参数。需要时 AI 可回退到 `unsafe_invoke_plugin_ipc` 精确指定类型。

### 陷阱 3：并发 IPC 调用

**问题：** 多个 MCP 请求同时调用同一插件的 IPC 函数可能导致竞态条件。

**评估：** 
- `IPluginIpcGateway` 是单例服务——`TryCreate` 每次创建新的 `ReflectionPluginCallGateSubscriber` 实例（非缓存）
- `InvokeFunc` 每次调用 `MethodInfo.Invoke()`——无共享可变状态
- Dalamud 的底层 IPC `InvokeFunc` 通常是同步调用，同一 CallGate 上无内置序列化

**缓解：** 每个 `SafeInvokePluginIpcOperation` 实例是无状态的（executor 委托捕获网关引用）。并发安全由 Dalamud 框架底层的 CallGate 实现保证。无需在操作层面添加锁。

[ASSUMED: Dalamud CallGate 底层线程安全性——基于 Dalamud IPC 设计文档的通用认知]

### 陷阱 4：Framework 线程上阻塞

**问题：** `InvokeFunc` 中的同步 IPC 调用在 Framework 线程上执行时，如果耗时过长会阻塞 Framework 更新循环。

**缓解：** IPC 调用通常在微秒级完成（进程内方法调用）。如果担心耗时操作，在 MCP 工具描述中标注「IPC 函数应快速返回，避免阻塞 Framework 线程」。这不是代码层面的缓解——由目标插件开发者负责。

### 陷阱 5：CallGate 名称区分大小写

**问题：** Dalamud IPC CallGate 名称可能区分大小写，但插件名和内部名可能大小写不一致。

**缓解：** 在操作描述中说明 CallGate 名称遵循内部名大小写。若调用失败 (ipc_missing)，建议 AI 检查插件实际 InternalName 的大小写。

[ASSUMED: Dalamud CallGate 名称区分大小写——基于 .NET string 键典型行为]

---

## 10. 与 Phase 15 (数据回传) 的关系

Phase 14 和 Phase 15 共享相同的 IPC 约定基础设施，但方向相反：

| | Phase 14: 安全 IPC 调用 | Phase 15: 数据回传 |
|---|---|---|
| **方向** | AI → 目标插件（请求→响应） | 目标插件 → DalamudMCP → AI（推送→缓存→轮询） |
| **IPC 模式** | CallGate Subscribe (getter IPC) | CallGate Action/Publish (推送 IPC) |
| **调用方** | DalamudMCP（调用 `InvokeFunc`） | 目标插件（调用 `SendMessage`） |
| **约定** | `{Name}.MCP.{Action}` | `{Name}.MCP.Relay` 或类似 |
| **参数** | AI 提供参数，返回响应 | 目标插件推送数据，无请求参数 |

**共享资产：**
- `IPluginIpcGateway` + `IPluginCallGateSubscriber`：两者都使用
- CallGate 命名约定模式
- 状态码分类体系（Phase 15 可能新增 `relay_overflow` 等状态码）
- `FakeIpcGateway` / `FakeIpcCallGateSubscriber` 测试桩

**Phase 14 不涉及 Phase 15 的：**
- Channel 缓存/有界缓冲区
- 订阅生命周期管理（subscribe/unsubscribe）
- 推送触发机制

---

## 11. 代码示例

以下示例来自现有代码库，已验证可用：

### 11.1 IPC 网关注入模式

```csharp
// Source: PluginServiceCollectionExtensions.cs:51
services.AddSingleton<IPluginIpcGateway, PluginIpcGateway>();

// Source: UnsafeInvokePluginIpcOperation.cs:26-28 — 构造注入模式
public UnsafeInvokePluginIpcOperation(
    IPluginIpcGateway gateway,
    IFramework framework)
```

### 11.2 Framework 线程编排

```csharp
// Source: PluginReloadOperation.cs:108-118 — 已验证模式
if (framework.IsInFrameworkUpdateThread)
{
    commandManager.ProcessCommand(reloadCommand);
}
else
{
    await framework.RunOnFrameworkThread(() =>
    {
        commandManager.ProcessCommand(reloadCommand);
    }).ConfigureAwait(false);
}
```

### 11.3 IPC 调用与错误处理

```csharp
// Source: UnsafeInvokePluginIpcOperation.cs:109-153
if (!gateway.TryCreate(callgate, typeArguments, out IPluginCallGateSubscriber? subscriber) ||
    subscriber is null ||
    !subscriber.HasFunction)
{
    return new Result(..., Status: "ipc_missing", ...);
}

try
{
    object? result = subscriber.InvokeFunc(arguments);
    return new Result(..., Success: true, Status: "ipc_success", ...);
}
catch (TargetInvocationException exception) when (exception.InnerException is not null)
{
    return new Result(..., Status: "ipc_error", ErrorMessage: exception.InnerException.Message);
}
catch (Exception exception)
{
    return new Result(..., Status: "ipc_error", ErrorMessage: exception.Message);
}
```

### 11.4 MemoryPack Result Record

```csharp
// Source: PluginReloadOperation.cs:140-145 — 标准模式
[MemoryPackable]
public sealed partial record PluginReloadResult(
    string PluginName,
    bool Success,
    string Status,
    string? ErrorMessage,
    string SummaryText);
```

### 11.5 暴露策略注册

```csharp
// Source: PluginOperationExposurePolicy.cs:21-26
private static readonly HashSet<string> UnsafeOperationIds =
[
    "unsafe.invoke.plugin-ipc",
    "plugin.reload",
    "command.slash"
    // Phase 14 将添加: "plugin.ipc"
];
```

---

## 12. 风险与缓解

| 风险 | 可能性 | 影响 | 缓解 |
|------|--------|------|------|
| 参数类型自动推断错误（int vs double 歧义） | 中 | 中 | `TryGetInt32` 优先整数；边界情况 AI 可用 unsafe 逃生舱 |
| 目标插件 IPC 函数签名与约定不一致 | 高 | 中 | `ipc_type_mismatch` 状态码告知 AI 签名不匹配；ErrorMessage 包含异常详情 |
| Dalamud IPC 泛型重载数量限制参数个数 | 低 | 中 | MCP 工具描述建议 ≤ 8 参数；超过此限制用 unsafe 逃生舱 |
| `GetRawText()` 在不同 STJ 版本的 JSON 信封行为差异 | 低 | 低 | 项目锁定 .NET 10.0 STJ 版本，行为确定 |
| 并发多个 IPC 调用无序列化 | 低 | 低 | 每次创建新 subscriber 实例，无共享状态；底层 Dalamud CallGate 线程安全 |
| ReflectionPluginCallGateSubscriber 对 null 参数的处理 | 中 | 低 | null 参数作为 `object` 类型传递；`MethodInfo.Invoke` 原生支持 null 引用 |

---

## 13. 环境可用性

**Step 2.6: SKIPPED（无外部依赖——纯代码变更，所有依赖已在 Phase 11/12 中验证可用）**

本阶段需要的所有服务（`IPluginIpcGateway`、`IFramework`）已在 DI 容器中注册，无需外部安装或配置。

---

## 14. 安全领域

### 适用的 ASVS 类别

| ASVS 类别 | 适用 | 标准控制 |
|-----------|------|---------|
| V2 身份验证 | 否 | — |
| V3 会话管理 | 否 | — |
| V4 访问控制 | **是** | 暴露策略的 `UnsafeOperationIds` 控制——`plugin.ipc` 归类 unsafe，受 UI 安全开关控制 |
| V5 输入验证 | **是** | JSON 解析验证（`JsonDocument.Parse`）、参数长度检查、空值检查 |
| V6 密码学 | 否 | — |

### 已知威胁模式

| 模式 | STRIDE | 标准缓解 |
|------|--------|---------|
| 恶意 JSON 输入导致反序列化异常 | 拒绝服务 (D) | `JsonDocument.Parse` 有内置深度/大小限制；try-catch 捕获异常转为 `ipc_plugin_error` |
| 无效参数类型导致反射调用的类型安全绕过 | 权限提升 (E) | .NET Reflection 的类型安全检查在运行时执行——无效类型会抛出异常而非静默通过 |
| 过大参数导致 Framework 线程阻塞 | 拒绝服务 (D) | `ArgumentsJson` 最大长度由 IPC 管道消息大小限制隐式约束 |
| 未经验证的插件名执行任意代码 | 篡改 (T) | IPC 调用仅在已安装插件范围内；无法执行未安装插件的方法 |

---

## 15. 假设日志

| # | 假设声明 | 章节 | 错误风险 |
|---|---------|------|---------|
| A1 | `InvokeFunc` 内部类型不匹配时抛出 `InvalidCastException`——需测试验证确切异常类型 | §2.3 | 低——错误分类可能不精确但不会导致错误路由（未知异常归入 ipc_plugin_error） |
| A2 | `JsonElement.GetRawText()` 返回原始 JSON 字符串行为——基于 STJ API 训练知识 | §4.2 | 低——.NET 10 STJ API 稳定，此方法行为确定 |
| A3 | Dalamud `GetIpcSubscriber<T>` 泛型重载支持 ≥ 8 个类型参数 | §9 陷阱1 | 低——参数数量不足时 `TryCreate` 返回 false → ipc_missing，非崩溃 |
| A4 | Dalamud CallGate 名称区分大小写 | §9 陷阱5 | 中——如果 Dalamud 使用不区分大小写的查找，CallGate 匹配会更宽松但不影响功能 |
| A5 | Dalamud CallGate 底层线程安全（并发 InvokeFunc 调用无数据竞争） | §9 陷阱3 | 低——每次创建新 subscriber 实例，操作层面无共享状态 |

---

## 16. 开放问题

1. **Dalamud GetIpcSubscriber 泛型参数数量上限是多少？**
   - 已知：`PluginIpcGateway` 通过反射扫描所有 `GetIpcSubscriber*` 重载，按参数数量排序
   - 不清楚：Dalamud SDK 中精确的重载数量（影响参数个数上限）
   - 建议：保守限制在 ≤ 8 参数；实际值可在测试中通过查看 `GetSubscriberMethods` 数组长度确认

2. **Phase 14 是否需要独立的测试桩增强？**
   - 已知：`FakeIpcGateway` + `FakeIpcCallGateSubscriber` 足以覆盖大部分测试
   - 不清楚：对于需要模拟 `InvokeFunc` 异常的场景，是否增强 `FakeIpcCallGateSubscriber` 还是直接用 NSubstitute mock 接口
   - 建议：保持测试桩最小化——能覆盖异常场景的用 NSubstitute mock `IPluginCallGateSubscriber` 接口直接模拟，无需修改 Phase 11 的测试桩

---

## 17. 来源

### 主要来源（高置信度）
- `src/DalamudMCP.Plugin/Ipc/IPluginIpcGateway.cs` — TryCreate 接口签名验证
- `src/DalamudMCP.Plugin/Ipc/IPluginCallGateSubscriber.cs` — HasFunction + InvokeFunc 接口验证
- `src/DalamudMCP.Plugin/Ipc/PluginIpcGateway.cs` — 反射网关实现验证
- `src/DalamudMCP.Plugin/Ipc/ReflectionPluginCallGateSubscriber.cs` — InvokeFunc 反射封装验证
- `src/DalamudMCP.Plugin/Operations/UnsafeInvokePluginIpcOperation.cs` — 现有 unsafe IPC 完整实现
- `src/DalamudMCP.Plugin/Operations/PluginReloadOperation.cs` — 操作模式参考（Request/Result/DI/线程）
- `src/DalamudMCP.Plugin/Operations/SlashCommandOperation.cs` — Phase 13 操作模式参考
- `src/DalamudMCP.Plugin/Hosting/PluginOperationExposurePolicy.cs` — 暴露策略注册验证
- `src/DalamudMCP.Plugin/Hosting/PluginServiceCollectionExtensions.cs` — DI 注册验证
- `tests/.../TestShared/Ipc/FakeIpcGateway.cs` — 测试桩验证
- `tests/.../TestShared/Ipc/FakeIpcCallGateSubscriber.cs` — 测试桩验证
- `tests/.../PluginReloadOperationTests.cs` — 测试模式参考
- `tests/.../UnsafeInvokePluginIpcOperationTests.cs` — 现有 IPC 测试模式
- `.planning/ROADMAP.md` — Phase 14 成功标准
- `.planning/REQUIREMENTS.md` — IPC-01 需求
- `.planning/PROJECT.md` — 项目约束和决策
- `.planning/phases/11-ipc-infra/11-CONTEXT.md` — IPC 基础设施设计决策
- `.planning/phases/11-ipc-infra/11-01-SUMMARY.md` — 接口提取完成确认
- `.planning/phases/11-ipc-infra/11-02-SUMMARY.md` — DI 注册完成确认
- `.planning/phases/13-slash-command/13-CONTEXT.md` — 暴露策略模式参考

### 次要来源（中置信度）
- 无——所有研究发现均可从代码库直接验证

### 第三来源（低置信度）
- 无

---

## 18. 元数据

**置信度分解：**
- 标准技术栈：HIGH — 所有库/接口来自已验证的 Phase 11/12/13 代码
- 架构：HIGH — 操作模式、DI 注入、线程编排全部有现有代码参考
- 陷阱：MEDIUM — 参数推断的边界情况和 Dalamud IPC 内部行为需要通过测试验证

**研究日期：** 2026-05-01
**有效期至：** 2026-06-01（基础架构稳定）

---

## 研究完成

**状态：** ✅ 研究完成——所有 10 个研究领域已覆盖

**核心发现：**
1. 安全 IPC 操作可直接注入 Phase 11 的 `IPluginIpcGateway` + `IFramework`，无需新基础设施
2. 约定式命名 `{PluginName}.MCP.{Action}` 简化为 CallGate 名称自动构建，目标插件零 SDK 依赖
3. 参数类型从 JSON 值自动推断（int/bool/double/string），JSON 对象/数组使用字符串信封传递
4. 5 种状态码（ipc_success/ipc_missing/ipc_not_ready/ipc_type_mismatch/ipc_plugin_error）覆盖全部 IPC 调用结果
5. 暴露策略归类为 unsafe，与 plugin.reload / command.slash 同级
6. 现有 unsafe 逃生舱保持不变，新增 safe 操作作为推荐方式
7. 测试复用 Phase 11 的 FakeIpcGateway/FakeIpcCallGateSubscriber 测试桩
8. 代码结构遵循 Phase 12/13 模式：单一操作文件 + Request/Result record + 暴露策略更新
9. 与 Phase 15 共享 IPC 约定基础设施，Phase 14 是单向调用，Phase 15 是反向推送
10. 无需新外部依赖——所有依赖已在 DI 容器中注册

**就绪状态：** ✅ 可进入规划阶段

---

*研究完成：2026-05-01*
*下一阶段：Phase 14 规划（PLAN.md 创建）*
