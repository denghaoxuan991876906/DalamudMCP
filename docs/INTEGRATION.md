# DalamudMCP 接入文档

## 架构概览

```
AI 客户端 (Claude Desktop 等)
    │  MCP over HTTP (SSE) 或 stdio
    ▼
DalamudMCP.Cli ──── HTTP MCP 端点 ──── 或 stdio MCP
    │
    │  命名管道 IPC (MemoryPack 二进制协议 v2.0.0)
    │  自动发现: %APPDATA%\XIVLauncher\pluginConfigs\DalamudMCP\active-instance.json
    │
    ▼
DalamudMCP.Plugin ──── 在 FFXIV 进程内运行
    │
    │  通过 Dalamud API 读取游戏状态
    ▼
FFXIV 游戏数据 (玩家、背包、任务、UI 组件等)
```

- **插件** (`DalamudMCP.Plugin`): 在 FFXIV 进程中运行，直接读取游戏状态。暴露命名管道供外部连接。
- **CLI** (`DalamudMCP.Cli`): 独立进程，通过命名管道连接插件，对外提供 CLI / stdio MCP / HTTP MCP 三种模式。
- **协议**: 命名管道上使用 MemoryPack 二进制序列化（协议版本 `2.0.0`），HTTP 端点上使用 MCP 标准 JSON-RPC over SSE。

## 快速接入

### 前置条件

- FFXIV 已启动，Dalamud 运行中
- DalamudMCP 插件已加载（API Level 15）
- 插件配置窗口中"启用 CLI/MCP 动作操作"已勾选（如需使用写入类工具）

### 方式一：HTTP MCP 端点（推荐）

插件加载后自动启动 HTTP MCP 服务器（如在配置中勾选了自动启动），或手动在配置窗口点击 **"启动 MCP HTTP 服务器"**。

默认端点：`http://127.0.0.1:38473/mcp`

#### Claude Desktop 配置

在 Claude Desktop 的 `mcpServers` 配置中添加：

```json
{
  "mcpServers": {
    "dalamudmcp": {
      "type": "sse",
      "url": "http://127.0.0.1:38473/mcp"
    }
  }
}
```

配置文件位置：
- Windows: `%APPDATA%\Claude\claude_desktop_config.json`
- macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`

#### 任意 MCP 客户端

支持 MCP over SSE (Server-Sent Events) 的客户端可直接使用 `http://127.0.0.1:38473/mcp` 作为端点 URL。

### 方式二：stdio MCP

```powershell
dotnet run --project src/DalamudMCP.Cli -- serve mcp
```

CLI 进程通过 `active-instance.json` 自动发现插件管道，然后以 stdio MCP 模式运行。适用于作为子进程被 MCP 客户端拉起。

### 方式三：CLI 直连

```powershell
# 自动发现（需要 discovery 文件存在）
dotnet run --project src/DalamudMCP.Cli -- player context

# 指定管道名
dotnet run --project src/DalamudMCP.Cli -- --pipe <管道名> player context
```

注意：CLI 直连可能因插件内部 MCP 子进程独占管道而超时，此时请使用 HTTP 端点方式。

## MCP 工具列表

### 观察类工具（默认启用）

| 工具名 | 描述 | 参数 |
|--------|------|------|
| `get_session_status` | 获取当前会话状态 | 无 |
| `get_player_context` | 获取当前玩家上下文（角色名、职业、等级、位置） | 无 |
| `get_inventory_summary` | 获取背包摘要 | 无 |
| `get_duty_context` | 获取当前副本信息 | 无 |
| `get_fate_context` | 获取附近 FATE 信息 | `max-distance`(可选), `name-contains`(可选) |
| `get_addon_list` | 获取已加载的 UI 组件列表 | 无 |
| `get_addon_tree` | 获取指定 UI 组件的节点树 | `addon`(必填) |
| `get_addon_strings` | 获取指定 UI 组件的字符串表 | `addon`(必填) |
| `get_nearby_interactables` | 获取附近可交互对象 | `max-distance`(可选), `name-contains`(可选), `include-players`(可选) |
| `get_quest_status` | 获取任务状态 | `quest-id`(可选), `query`(可选), `max-results`(可选) |
| `get_available_quests` | 获取当前区域可见的未接任务 | `name-contains`(可选), `max-results`(可选) |
| `get_current_quest_objective` | 获取当前追踪的任务目标 | 无 |
| `capture_game_screenshot` | 截取游戏画面 | `capture-area`(可选: client/window) |

### 动作类工具（需在插件配置中启用）

| 工具名 | 描述 | 参数 |
|--------|------|------|
| `target_object` | 选中指定实体 | `game-object-id`(必填) |
| `interact_with_target` | 与当前目标交互 | `expected-game-object-id`(可选), `check-line-of-sight`(可选) |
| `move_to_entity` | 移动到指定实体 | `game-object-id`(必填), `allow-flight`(可选) |
| `move_to_nearby_interactable` | 移动到附近可交互对象 | `name-contains`(必填), `max-distance`(可选), `allow-flight`(可选), `include-players`(可选) |
| `teleport_to_aetheryte` | 传送到已解锁的以太之光 | `query`(必填) |
| `use_duty_action` | 使用副本技能 | `slot`(必填) |
| `send_addon_input` | 向 UI 组件发送底层输入 | `addon`(必填), `input-type`(必填), `input-id`(必填), `auxiliary-state`(可选), `input-state`(可选) |
| `send_addon_event` | 向 UI 组件发送事件 | `addon`(必填), `event-type`(必填), `event-param`(可选), `collision-index`(可选), `node-id`(可选) |
| `send_addon_callback_values` | 向 UI 组件发送回调值 | `addon`(必填), `values`(必填: 整数数组) |
| `select_addon_menu_item` | 选择 UI 菜单项 | `addon`(必填), `label`(必填), `contains-match`(可选) |

### 开发者工具（独立开关）

| 工具名 | 描述 | 参数 |
|--------|------|------|
| `unsafe_invoke_plugin_ipc` | 调用任意 Dalamud 插件 IPC | `callgate`(必填), `result-kind`(必填), `argument-kinds`(可选), `arguments-json`(可选), `run-on-framework-thread`(可选) |

## 响应数据格式

### MCP 工具响应结构

所有工具通过 MCP `tools/call` 方法调用，返回统一结构：

```json
{
  "result": {
    "content": [
      {
        "type": "text",
        "text": "<人类可读摘要>"
      }
    ],
    "structuredContent": {
      "<字段>": "<值>"
    },
    "isError": false
  }
}
```

- `content[].text`: 人类可读的文本摘要，始终存在
- `structuredContent`: 结构化的机器可读数据，包含所有字段，可用作后续处理的输入
- `isError`: 操作是否失败

### 常用响应示例

#### get_player_context

```json
{
  "structuredContent": {
    "characterName": "角色名",
    "homeWorld": "服务器名",
    "jobName": "Black Mage",
    "jobLevel": 100,
    "territoryName": "Territory#1162",
    "position": {
      "x": 31.5,
      "y": 56.5,
      "z": 481.1
    }
  }
}
```

#### get_addon_list

```json
{
  "structuredContent": [
    {
      "addonName": "Inventory",
      "isReady": true,
      "isVisible": false,
      "capturedAt": "2026-04-30T15:40:30.8797292+00:00",
      "summaryText": "Inventory is open and hidden."
    }
  ]
}
```

#### get_inventory_summary

```json
{
  "structuredContent": {
    "itemCount": 140,
    "slotsUsed": 95,
    "slotsFree": 45,
    "gil": 1234567,
    "items": [
      {
        "name": "恢复药",
        "count": 10,
        "slot": 0,
        "isHq": false
      }
    ]
  }
}
```

### 错误响应

```json
{
  "error": {
    "code": -32601,
    "message": "Method 'xxx' is not available."
  }
}
```

常见错误码：
- `-32601`: 方法不存在（工具名拼写错误或工具未启用）
- `-32602`: 参数无效（缺少必填参数或参数类型错误）
- `-32000`: 游戏内操作执行失败

## 安全模型

### 工具暴露等级

| 等级 | 涵盖工具 | 默认状态 | 如何启用 |
|------|----------|----------|----------|
| 观察 (Observe) | get_* 系列 | **启用** | 无需操作 |
| 动作 (Action) | target, move, teleport, send_* 等 | **禁用** | 插件配置中勾选"启用 CLI/MCP 动作操作" |
| 非安全 (Unsafe) | unsafe_invoke_plugin_ipc | **禁用** | 插件配置中勾选"启用非安全集成工具" |

### 网络绑定

HTTP 服务器仅绑定 `127.0.0.1`（本地回环），不接受外部网络连接。确保：
- 不要将端口转发到公网
- 在有其他用户访问的机器上运行时注意 MCP 端点可被本机其他进程访问

## 协议细节

### 命名管道通信（插件内部）

插件与 CLI 之间使用 Windows 命名管道通信，协议格式：

```
帧头 (4字节 Big-Endian 长度) + MemoryPack 序列化的 ProtocolRequestEnvelope/ProtocolResponseEnvelope
```

信封结构：

```csharp
// 请求
ProtocolRequestEnvelope {
    ContractVersion: "2.0.0",
    RequestType: string,     // 操作类型标识
    RequestId: string,       // 请求追踪 ID
    PayloadFormat: enum,     // None=0, Json=1, MemoryPack=2
    PreferredResponseFormat: enum,
    Payload: byte[]          // 序列化的请求数据
}

// 响应
ProtocolResponseEnvelope {
    ContractVersion: "2.0.0",
    RequestId: string,
    Success: bool,
    ErrorCode: string?,      // 失败时的错误码
    ErrorMessage: string?,   // 失败时的错误描述
    PayloadFormat: enum,
    Payload: byte[],         // 序列化的响应数据
    DisplayText: string?     // 可选的显示文本
}
```

### 管道自动发现

插件启动时将连接信息写入：

```
%APPDATA%\XIVLauncher\pluginConfigs\DalamudMCP\active-instance.json
```

格式：
```json
{
  "PipeName": "DalamudMCP.25028.f9517ffc",
  "ProcessId": 25028,
  "UpdatedAtUtc": "2026-04-30T15:00:00Z"
}
```

CLI 读取此文件自动获取管道名。也可通过 `--pipe` 参数或 `DALAMUD_MCP_PIPE` 环境变量手动指定。

## 配置选项

| 配置项 | 位置 | 说明 |
|--------|------|------|
| 管道名 | 插件配置 → 高级详情 | 当前命名管道名称 |
| HTTP 端点 | 插件配置 → HTTP 服务器 | MCP HTTP 端点 URL |
| 动作工具开关 | 插件配置 → 运行时 | 启用/禁用写入类操作 |
| 非安全工具开关 | 插件配置 → 运行时 | 启用/禁用 unsafe 操作 |
| 自动启动 HTTP | 插件配置 → HTTP 服务器 | 插件加载时自动启动 HTTP 服务器 |
| HTTP 端口 | CLI 参数 `--port` | 默认 38473 |

## 依赖

- FFXIV Patch 7.5+
- Dalamud API Level 15
- .NET 10.0
- Windows 10/11
