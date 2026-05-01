# Phase 11: IPC 基础设施提取 - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-05-01
**Phase:** 11-ipc-infra
**Areas discussed:** 接口放置位置, 网关范围

---

## 接口放置位置

### 放在哪个项目？

| Option | Description | Selected |
|--------|-------------|----------|
| DalamudMCP.Plugin 顶层 | 移出嵌套类，作为 Plugin 项目的顶层 public interface | ✓ |
| DalamudMCP.Framework | 放入纯抽象层，与 IOperationInvoker 同级 | |
| DalamudMCP.Protocol | 放入协议层，与 IPC 协议契约放在一起 | |

**User's choice:** DalamudMCP.Plugin 顶层
**Notes:** 所有后续操作都在 Plugin 内，无需跨项目引用

### 提取哪些类型？

| Option | Description | Selected |
|--------|-------------|----------|
| 接口 + 实现 + 枚举全提取 | 所有嵌套类型独立文件 | |
| 仅接口提取 | 只提取接口为 public 顶层 | |
| 接口 + 实现提取 | 接口 + 实现类独立文件，枚举和 Result 保留 | ✓ |

**User's choice:** 接口 + 实现提取（PluginIpcValueKind 和 UnsafeInvokePluginIpcResult 保留在原文件）

### 放在哪个子目录？

| Option | Description | Selected |
|--------|-------------|----------|
| 新建 Ipc/ 子目录 | src/DalamudMCP.Plugin/Ipc/ | ✓ |
| Hosting/ 子目录 | 与 DI 注册代码一起 | |
| Operations/ 子目录 | 与现有操作就近 | |

**User's choice:** 新建 Ipc/ 子目录

---

## 网关范围

### 网关是否包含 IExposedPlugin？

| Option | Description | Selected |
|--------|-------------|----------|
| 纯 IPC 网关 | 只负责 IPC CallGate，Phase 12 直接注入 IPluginFinder | ✓ |
| 扩展为统一网关 | 涵盖插件发现 + IPC 调用的统一门面 | |

**User's choice:** 纯 IPC 网关
**Notes:** Phase 12 的插件重载操作直接注入 IPluginFinder（Dalamud 原生接口），不做额外封装

### DI 注册方式？

| Option | Description | Selected |
|--------|-------------|----------|
| 手动注册 | PluginServiceCollectionExtensions 中 AddSingleton | ✓ |
| 源生成器自动注册 | 标记属性让生成器发现 | |

**User's choice:** 手动注册
**Notes:** 与现有 NamedPipeProtocolServer 等基础设施服务的注册方式保持一致

### 测试桩处理？

| Option | Description | Selected |
|--------|-------------|----------|
| 提取为公共测试桩 | FakeGateway/FakeSubscriber 移入测试项目复用 | ✓ |
| 保留在各测试类内 | 各自实现 mock | |
| 用 Moq/NSubstitute | 引入 mocking 框架 | |

**User's choice:** 提取为公共测试桩

### UnsafeInvokePluginIpcOperation 构造函数？

| Option | Description | Selected |
|--------|-------------|----------|
| public 改注入 IPluginIpcGateway | DI 注入网关，internal 测试构造不动 | ✓ |
| 统一构造函数 | 去掉 internal 构造，全部走 DI | |

**User's choice:** public 改注入 IPluginIpcGateway（internal 测试构造函数保持不变）

---

## Claude's Discretion

- 提取后各文件内部的导入语句、命名空间组织
- 测试桩的具体文件位置和命名

## Deferred Ideas

None — 讨论未超出阶段范围
