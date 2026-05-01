---
phase: 15-data-relay
plan: 01
subsystem: ipc
tags: [channel, dalamud-ipc, relay, data-push, bounded-channel, provider]

# Dependency graph
requires:
  - phase: 14-safe-ipc-invoke
    provides: "PluginIpcGateway DI 注册模式、PluginServiceCollectionExtensions 结构"
provides:
  - "IPluginDataRelayService 接口——Subscribe/Unsubscribe/TryPoll/IsSubscribed/ActiveChannels"
  - "PluginDataRelayService DI 单例——ConcurrentDictionary 管理 RelayChannel 集合"
  - "RelayChannel record——封装 Channel<string> + ICallGateProvider + 元数据"
  - "IPC Provider 注册模式——GetIpcProvider<string,object> + RegisterAction"
  - "自动清理——IFramework.Update 每 60 帧检测 InstalledPlugins 变化"
affects: [15-02, data-relay-operations, relay-ops]

# Tech tracking
tech-stack:
  added: [System.Threading.Channels, Dalamud.Plugin.Ipc.ICallGateProvider]
  patterns: ["ConcurrentDictionary-based channel registry", "IFramework.Update throttled auto-cleanup (60-frame)", "BoundedChannelFullMode.DropOldest overflow strategy"]

key-files:
  created:
    - "src/DalamudMCP.Plugin/Relay/IPluginDataRelayService.cs"
    - "src/DalamudMCP.Plugin/Relay/RelayChannel.cs"
    - "src/DalamudMCP.Plugin/Relay/PluginDataRelayService.cs"
  modified:
    - "src/DalamudMCP.Plugin/Hosting/PluginServiceCollectionExtensions.cs"

key-decisions:
  - "使用 ICallGateProvider<string, object>.UnregisterAction() 而非 IDisposable.Dispose()——Dalamud SDK 15 中 ICallGateProvider 不实现 IDisposable"
  - "IFramework.OnUpdateDelegate 签名为 void(IFramework)，回调需接受 IFramework 参数"
  - "接口成员需显式 public 修饰符——项目分析器 IDE0040 规则要求"
  - "Relay/ 目录文件使用 UTF-8 无 BOM 编码——与项目现有编码风格一致"

patterns-established:
  - "IPC Provider 注册模式: pluginInterface.GetIpcProvider<string, object>(callGate).RegisterAction(handler)"
  - "IPC Provider 注销模式: provider.UnregisterAction()"
  - "订阅生命周期: Subscribe→Unsubscribe+自动清理→Dispose 批量清理"

requirements-completed:
  - RELAY-01

# Metrics
duration: 14min
completed: 2026-05-01
---

# Phase 15 Plan 01: PluginDataRelayService 基础设施 Summary

**PluginDataRelayService DI 单例——管理 ConcurrentDictionary 有界 Channel 注册表 + IPC Provider 注册/注销 + IFramework.Update 自动清理**

## Performance

- **Duration:** 14 min
- **Started:** 2026-05-01T16:09:45Z
- **Completed:** 2026-05-01T16:23:35Z
- **Tasks:** 3
- **Files modified:** 4

## Accomplishments

- 创建 IPluginDataRelayService 接口，定义 Subscribe/Unsubscribe/TryPoll/IsSubscribed/ActiveChannels 五个公开成员
- 创建 RelayChannel internal record，封装 Channel<string> + ICallGateProvider<string, object> + 元数据
- 实现 PluginDataRelayService 单例服务——管理 ConcurrentDictionary 通道注册表、IPC Provider 注册/注销、60 帧节流自动清理
- 在 PluginServiceCollectionExtensions 注册 AddSingleton<IPluginDataRelayService, PluginDataRelayService>()

## Task Commits

Each task was committed atomically:

1. **Task 1: 创建 IPluginDataRelayService 接口和 RelayChannel record** - `e98be66` (feat)
2. **Task 2: 创建 PluginDataRelayService 实现（Channel 管理 + IPC Provider + 自动清理）** - `1a51fad` (feat)
3. **Task 3: 注册 IPluginDataRelayService 到 DI 容器** - `b6b7ada` (feat)

## Files Created/Modified

- `src/DalamudMCP.Plugin/Relay/IPluginDataRelayService.cs` - 公开接口：Subscribe/Unsubscribe/TryPoll/IsSubscribed/ActiveChannels
- `src/DalamudMCP.Plugin/Relay/RelayChannel.cs` - internal record：封装 Channel<string> + ICallGateProvider<string, object> + PluginName + FullChannelName + CreatedAt
- `src/DalamudMCP.Plugin/Relay/PluginDataRelayService.cs` - internal sealed 实现：ConcurrentDictionary 注册表 + BoundedChannel(DropOldest, 1000) + IFramework.Update 自动清理
- `src/DalamudMCP.Plugin/Hosting/PluginServiceCollectionExtensions.cs` - 添加 using DalamudMCP.Plugin.Relay + AddSingleton 注册

## Decisions Made

- **ICallGateProvider 清理机制：** Dalamud SDK 15 中 ICallGateProvider<string, object> 不实现 IDisposable，使用 `UnregisterAction()` 注销 IPC 端点（计划假设了 Dispose 模式，实际 API 不同）
- **IFramework.OnUpdateDelegate 签名：** 实际签名为 `void(IFramework framework)`，回调方法需接受 IFramework 参数
- **接口成员显式修饰符：** 项目分析器 IDE0040 要求接口成员显式加 public 修饰符，接口文件无需 `using System.Threading.Channels`
- **文件编码：** 与项目现有编码风格一致，使用 UTF-8 无 BOM（计划指定 BOM 但项目其他文件均无 BOM）

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] 修复接口成员缺少显式 public 修饰符和多余 using**
- **Found during:** Task 1（IPluginDataRelayService.cs 编译）
- **Issue:** IDE0040 要求接口成员显式可访问性修饰符；IDE0005 报告 `using System.Threading.Channels;` 不需要
- **Fix:** 为所有 5 个接口成员添加 `public` 修饰符；删除接口文件中不需要的 using 语句
- **Files modified:** src/DalamudMCP.Plugin/Relay/IPluginDataRelayService.cs
- **Verification:** dotnet build 零错误
- **Committed in:** e98be66（Task 1 提交）

**2. [Rule 1 - Bug] 修复 ICallGateProvider 不实现 IDisposable**
- **Found during:** Task 2（PluginDataRelayService.cs 编译）
- **Issue:** 计划假设 `ICallGateProvider<string, object>` 实现 `IDisposable` 可调用 `.Dispose()` 注销——实际 Dalamud SDK 15 中该类使用 `UnregisterAction()` 方法
- **Fix:** 将 `provider.Dispose()` 替换为 `provider.UnregisterAction()`；RelayChannel 记录中将 `IDisposable IpcProvider` 改为 `ICallGateProvider<string, object> IpcProvider`；添加 `using Dalamud.Plugin.Ipc;`
- **Files modified:** src/DalamudMCP.Plugin/Relay/PluginDataRelayService.cs, src/DalamudMCP.Plugin/Relay/RelayChannel.cs
- **Verification:** dotnet build 零错误；grep UnregisterAction 确认 Dispose 和 Unsubscribe 两处均调用
- **Committed in:** 1a51fad（Task 2 提交）

**3. [Rule 1 - Bug] 修复 IFramework.OnUpdateDelegate 签名不匹配**
- **Found during:** Task 2（PluginDataRelayService.cs 编译）
- **Issue:** 计划假设 `IFramework.Update` 事件委托为无参数 `Action`——实际签名为 `IFramework.OnUpdateDelegate(IFramework framework)`
- **Fix:** `OnFrameworkUpdate()` 改为 `OnFrameworkUpdate(IFramework _)`，使用丢弃参数 `_`
- **Files modified:** src/DalamudMCP.Plugin/Relay/PluginDataRelayService.cs
- **Verification:** dotnet build 零错误
- **Committed in:** 1a51fad（Task 2 提交）

---

**Total deviations:** 3 auto-fixed（均为 Rule 1 - Bug）
**Impact on plan:** 全部自动修复为必要的 API 对齐——功能实现正确，计划假设的 Dalamud SDK API 细节与实际情况有差异（ICallGateProvider 不实现 IDisposable、IFramework.OnUpdateDelegate 含参数）。无功能影响，三个任务全部完成并编译通过。

## Issues Encountered

- PowerShell `Get-Content -Raw` 配合 `UTF8Encoding` BOM 重写导致中文字符损坏——直接使用 Write 工具重写文件解决（文件编码改为与项目一致的 UTF-8 无 BOM）

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- PluginDataRelayService 基础设施就绪，三个操作类（subscribe/unsubscribe/poll）可通过 DI 构造注入 `IPluginDataRelayService`
- Phase 15 Plan 02（操作类 + 测试）可直接使用本计划创建的接口和服务实现
- 建议 Plan 02 继续使用 `ICallGateProvider.UnregisterAction()` 模式（非 `IDisposable`）
- 建议 Plan 02 的测试桩遵循 RelayChannel 的实际类型签名

---
*Phase: 15-data-relay*
*Completed: 2026-05-01*

## Self-Check: PASSED

- 4/4 created/modified files verified on disk
- 3/3 task commits verified in git log
- dotnet build: 0 errors, 0 warnings
