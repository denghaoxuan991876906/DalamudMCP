# Phase 12: 插件重载操作 — 技术研究

**研究日期：** 2026-05-01
**阶段目标：** AI 客户端能够通过 MCP 工具触发指定插件的卸载→重载，获取结构化状态响应
**需求：** RELOAD-01

---

## 1. Dalamud 插件重载 API 分析

### 1.1 核心 API：`IExposedPlugin.Reload()`

Dalamud 通过 `IDalamudPluginInterface.InstalledPlugins` 暴露已安装插件列表。每个插件以 `IExposedPlugin` 表示：

```csharp
// Dalamud API (来自 Dalamud.dll)
namespace Dalamud.Plugin;

public interface IExposedPlugin
{
    string InternalName { get; }   // 插件内部名称（如 "DalamudMCP"）
    string Name { get; }           // 显示名称
    // ... 其他属性
    void Reload();                 // 触发插件 unload → reload
}

public interface IDalamudPluginInterface
{
    IEnumerable<IExposedPlugin> InstalledPlugins { get; }
    // ... 其他成员
}
```

**关键特性：**
- `Reload()` 为同步方法，触发插件的 `Dispose()` → 重新加载 → 重新初始化
- `InternalName` 是唯一标识符，由插件程序集名称派生
- `InstalledPlugins` 列表由 Dalamud 框架维护，无需手动刷新

### 1.2 插件发现机制

通过 `InternalName` 匹配目标插件：

```csharp
IExposedPlugin? target = pluginInterface.InstalledPlugins
    .FirstOrDefault(p => string.Equals(p.InternalName, requestedName, StringComparison.OrdinalIgnoreCase));
```

**匹配策略：** 不区分大小写（OrdinalIgnoreCase），因为 Dalamud 的 InternalName 在不同平台可能大小写不一致。

**边界情况：**
- 插件未安装 → 返回错误状态 `plugin_not_found`
- 尝试重载自身（DalamudMCP）→ 需特殊处理或阻止
- 目标插件已在重载中（`IExposedPlugin` 无状态暴露）→ 需文档说明并发风险

---

## 2. 线程安全要求

### 2.1 Framework 线程约束

Dalamud 插件生命周期操作（包括 `Reload()`）必须在 Framework 线程上执行。项目已注入 `IFramework` 服务，使用 `RunOnFrameworkThread()` 编排：

```csharp
// 模式来自 UnsafeInvokePluginIpcOperation (Phase 11)
if (framework.IsInFrameworkUpdateThread)
    return ExecuteReload(pluginInterface, request);
return await framework.RunOnFrameworkThread(() => ExecuteReload(pluginInterface, request)).ConfigureAwait(false);
```

### 2.2 重载期间的行为

- `Reload()` 是同步阻塞调用，会等待目标插件完全卸载并重新加载
- 重载期间同一 Framework 线程被占用，其他操作需等待
- 无需额外取消支持（重载操作不可中途取消）

---

## 3. 响应设计

### 3.1 状态码体系

根据 ROADMAP 需求，重载操作返回结构化响应：

| 状态码 | 含义 | 触发条件 |
|--------|------|---------|
| `reload_initiated` | 重载已触发 | `Reload()` 调用成功返回 |
| `plugin_not_found` | 未找到目标插件 | `InstalledPlugins` 中无匹配 `InternalName` |
| `reload_failed` | 重载执行异常 | `Reload()` 抛出异常 |
| `self_reload_blocked` | 阻止自身重载 | 请求的 `InternalName` 匹配 DalamudMCP |

### 3.2 响应结构

采用 record 类型（项目惯例），MemoryPack 可序列化：

```csharp
[MemoryPackable]
public sealed partial record PluginReloadResult(
    string PluginName,        // 请求的插件 InternalName
    bool Success,             // 重载是否成功
    string Status,            // 状态码（reload_initiated / plugin_not_found / reload_failed / self_reload_blocked）
    string? ErrorMessage,     // 失败时的错误详情
    string SummaryText        // 人类可读摘要，供 CLI 格式化输出
);
```

### 3.3 MCP 工具描述

MCP 工具名：`reload_plugin`

工具描述中需包含：
- 参数说明：`plugin_name`（插件内部名称）
- 重载后 IPC 通道恢复需要时间（通常 1-3 秒）
- 建议 AI 在重载后使用 `unsafe_invoke_plugin_ipc` 或未来 `invoke_plugin_ipc` 轮询 IPC 通道就绪状态
- 不能重载 DalamudMCP 自身

---

## 4. 与 Phase 11 基础设施的集成

### 4.1 DI 依赖

Phase 11 已提供：
- `IPluginIpcGateway` — 已注册为 DI 单例（`PluginIpcGateway` 实现）
- `IFramework` — 已注册为 DI 单例
- `IDalamudPluginInterface` — 已注册为 DI 单例

新操作 `PluginReloadOperation` 的构造函数：
```csharp
public PluginReloadOperation(
    IDalamudPluginInterface pluginInterface,  // 获取 InstalledPlugins
    IFramework framework)                      // RunOnFrameworkThread
```

**不需要** `IPluginIpcGateway`——重载操作直接使用 `IDalamudPluginInterface`，不通过 IPC 网关。

### 4.2 DI 注册

`AddGeneratedPluginOperations()` 源生成器会自动发现并注册 `[Operation]` 属性标记的类，无需手动修改 `PluginServiceCollectionExtensions.cs`。

### 4.3 暴露策略

重载操作属于高风险操作（「不安全」类别），应在 `PluginOperationExposurePolicy` 中归类为 `unsafe` 操作，受 UI 开关控制。

---

## 5. 测试策略

### 5.1 单元测试

使用 Phase 11 建立的测试基础设施：
- **测试框架：** xUnit v3 + NSubstitute 5.3.0
- **测试项目：** `tests/DalamudMCP.Plugin.Operations.Tests/`

#### 测试桩需求

需要创建 `FakeExposedPlugin` 测试桩（实现 `IExposedPlugin` 接口）：
- `InternalName` 属性可配置
- `Reload()` 行为可控制（正常执行 / 抛出异常）

#### 测试用例

| 测试场景 | 预期结果 |
|----------|---------|
| 正常重载已安装插件 | `Success=true`, `Status=reload_initiated` |
| 目标插件未安装 | `Success=false`, `Status=plugin_not_found` |
| Reload() 抛出异常 | `Success=false`, `Status=reload_failed`, `ErrorMessage` 包含异常消息 |
| 请求重载自身 | `Success=false`, `Status=self_reload_blocked` |
| 空/null 插件名 | `ArgumentException` |
| InternalName 大小写不敏感匹配 | 正确匹配已安装插件 |

### 5.2 测试桩放置

遵循 Phase 11 建立的模式，将 `FakeExposedPlugin` 放在 `tests/DalamudMCP.Plugin.Operations.Tests/TestShared/Ipc/` 目录（与其他 IPC 测试桩共存）。

---

## 6. 边界情况与约束

### 6.1 自身重载保护

尝试重载 DalamudMCP 自身会导致自身进程中断，操作必须阻止此行为。

**检测方式：** 比较请求的 `InternalName` 与当前插件 `IDalamudPluginInterface.InternalName`。

### 6.2 并发重载

如果在短时间内多次请求重载同一插件：

- `IExposedPlugin` 不暴露加载状态
- 多次快速 `Reload()` 调用可能导致 Dalamud 内部状态不一致
- **缓解方案：** 在工具描述中建议 AI 方控制重载频率，不在操作层面加锁（遵循「重载后不自动等待就绪」决策）

### 6.3 Plugin 不在 Framework 线程上下文

遵循 `UnsafeInvokePluginIpcOperation` 模式，检测 `IFramework.IsInFrameworkUpdateThread` 并在需要时编排到 Framework 线程。

---

## 7. 文件结构规划

基于现有代码模式，新操作的文件布局：

```
src/DalamudMCP.Plugin/Operations/PluginReloadOperation.cs  — 操作实现 + Request + Result
tests/DalamudMCP.Plugin.Operations.Tests/PluginReloadOperationTests.cs  — 单元测试
tests/DalamudMCP.Plugin.Operations.Tests/TestShared/Ipc/FakeExposedPlugin.cs  — 测试桩
```

---

## 8. 验证架构 (Nyquist Dimension 8)

### 8.1 验证层次

| 层次 | 验证手段 | 覆盖内容 |
|------|---------|---------|
| 编译 | `dotnet build` | 无编译错误，0 warnings-as-errors |
| 单元测试 | `dotnet test` | 操作逻辑正确性，状态码映射，异常处理 |
| IPC 协议 | `dotnet test` (集成级) | Request 可被 MemoryPack 序列化/反序列化 |
| 生成器 | `dotnet build` | `[Operation]` 属性被源生成器正确发现并注册 |

### 8.2 无法自动验证的项

- **实际 Dalamud 运行时重载行为：** `IExposedPlugin.Reload()` 的真实行为只能在 FFXIV 游戏内验证——标记为 UAT 项
- **IPC 通道恢复时间：** 理论值 1-3 秒，实际因插件而异——在 MCP 工具描述中提供通用建议

---

## 9. 风险与缓解

| 风险 | 可能性 | 影响 | 缓解 |
|------|--------|------|------|
| `IExposedPlugin.Reload()` 在 API 15 中行为变更 | 低 | 中 | Phase 12 按已知 API 实现；Phase 5 已验证运行时操作 |
| 重载后 IPC 通道滞后导致后续调用失败 | 中 | 低 | 已在工具描述中建议等待；AI 端负责重试逻辑 |
| 自身重载被意外触发 | 低 | 高 | 代码级硬阻止，返回 `self_reload_blocked` |
| InstalledPlugins 列表在某些状态下为空 | 低 | 低 | 正常返回 `plugin_not_found` |

---

## 10. 总结

**核心发现：**
1. Dalamud 提供 `IDalamudPluginInterface.InstalledPlugins` 和 `IExposedPlugin.Reload()` 作为标准 API
2. 重载必须在 Framework 线程上执行，已有 `IFramework.RunOnFrameworkThread()` 模式
3. Phase 11 基础设施（DI、`IPluginIpcGateway`、测试桩）为操作类提供完整支撑
4. 响应需结构化状态码，参考 `UnsafeInvokePluginIpcResult` 模式
5. 测试需 mock `IExposedPlugin`——Dalamud 原生接口无法在单元测试中实例化

**就绪状态：** ✅ 可进入规划阶段。所有技术先决条件已满足，无阻塞问题。

---

*研究完成：2026-05-01*
*下一阶段：Phase 12 规划（PLAN.md 创建）*
