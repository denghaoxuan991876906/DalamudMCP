# Project Research Summary

**Project:** DalamudMCP
**Milestone:** API Level 14 -> API Level 15 Migration
**Domain:** FFXIV Dalamud plugin (MCP bridge)
**Researched:** 2026-04-30
**Confidence:** MEDIUM

## Executive Summary

研究一致表明，这并非典型的产品构建项目，而是一次**范围极其有限的平台兼容性迁移**。DalamudMCP 使用桥接/代理模式，将 Dalamud 依赖限制在单一项目 `src/DalamudMCP.Plugin/` 中，其他六个源项目均无 Dalamud 依赖。API 15 的三个已知破坏性变更（IChatGui XivChatType 重构、IClientState RowRef 迁移、ImRaii 移除 IEndObject）均不影响当前代码库。因此，迁移在配置层面仅需三个版本号变更：SDK、manifest API Level、packages.lock.json。

**关键建议：专注于验证而非重构。** 不要同时升级其他 NuGet 依赖，不要重构 DI 模式，不要将 `IDalamudPlugin` 改为 `IAsyncDalamudPlugin`。迁移应严格限定于 SDK 版本升级和运行时验证。最大的未解决问题并非 API 15 本身，而是 Patch 7.5 对 FFXIVClientStructs 布局的破坏性变更，这需要通过完整的运行时测试来覆盖。

**主要风险：** 构建环境指向错误运行时（DALAMUD_HOME 配置）、CI 无法验证插件编译（CI 解决方案排除 Plugin 项目）、同步-over-异步导致死锁（5 个已识别站点）。所有风险均可通过结构化验证清单来缓解。

## Key Findings

### Recommended Stack

迁移只需修改三个配置项。无需新增工具链依赖，无需修改 .NET 版本（仍为 `net10.0-windows7.0`），所有非 Dalamud 依赖（MemoryPack、ModelContextProtocol、xunit 等）均保持不变。

**核心变更：**
- **Dalamud.NET.Sdk**: 14.0.2 -> 15.0.0 — 提供 API 15 引用程序集
- **DalamudMCP.json**: `DalamudApiLevel` 14 -> 15 — 声明运行时兼容性
- **packages.lock.json**: 全部 7 个测试项目的锁文件需重新生成

**需注意：** 不同来源对 SDK 版本号存在分歧 — 项目计划假设 15.0.0，但 API 15 官方文档仍引用 14.0.2。需在实际可获取 SDK 版本时确认最终版本号。

详细信息见 [STACK.md](STACK.md)。

### Expected Features

这是兼容性迁移而非功能开发，因此功能集按"必须处理"和"可以考虑"分类：

**必须处理（Table Stakes）：**
- SDK 版本升级（`.csproj` 头声明）
- Manifest `DalamudApiLevel` 更新（14 -> 15）
- packages.lock.json 重新生成
- DALAMUD_HOME 指向 API 15 运行时
- 确保发布 zip 中 manifest 不被仓库覆盖（API 15 新行为）

**可考虑（Differentiators）：**
- 使用新的 `IAsyncDalamudPlugin` 接口（不推荐在迁移中做）
- 通过 `IDalamudPluginInterface` 作为 `IServiceProvider` 简化的 DI（不推荐在迁移中做）
- 采用更干净的 `LogMessage` 事件（目前无需 chat 功能）

**不包含（Anti-Features）：**
- 不允许同时升级非 Dalamud 依赖
- 不允许修改 IPC 协议
- 不允许重构架构分层

详细信息见 [FEATURES.md](FEATURES.md)。

### Architecture Approach

架构分析确认：六个无 Dalamud 依赖的项目完全不受影响。唯一受影响的组件 `src/DalamudMCP.Plugin/` 的所有 20+ 操作和基础设施代码中，没有任何代码路径使用 API 15 变更涉及的服务接口。

**核心发现：**
1. **分层隔离良好：** Framework、Protocol、CLI、Source Generator 均无 Dalamud 依赖
2. **请求路径无变化：** CLI -> NamedPipe -> Dispatcher -> Operation 的数据流完全不受影响
3. **线程编组模式不受影响：** 代码一致使用同步 `Func<T>` 重载而非异步 `Func<Task<T>>`，后者才是 API 15 标记废弃的
4. **DI 组合无需变更：** 手动 DI 注册模式在 API 15 下仍然有效

详细信息见 [ARCHITECTURE.md](ARCHITECTURE.md)。

### Critical Pitfalls

1. **DALAMUD_HOME 指向错误运行时** — 如果构建环境仍指向 API 14 运行时，编译通过但运行时失败（MissingMethodException）。务必在升级 SDK 前验证 `DALAMUD_HOME` 路径确认为 API 15。

2. **CI 无法验证迁移** — CI 解决方案（`DalamudMCP.CI.slnx`）明确排除了 Plugin 项目。迁移后的所有代码 CI 永不编译。必须建立完整的本地验证清单并有签署确认机制。

3. **同步-over-异步死锁** — 代码库存在 5 个 `.GetAwaiter().GetResult()` 站点，其中 `PluginEntryPoint.cs:61` 在构造器中阻塞等待。API 15 可能收紧线程安全检查，使这块现有技术债务暴露为运行时死锁。

4. **FFXIVClientStructs 布局变更** — 虽然有明确的项目范围声明（不在本次迁移覆盖），但使用 unsafe 指针的操作（AddonInput、InteractWithTarget 等）依赖于 Patch 7.5 可能改变的内存布局。这是最高风险的运行时问题。

5. **Protocol 版本失配** — SDK 升级后 packages.lock.json 重新生成可能导致 MemoryPack 版本在 CLI 与 Plugin 之间不一致，导致二进制反序列化静默失败。

详细信息见 [PITFALLS.md](PITFALLS.md)。

## Implications for Roadmap

基于综合研究，建议按以下 Phases 组织迁移：

### Phase 0: 前提条件验证
**Rationale:** 在研究过程中发现的阻塞性问题：SDK 版本不确定性、DALAMUD_HOME 配置要求、CI 无法验证。这些问题必须在任何代码变更前确认。
**Delivers:** 可操作的迁移环境（已验证的 API 15 DALAMUD_HOME、确认的 SDK 版本、本地验证清单）
**Addresses:** 基础设施准备
**Avoids:** 陷阱 6 (DALAMUD_HOME 错误)、陷阱 7 (CI 无法验证)

### Phase 1: SDK 升级与构建基础设施
**Rationale:** 这是所有后续工作的基础。三个配置变更（SDK、manifest、lock files）必须首先完成并验证编译通过。
**Delivers:** 可通过 SDK/API 15 编译的代码库
**Addresses:** 表属性任务（SDK 版本、Manifest API Level、packages.lock.json 更新）
**Avoids:** 陷阱 5 (Manifest 不匹配)、陷阱 8 (MemoryPack 版本失配)
**可能需要研究：** 如果 SDK 版本不是假设的 15.0.0，需在恢复后确认实际版本并相应调整文档。

### Phase 2: API 兼容性审计
**Rationale:** API 15 的三个破坏性变更虽然都不影响当前代码路径，但仍需系统审计所有 IClientState 注入点（20+ 操作类）以确保没有遗漏。
**Delivers:** 确认所有代码路径与 API 15 兼容
**Addresses:** 代码适配（如需要）
**Avoids:** 陷阱 1 (IClientState 静默损坏)、陷阱 2 (XivChatType 语义变化)、陷阱 9 (NuGet 依赖移除)
**不需要研究：** 标准模式，代码审计和经验证的三次变更即可。

### Phase 3: 线程安全性加固（可选但推荐）
**Rationale:** 5 个 `.GetAwaiter().GetResult()` 站点是已知的技术债务。API 15 可能暴露这些问题导致死锁。虽然严格来说不是迁移必需品，但在同一里程碑内解决的成本远低于事后修复。
**Delivers:** 消除同步-over-异步死锁风险
**Addresses:** 线程安全改进
**Avoids:** 陷阱 4 (同步-over-异步死锁)
**可能需要研究：** 如果转换 `PluginEntryPoint.cs:61` 的模式不明确（从构造器阻塞改为异步工厂模式），可能需要浅研究最佳方案。

### Phase 4: 运行时验证
**Rationale:** 编译成功不等于迁移完成。真正的验证发生在 FFXIV + API 15 Dalamud 运行时：加载、操作、IPC、unsafe 操作。这是四个 Phase 中工作量最大的。
**Delivers:** 确认迁移完成的信心
**Addresses:** 所有 20+ 操作的功能正确性、IPC 往返通信、Patch 7.5 兼容性
**Avoids:** 陷阱 10 (FFXIVClientStructs 布局不匹配)
**可能需要研究：** 如果 FFXIVClientStructs 布局问题被触发，则需要在迁移之外额外规划 struct offset 修复的研究 Phase。

### Phase 排序理由

1. **Phase 0 在 Phase 1 之前** — 没有可用的 API 15 DALAMUD_HOME 和 SDK 版本确认就开始升级是盲目的，这是 PITFALLS.md 中最高优先级的问题。
2. **Phase 1 在 Phase 2 之前** — 必须先升级 SDK 才能编译与测试 API 兼容性。
3. **Phase 2 在 Phase 3 之前** — API 兼容性审计优先于线程安全加固，因为 API 15 可能引入的兼容性问题必须先排除。
4. **Phase 4 在最后** — 运行时验证需要前三个 Phase 全部完成。
5. **Phase 3 可选但推荐** — 本质上属于重构范畴，PITFALLS.md 和 CONCERNS.md 都已识别此风险。建议在迁移里程碑中安排，但如果不是时间紧迫可以降级。

### Research Flags

可能需要深入研究的 Phase：
- **Phase 0:** SDK 版本不确定性需要在实际环境中确认。官方文档引用 14.0.2，但项目计划使用 15.0.0。需 `dotnet nuget list source` 确认。
- **Phase 3:** 如果 `PluginEntryPoint.cs` 的同步构造成分需要重构为异步工厂模式，可能需要浅研究以确定最佳方案。
- **Phase 4:** 如果 Patch 7.5 的 FFXIVClientStructs 变更影响 unsafe 操作，需要额外研究 struct offset 变更。

可使用标准模式的 Phase（无需研究）：
- **Phase 1:** SDK 版本升级、manifest 更新、lock file 重新生成都是标准操作。
- **Phase 2:** API 审计是常规代码审查工作，无需额外研究。

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | NuGet 确认 `Dalamud.NET.Sdk/15.0.0` 和 `DalamudPackager/15.0.0` 存在。API 15 文档确认 .NET 10.0、破坏性变更。唯一的低置信点是 SDK 版本号在文档间不一致。 |
| Features | MEDIUM | API 15 尚未最终发布。破坏性变更清单可靠（官方文档），但当前代码库对这些变更的零影响评估基于代码审计，置信度高。SDK 版本号需要最终确认。 |
| Architecture | MEDIUM | 代码审计（`src/DalamudMCP.Plugin/` 的全面分析）置信度高。但 API 15 是否引入超出文档列的额外接口变更（尤其是 IClientState 未公开的变更）存在不确定性。 |
| Pitfalls | MEDIUM | API 13->14 的历史迁移模式验证可靠。5 个同步-over-异步站点的审计基于实际代码阅读（高置信度）。FFXIVClientStructs 的 Patch 7.5 影响是基于过去经验推断的。 |

**总体置信度：MEDIUM**

置信度评级为 MEDIUM 的主要原因是：API 15 尚未最终发布，SDK/文档间的版本号存在分歧，以及 FFXIVClientStructs 布局变更的未知影响。代码审计层面的发现置信度高，但运行时的意外行为可能性无法完全排除。

### Gaps to Address

- **SDK 最终版本号：** 文档引用 14.0.2（可能是文档未更新），项目计划假设 15.0.0。需在 Phase 0 通过 NuGet 源确认。如果 `Dalamud.NET.Sdk/15.0.0` 不可用，可能需要使用 `14.0.2` 作为过渡 — 这不会影响 API Level（manifest 仍声明 15，只要 DALAMUD_HOME 指向 API 15 运行时）。

- **FFXIVClientStructs 具体变更清单：** API 15 文档未列出 FFXIVClientStructs 的独立破坏性变更。Patch 7.5 通常每季度带来 struct 布局变更。无法从文档预防，只能通过 Phase 4 的运行时测试发现。

- **第三方 IPC 插件兼容性：** Lifestream、Vnavmesh 等插件的 API 15 更新时间表未知。DalamudMCP 的操作如果依赖这些插件，需要在 Phase 4 中测试并加入降级逻辑。

## Sources

### Primary (HIGH confidence)
- 官方 API 15 文档: https://dalamud.dev/versions/v15/ — 确认 SDK v15.0.0、Packager v15.0.0、API Level 15、.NET 10.0、三个破坏性变更
- NuGet API: `Dalamud.NET.Sdk/15.0.0` 和 `DalamudPackager/15.0.0` 存在（MIT/EUPL-1.2 许可证）
- 代码库审计: `src/DalamudMCP.Plugin/` 的全面源代码分析

### Secondary (MEDIUM confidence)
- API 15 API 文档: `IDalamudPluginInterface`、`IClientState`、`IFramework` 的 API 15 版本参考 — 确认接口签名未受影响的属性
- API 15 特性参考: `XivChatRelationKind` 枚举文档 — 新功能参考
- Dalamud 更新 FAQ: https://dalamud.dev/faq/updates/ — API 版本变更流程
- API 13->14 变更日志: https://dalamud.dev/versions/v14/ — 历史迁移模式验证

### Tertiary (LOW confidence)
- 网络搜索结果（"Dalamud v15 breaking changes" 多次查询）— 搜索结果在 API 15 存在性上相互矛盾
- Dalamud 插件开发者社区知识 — 来自多个网络搜索结果的聚合

---
*Research completed: 2026-04-30*
*Ready for roadmap: yes*
