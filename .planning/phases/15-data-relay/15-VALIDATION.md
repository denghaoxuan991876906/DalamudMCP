---
status: pending
phase: 15-data-relay
plans: 2
date: 2026-05-02
nyquist_enabled: true
---

# Phase 15 Validation Strategy: 数据回传

> Nyquist 验证策略 — 按任务登记的测试映射与反馈延迟预算

## 测试框架

| 属性 | 值 |
|------|-----|
| 框架 | xunit.v3.mtp-v2 3.2.2 |
| 配置文件 | `tests/DalamudMCP.Plugin.Operations.Tests/DalamudMCP.Plugin.Operations.Tests.csproj` |
| 快速运行命令 | `dotnet test tests/DalamudMCP.Plugin.Operations.Tests --filter "FullyQualifiedName~PluginData"` |
| 完整套件命令 | `./build/test.ps1` |

## 阶段需求 → 测试映射

| 需求 ID | 行为 | 测试类型 | 自动化命令 | 文件存在？ |
|---------|------|---------|-----------|----------|
| RELAY-01 | 订阅通道：IPC Provider 注册 + Channel 创建 | 单元 | `dotnet test ... --filter "PluginDataSubscribe"` | ❌ Wave 0 |
| RELAY-01 | 退订通道：Provider 注销 + Channel 关闭 | 单元 | `dotnet test ... --filter "PluginDataUnsubscribe"` | ❌ Wave 0 |
| RELAY-01 | 轮询数据：非阻塞读取所有缓存条目 | 单元 | `dotnet test ... --filter "PluginDataPoll"` | ❌ Wave 0 |
| RELAY-01 | 数据推送：IPC Action 写入 Channel | 集成 | `dotnet test ... --filter "PluginDataRelayService"` | ❌ Wave 0 |
| RELAY-01 | 溢出丢弃：Channel 满时丢弃最旧数据 | 集成 | `dotnet test ... --filter "DropOldest"` | ❌ Wave 0 |
| RELAY-01 | 自动清理：插件卸载检测 + 自动退订 | 集成 | `dotnet test ... --filter "AutoCleanup"` | ❌ Wave 0 |

## 按 Wave 验证映射

### Wave 1 — Plan 15-01: PluginDataRelayService 基础设施

| 任务 | 验证方法 | 自动化 | 采样频率 |
|------|---------|--------|---------|
| Task 1: IPluginDataRelayService 接口 + RelayChannel | `dotnet build` 编译通过 | 每次提交 | 即时 |
| Task 2: PluginDataRelayService 实现（Channel + IPC Provider + 自动清理） | `dotnet build` 编译通过 | 每次提交 | 即时 |
| Task 3: DI 注册 PluginServiceCollectionExtensions | `dotnet build` 编译通过 | 每次提交 | 即时 |

### Wave 2 — Plan 15-02: 3 操作类 + 单元测试

| 任务 | 验证方法 | 自动化 | 采样频率 |
|------|---------|--------|---------|
| Task 1: 3 操作类 + 暴露策略 | `dotnet build` 编译通过 | 每次提交 | 即时 |
| Task 2: 21 个测试（Subscribe 8 + Unsubscribe 5 + Poll 8） | `dotnet test --filter "PluginData"` | 每次提交 | 即时 |

## 反馈延迟预算

| 违规类型 | 采样间隔 | 最大检测延迟 | 检测方式 |
|---------|---------|-------------|---------|
| 编译错误 | 每次 `dotnet build` | < 5 秒 | 构建失败 |
| 测试失败 | 每次 `dotnet test` | < 10 秒 | 测试套件 |
| 架构偏离 | 每个 Wave 结束时 | ~5 分钟 | 执行后审查 |
| 威胁模型差距 | 阶段完成时 | ~30 分钟 | SECURITY.md 审计 |

## 采样率

- **每个任务提交：** `dotnet test tests/DalamudMCP.Plugin.Operations.Tests --filter "PluginData" --no-restore`
- **每个 Wave 合并：** `./build/test.ps1`
- **阶段关卡：** 完整套件通过后方可执行 `/gsd-verify-work`

## Wave 0 差距（待修复）

- [ ] `tests/.../PluginDataSubscribeOperationTests.cs` — 覆盖 RELAY-01 订阅路径
- [ ] `tests/.../PluginDataUnsubscribeOperationTests.cs` — 覆盖 RELAY-01 退订路径
- [ ] `tests/.../PluginDataPollOperationTests.cs` — 覆盖 RELAY-01 轮询路径
- [ ] `tests/.../TestShared/Relay/FakePluginDataRelayService.cs` — 共享测试桩
- [ ] `tests/.../PluginDataRelayServiceTests.cs` — 覆盖服务集成场景（可选，可在 Wave 1）

## 阶段关卡检查单

- [ ] 所有 PLAN.md 任务 `acceptance_criteria` 通过
- [ ] `dotnet build` 零错误
- [ ] 全部 21+ 测试通过
- [ ] RELAY-01 所有测试映射创建完毕
- [ ] `./build/test.ps1` 完整套件通过
- [ ] 威胁模型中所有 STRIDE 威胁已缓解或接受

---

*验证策略创建日期：2026-05-02*
*基于：.planning/phases/15-data-relay/15-RESEARCH.md §验证架构*
