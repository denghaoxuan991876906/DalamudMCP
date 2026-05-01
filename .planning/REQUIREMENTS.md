# Requirements: DalamudMCP

**Defined:** 2026-05-01
**Core Value:** AI 客户端能够以结构化的方式与 FFXIV 游戏及其他 Dalamud 插件交互，实现自动化测试

## v1.1 Requirements

### 插件重载

- [x] **RELOAD-01**: AI 能够通过 MCP 指定插件内部名称触发该插件的重载（unload → reload）

### 跨插件 IPC 调用

- [ ] **IPC-01**: AI 能够通过 MCP 调用目标插件暴露的 IPC 函数（指定插件名 + 方法名 + 参数），并获取返回值

### 数据回传

- [ ] **RELAY-01**: 目标插件能够通过 Dalamud IPC 向 DalamudMCP 发送结构化数据，DalamudMCP 缓存数据供 AI 通过 MCP 操作轮询获取

### 斜杠命令调度

- [ ] **SLASH-01**: AI 能够通过 MCP 发送游戏内斜杠命令（如 `/xlreload`、`/ping` 等 Dalamud 注册命令）

## v2 Requirements

### 插件重载增强

- **RELOAD-02**: 重载操作返回详细结果（成功/失败/超时）
- **RELOAD-03**: 重载后自动诊断 IPC 通道是否恢复可用

### IPC 增强能力

- **IPC-02**: AI 能够订阅目标插件的 IPC 事件，接收异步推送通知
- **RELAY-02**: 数据回传支持推送模式（MCP 主动通知 AI，非轮询）
- **RELAY-03**: 被测插件自动发现（列出已安装插件及其暴露的 IPC 接口）

### 测试增强

- **TEST-01**: 批量测试场景执行（一次性发送多步骤，插件按序执行）

## Out of Scope

| Feature | Reason |
|---------|--------|
| SDK/NuGet 包给被测插件引用 | 降低接入门槛只需实现 IPC 接口约定，不需要额外依赖 |
| Dalamud IPC 弱类型安全封装 | 现有 UnsafeInvokePluginIpcOperation 已通过反射解决，不重复封装 |
| 游戏原生聊天命令发送 | ICommandManager 仅能派发 Dalamud 注册命令，游戏原生命令需内存注入，超出范围 |
| 插件自动发现 | 需要更复杂的插件元数据枚举，推迟到 v2 |
| 批量测试场景 | 需要状态机设计，v1.1 仅支持单步交互式 |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| RELOAD-01 | Phase 12 | ✅ Complete |
| IPC-01 | Phase 14 | Pending |
| RELAY-01 | Phase 15 | Pending |
| SLASH-01 | Phase 13 | Pending |

**Coverage:**
- v1.1 requirements: 4 total
- Mapped to phases: 4 ✓
- Unmapped: 0 ✓

**Infrastructure dependency:** Phase 11 (IPC 基础设施提取) 不直接映射需求，但为 Phase 12/14/15 提供共享 IPC 网关服务支撑。

---
*Requirements defined: 2026-05-01*
*Last updated: 2026-05-01 after roadmap creation*