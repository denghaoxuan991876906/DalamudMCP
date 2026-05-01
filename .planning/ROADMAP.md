# Roadmap: DalamudMCP

## Milestones

- ✅ **v1.0 API 15 迁移** — Phases 1-10 (shipped 2026-05-01)
- 🚧 **v1.1 自动化测试桥接** — Phases 11-15 (in progress)

## Phases

<details>
<summary>✅ v1.0 API 15 迁移 (Phases 1-10) — SHIPPED 2026-05-01</summary>

- [x] Phase 1: 构建环境前提确认 — completed 2026-05-01
- [x] Phase 2: SDK 版本升级 — completed 2026-05-01
- [x] Phase 3: Manifest 与锁文件更新 — completed 2026-05-01
- [x] Phase 4: 编译验证 — completed 2026-05-01
- [x] Phase 5: 运行时加载与操作验证 — completed 2026-05-01
- [x] Phase 6: IPC 桥接与 CLI 模式验证 — completed 2026-05-01
- [x] Phase 7: 打包验证 — completed 2026-05-01
- [x] Phase 8: 改中文界面 — completed 2026-05-01
- [x] Phase 9: 可切换界面语言 (3/3 plans) — completed 2026-05-01
- [x] Phase 10: 添加日志读取能力 (3/3 plans) — completed 2026-05-01

</details>

### 🚧 v1.1 自动化测试桥接 (In Progress)

**Milestone Goal:** 让 DalamudMCP 成为其他 Dalamud 插件的自动化测试桥梁，AI 可重载插件、调用 IPC、接收回传数据、发送斜杠命令

- [x] **Phase 11: IPC 基础设施提取** — 提取共享 IPC 网关服务，为后续跨插件功能奠定基础
- [ ] **Phase 12: 插件重载操作** — AI 通过 MCP 触发指定插件重载
- [x] **Phase 13: 斜杠命令调度** — AI 通过 MCP 发送游戏内斜杠命令
- [ ] **Phase 14: 安全 IPC 调用** — AI 通过 MCP 调用目标插件的 IPC 方法并获取返回值
- [ ] **Phase 15: 数据回传** — 目标插件通过 IPC 发送数据，AI 通过 MCP 轮询获取

## Phase Details

### Phase 11: IPC 基础设施提取
**Goal**: 共享 IPC 网关服务可被所有跨插件操作注入使用，现有功能无回归
**Depends on**: Nothing (基础设施，但依赖 v1.0 已完成的操作模型)
**Requirements**: （基础设施阶段，为 RELOAD-01、IPC-01、RELAY-01 提供支撑）
**Success Criteria** (what must be TRUE):
  1. `IPluginIpcGateway` 和 `IPluginCallGateSubscriber` 从 `UnsafeInvokePluginIpcOperation` 提取为独立单例服务，注册到 DI 容器
  2. 现有 `UnsafeInvokePluginIpcOperation` 重构为使用共享 IPC 网关，功能无回归
  3. 所有现有测试通过，新增共享服务的单元测试
  4. 新操作类可通过 DI 注入 `IPluginIpcGateway` 实现跨插件 IPC 调用
**Plans**: 3 plans

Plans:
- [x] 11-01-PLAN.md — 提取 IPC 接口和实现到独立文件（4 个新文件）
- [x] 11-02-PLAN.md — 重构操作类并注册 DI（2 个现有文件修改）
- [x] 11-03-PLAN.md — 提取测试桩并新增服务测试（6 个文件）

### Phase 12: 插件重载操作
**Goal**: AI 客户端能够通过 MCP 工具触发指定插件的卸载→重载，获取结构化状态响应
**Depends on**: Phase 11
**Requirements**: RELOAD-01
**Success Criteria** (what must be TRUE):
  1. AI 通过 MCP `reload_plugin` 工具指定插件内部名称，触发该插件的 unload→reload 流程
  2. 重载操作返回结构化响应，包含 `reload_initiated`/`plugin_not_found`/`reload_failed`/`self_reload_blocked` 等状态码
  3. 重载操作在 Framework 线程上执行 `IExposedPlugin.Reload()`，不阻塞游戏主线程
  4. MCP 工具描述中包含等待建议，指导 AI 在重载后轮询 IPC 通道就绪状态
**Plans**: 2 plans

Plans:
- [x] 12-01-PLAN.md — 创建 PluginReloadOperation 操作（含 4 状态码响应 + 暴露策略注册）
- [x] 12-02-PLAN.md — 创建测试桩和单元测试（12 个测试覆盖全部状态码路径）

### Phase 13: 斜杠命令调度
**Goal**: AI 客户端能够通过 MCP 发送 Dalamud 注册的斜杠命令到游戏内
**Depends on**: Phase 11
**Requirements**: SLASH-01
**Success Criteria** (what must be TRUE):
  1. AI 通过 MCP `slash_command` 工具发送以 `/` 开头的命令字符串
  2. 命令通过 `ICommandManager.ProcessCommand()` 在 Framework 线程上派发，采用 fire-and-forget 模式
  3. 输入经过验证：命令必须以 `/` 开头（D-01）、长度 ≤ 256 字符（D-01）、不过滤特殊字符（D-02）
  4. 仅支持 Dalamud 注册命令，游戏原生命令在 MCP 工具描述中说明限制
**Plans**: 2 plans

Plans:
- [x] 13-01-PLAN.md — 创建 SlashCommandOperation 操作类 + 注册 unsafe 暴露策略
- [x] 13-02-PLAN.md — 创建单元测试（11 个测试覆盖全部验证/线程/构造路径）

### Phase 14: 安全 IPC 调用
**Goal**: AI 客户端能够通过 MCP 调用目标插件的 IPC 函数，传入参数并获取返回值，错误信息结构化可读
**Depends on**: Phase 11
**Requirements**: IPC-01
**Success Criteria** (what must be TRUE):
  1. AI 通过 MCP `invoke_plugin_ipc` 工具指定插件名 + 方法名 + 参数，调用目标插件的 IPC 函数
  2. IPC 调用使用约定式命名 `{Name}.MCP.{Action}`，目标插件零 SDK 依赖，只需按约定暴露接口
  3. IPC 调用在 Framework 线程上执行，支持基元类型和 JSON 字符串信封作为参数
  4. 错误响应细分为 `ipc_missing`/`ipc_not_ready`/`ipc_type_mismatch`/`ipc_plugin_error` 等状态码
  5. 现有 `unsafe.invoke.plugin-ipc` 逃生舱继续工作，新安全版本为推荐方式
**Plans**: 2 plans

Plans:
- [ ] 14-01-PLAN.md — 创建 SafeInvokePluginIpcOperation 操作类 + 注册 "plugin.ipc" 到 unsafe 暴露策略
- [ ] 14-02-PLAN.md — 创建单元测试（24 个测试覆盖全部 5 种状态码/类型推断/线程编排/构造验证）

### Phase 15: 数据回传
**Goal**: 目标插件能够通过 IPC 向 DalamudMCP 推送结构化数据，AI 客户端通过 MCP 操作轮询获取这些数据
**Depends on**: Phase 14
**Requirements**: RELAY-01
**Success Criteria** (what must be TRUE):
  1. 目标插件通过 IPC SendMessage 推送结构化数据到 DalamudMCP，DalamudMCP 缓存在有界 Channel 中
  2. AI 通过 MCP `plugin_data_poll` 操作按通道名轮询获取已缓存的数据
  3. AI 通过 MCP `plugin_data_subscribe`/`plugin_data_unsubscribe` 操作管理数据通道的订阅生命周期
  4. 目标插件卸载时，对应的 IPC 订阅自动退订，不会产生僵尸订阅或内存泄漏
  5. 高频数据推送不会导致内存无限增长，有界 Channel 采用丢弃旧数据策略
**Plans**: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 11 → 12 → 13 → 14 → 15
（Phase 13 和 14 可并行，但对同一规划流程按序执行）

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 11. IPC 基础设施提取 | v1.1 | 3/3 | Complete ✅ | 2026-05-01 |
| 12. 插件重载操作 | v1.1 | 2/2 | Complete ✅ | 2026-05-01 |
| 13. 斜杠命令调度 | v1.1 | 2/2 | Complete ✅ | 2026-05-01 |
| 14. 安全 IPC 调用 | v1.1 | 0/2 | Planned | 2026-05-01 |
| 15. 数据回传 | v1.1 | 0/? | Not started | - |

---

*See `.planning/milestones/v1.0-ROADMAP.md` for completed milestone details.*