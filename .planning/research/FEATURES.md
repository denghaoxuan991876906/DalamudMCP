# Feature Research: Dalamud API 15 Migration

**Domain:** FFXIV Dalamud Plugin (API Level upgrade 14 -> 15)
**Researched:** 2026-04-30
**Confidence:** MEDIUM (API 15 尚未最终发布，细节可能变化)

## Migration Feature Landscape

### Table Stakes (Every Plugin MUST Handle)

这些是 API 15 迁移的强制性变更。遗漏任意一项将导致插件无法在 API 15 运行时加载。

| 变更项 | 原因 | 影响范围 | DalamudMCP 影响评估 |
|---------|------|----------|---------------------|
| **SDK 版本升级** | `Dalamud.NET.Sdk` 必须升级以提供 API 15 的引用程序集 | `.csproj` 头部声明 | `Dalamud.NET.Sdk/15.0.0` (PROJECT.md: 15.0.0 待验证) |
| **Packager 版本升级** | `DalamudPackager` 必须匹配 API 15 | `packages.lock.json`, `.csproj` | `DalamudPackager/15.0.0` (待验证) |
| **Manifest API Level 更新** | `DalamudMCP.json` 中 `DalamudApiLevel` 必须改为 15 | 插件清单文件 | 从 `14` 改为 `15` |
| **Manifest 准确性要求** | API 15 不再用 repo manifest 覆盖 zip 内的 `InternalName.json` | 发布打包流程 | 确保 zip 包含正确的 manifest |
| **IChatGui - XivChatType 重构** | `XivChatType` 在 `OnMessage` 事件中不再包含 packed relation data，拆分为 `sourceKind`/`targetKind` 参数 | 所有订阅 `IChatGui.OnMessage` 的代码 | **不适用** — DalamudMCP 不使用 IChatGui |
| **IClientState - ZoneInitEventArgs 改用 RowRef** | `ZoneInitEventArgs` 的 territory 引用从 raw ID 改为 `RowRef<TerritoryType>` | 订阅 `IClientState.ZoneInit` 事件的代码 | **不适用** — DalamudMCP 不订阅 ZoneInit |
| **IClientState - ActiveFestivals 合并** | `ActiveFestivals` 和 `ActiveFestivalPhases` 数组合并为 `IReadOnlyList<FestivalEntry> ActiveFestivals` | 访问 `IClientState` 节日信息的代码 | **不适用** — DalamudMCP 不使用该属性 |
| **NuGet package lock 文件更新** | SDK 和 Packager 版本变更需要更新 `packages.lock.json` | 所有 `packages.lock.json` 文件 | 全部 7 个测试项目的 lock 文件需更新 |

#### Table Stakes: SDK 版本确认

| 来源 | SDK 版本 | Packager 版本 |
|------|----------|---------------|
| 官方 API 15 文档 (dalamud.dev/versions/v15/) | `Dalamud.NET.Sdk/14.0.2` | `DalamudPackager/14.0.2` |
| PROJECT.md 当前计划 | `Dalamud.NET.Sdk/15.0.0` | `DalamudPackager/15.0.0` |
| **结论** | **待验证** — 项目计划假设 15.x，但官方文档引用 14.0.2。应在 DALAMUD_HOME 指向 API 15 程序集后检查可用版本。 |

> **动作项**: 升级前运行 `dotnet nuget list source` 确认 Dalamud SDK 可用版本，或直接检查 DALAMUD_HOME 目录下的 SDK 版本。

### Differentiators (Nice-to-Have API 15 Features)

API 15 引入的新功能，并非迁移必需，但可提升插件质量或简化代码。

| 功能 | 价值 | 复杂度 | DalamudMCP 建议 |
|------|------|--------|-----------------|
| **LogMessage 事件替代 OnMessage** | API 14 引入的 LogMessage 事件提供更干净的 chat 消息处理方式，避免 XivChatType 兼容性问题 | LOW | **暂不需要** — 当前不使用 chat 事件，将来如需 chat 读取应使用此事件而非 OnMessage |
| **XivChatRelationKind 枚举** | 提供标准化的 source/target 关系分类（PartyMember, AllianceMember 等），取代手写关系判断 | LOW | **暂不需要** — 仅在实现 chat 消息分析时有用 |
| **RowRef 强类型引用** | ZoneInitEventArgs 使用 `RowRef<TerritoryType>` 提供强类型 territory 数据访问 | LOW | **暂不需要** — 但可作为模式参考用于将来操作 |

### Anti-Features (Deprecated APIs to Stop Using)

API 15 中应停止使用的模式。

| 废弃模式 | 问题 | 替代方案 | DalamudMCP 影响 |
|----------|------|----------|-----------------|
| **依赖 XivChatType 超出范围值 (>110)** | API 15 中 XivChatType 不再编码 relation 数据，超出 LogKind sheet 范围的值不再存在 | 使用 `LogMessage` 事件或新的 `sourceKind`/`targetKind` 参数 | **不适用** — 不使用 XivChatType |
| **依赖 zip 内 manifest 被覆盖** | API 15 不再覆盖 manifest，必须手动确保其准确性 | 在打包流程中验证 manifest 内容 | **适用** — 需确保 `DalamudMCP.json` 在发布 zip 中准确无误 |
| **API Level 14 的 SDK** | `Dalamud.NET.Sdk/14.x` 不包含 API 15 的引用程序集 | 升级到 `Dalamud.NET.Sdk/15.x` | **适用** — 必须升级 |
| **Lumina raw row ID 模式** | ZoneInitEventArgs 的 RowRef 变更提示 Lumina API 正逐步过渡到 RowRef 强类型访问 | 优先使用 `RowRef<T>` 而非 raw RowID + `IDataManager.GetExcelSheet<T>()` | **注意** — 当前使用 `IDataManager.GetExcelSheet<Quest>()` 模式，该模式本身未废弃，但应关注 Lumina 变化 |

## Migration Impact Assessment

### DalamudMCP 受影响的 API 使用扫描

| Dalamud 服务 | 使用情况 | API 15 变更 | 实际影响 |
|-------------|----------|-------------|----------|
| `IClientState` | 广泛使用（20+ 操作注入） | ZoneInitEventArgs + ActiveFestivals | **无代码影响** — 仅使用 `TerritoryType` (ushort，未变)、`TerritoryChanged` (未变) 和 `LocalPlayer` |
| `IClientState.TerritoryType` | 7 处引用（CurrentQuestObjective、DutyContext、FateContext、AvailableQuests、PlayerContext 等） | 未变化，仍为 `ushort` | **无影响** |
| `IFramework` | 13+ 操作注入 | 未变化 | **无影响** |
| `ICondition` | DutyContextOperation、PluginEntryPoint | 未变化 | **无影响** |
| `IObjectTable` | 7+ 操作注入 | 未变化 | **无影响** |
| `IGameGui` | 8 个 addon 相关操作注入 | 未变化 | **无影响** |
| `ITargetManager` | 3 个操作注入 | 未变化 | **无影响** |
| `IDataManager` | 3 个 quest 相关操作注入 | 未变化 | **无影响** |
| `IChatGui` | 未使用 | XivChatType 重构 | **无影响** |

### Comprehensive Impact Summary

```
SDK 升级 (必须)
  └──requires──> Dalamud.NET.Sdk 15.x
  └──requires──> DalamudPackager 15.x
  └──requires──> packages.lock.json 更新

Manifest 更新 (必须)
  └──requires──> DalamudMCP.json: DalamudApiLevel: 14 -> 15
  └──requires──> 确保 zip manifest 准确 (发布流程)

IChatGui XivChatType 重构
  └──enhances──> XivChatRelationKind 枚举
  └──impact──>   DalamudMCP: 不适用 (不使用)

IClientState ZoneInitEventArgs
  └──impact──>   DalamudMCP: 不适用 (不订阅 ZoneInit)

IClientState ActiveFestivals 合并
  └──impact──>   DalamudMCP: 不适用 (不使用)
```

### 代码变更范围

```
实际需要修改的文件:
  1. src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj          -- SDK 版本
  2. src/DalamudMCP.Plugin/DalamudMCP.json                   -- DalamudApiLevel
  3. src/DalamudMCP.Plugin/DalamudPackager.targets            -- 版本号 (如有硬编码)
  4. tests/DalamudMCP.Plugin.Tests/packages.lock.json         -- 版本锁文件
  5. tests/DalamudMCP.Plugin.Operations.Tests/packages.lock.json -- 版本锁文件
  6. 其他 test 项目的 packages.lock.json                          -- 版本锁文件

理论上无需修改的代码:
  - src/DalamudMCP.Plugin/Operations/*.cs  -- API 15 未改变任何使用的接口
  - src/DalamudMCP.Plugin/Hosting/*.cs     -- 未使用受影响的事件
  - src/DalamudMCP.Protocol/*.cs           -- 纯协议层，无 Dalamud 依赖
  - src/DalamudMCP.Framework/*.cs          -- 纯抽象层，无 Dalamud 依赖
  - src/DalamudMCP.Cli/*.cs                -- CLI 层，无 Dalamud 依赖
```

## Migration Task Breakdown

### Phase 1: Build Infrastructure (阻塞性，必须先完成)

| 任务 | 文件 | 验证方式 |
|------|------|----------|
| 更新 `.csproj` SDK 声明 | `DalamudMCP.Plugin.csproj` 第1行 | `dotnet build` 成功 |
| 更新 `DalamudApiLevel` | `DalamudMCP.json` 第14行 | manifest 文件验证 |
| 更新 `packages.lock.json` 文件 | 所有 test 项目 | `dotnet restore --locked-mode` 成功 |
| 设置 `DALAMUD_HOME` 指向 API 15 目录 | 构建环境 | `DalamudMCP.json` 引用程序集版本匹配 |

### Phase 2: Code Adaptation (如有必要)

| 任务 | 触发条件 | 处理方式 |
|------|----------|----------|
| IChatGui 变更适应 | 如果将来添加 chat 操作 | 使用 LogMessage 事件 + XivChatRelationKind |
| IClientState 变更适应 | 如果将来订阅 ZoneInit | 使用 RowRef<TerritoryType> |
| ActiveFestivals 变更适应 | 如果将来读取节日信息 | 使用合并后的 IReadOnlyList<FestivalEntry> |

### Phase 3: Verification

| 任务 | 验证方式 |
|------|----------|
| 插件在 API 15 运行时成功加载 | 启动 FFXIV + Dalamud，确认插件列表显示 |
| 所有 20+ 操作正常工作 | 运行对应 CLI 命令验证输出 |
| 命名管道 IPC 通信正常 | CLI <-> Plugin 往返测试 |
| MCP 服务器模式健康 | HTTP/stdio MCP 工具调用测试 |

## Sources

### Authoritative Documentation
- [What's New in Dalamud v15](https://dalamud.dev/versions/v15/) — 官方 API 15 变更日志 (MEDIUM confidence, 文档未最终确定)
- [XivChatRelationKind Enum (API 15)](https://dalamud.dev/api/api15/Dalamud.Game.Text/Enums/XivChatRelationKind/) — 新枚举参考 (MEDIUM confidence)
- [IClientState Interface (API 15)](https://dalamud.dev/api/api15/Dalamud.Plugin.Services/Interfaces/IClientState/) — API 15 参考 (MEDIUM confidence)
- [Dalamud Updates FAQ](https://dalamud.dev/faq/updates/) — API 版本变更流程说明 (MEDIUM confidence)
- [Dalamud Namespace (API 15)](https://dalamud.dev/api/api15/Dalamud.Game.Text/) — API 15 命名空间参考 (MEDIUM confidence)

### Codebase Sources
- `src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj` — 当前 SDK v14.0.2, 需升级
- `src/DalamudMCP.Plugin/DalamudMCP.json` — 当前 `DalamudApiLevel: 14`, 需改为 15
- `src/DalamudMCP.Plugin/Operations/*.cs` — 20+ 操作实现，评估全部不受 API 15 影响
- `.planning/PROJECT.md` — 项目 v0.2.0，目标 API 15 升级

### Open Questions / Low Confidence Areas
- **SDK 版本号**: 官方文档引用 14.0.2 但项目计划使用 15.0.0。需在 DALAMUD_HOME 可用时确认实际版本。
- **FFXIVClientStructs 变更**: API 15 文档提到 FFXIVClientStructs 有独立的 breaking changes (Patch 7.5 相关)，但未详细列出。DalamudMCP 不直接使用 FFXIVClientStructs，但间接通过 Dalamud 内部可能受影响。需在升级后运行完整测试套件确认。
- **Lumina/IDataManager 间接影响**: API 15 的 RowRef 趋势可能影响 Lumina 数据访问。当前使用 `IDataManager.GetExcelSheet<T>()` 模式本身未变化。

---
*Feature research for: Dalamud API 15 migration (DalamudMCP)*
*Researched: 2026-04-30*
