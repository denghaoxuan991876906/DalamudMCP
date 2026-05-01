# Phase 13: 斜杠命令调度 - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-05-01
**Phase:** 13-slash-command
**Areas discussed:** 输入验证规则

---

## 输入验证规则

| Option | Description | Selected |
|--------|-------------|----------|
| 宽松验证 | `/` 前缀 + 512 字符 + 滤除控制字符。让 ICommandManager 自然拒绝无效命令。 | ✓（修改版）|
| 严格验证 | `/` 前缀 + 256 字符 + 白名单字符集 + 过滤 shell 元字符。 | |
| 最小验证 | 仅 `/` 前缀 + null 字节过滤。 | |

**User's choice:** 宽松验证方向 — `/` 前缀检查 + 256 字符上限 + **不滤除任何特殊字符**

### 验证细则

| Option | Description | Selected |
|--------|-------------|----------|
| 控制字符 + null 字节 | 过滤 \0 和 C0 控制字符。长度 512。 | |
| 控制字符 + 换行 | 过滤 null/控制字符 + \r\n。长度 512。 | |
| 全部滤除 | null + 控制字符 + 换行。长度 256。 | |
| 不用过滤 | 不滤除任何特殊字符。 | ✓ |

**User's choice:** 不滤除特殊字符。保留简单验证。

### 长度限制

| Option | Description | Selected |
|--------|-------------|----------|
| 512 字符 | 覆盖面广 | |
| 1024 字符 | 最宽松 | |
| 256 字符 | 保守，大多数命令不超此长度 | ✓ |

**User's choice:** 256 字符

---

## Claude's Discretion

以下领域用户选择跳过讨论，由下游代理决定：

- **响应模型设计** — Fire-and-forget 模式下的状态码与响应结构
- **游戏原生命令策略** — 如何检测/处理非 Dalamud 注册命令
- **暴露策略** — unsafe 操作归类

## Deferred Ideas

无 — 讨论未超出阶段范围.
