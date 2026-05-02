# Phase 10: 添加日志读取能力 — Research

**Researched:** 2026-05-01
**Domain:** Dalamud Chat/Log API, Real-time Event Capture, Chat Data Modeling
**Confidence:** HIGH

## Summary

本阶段的目标是通过 MCP 向 AI 客户端暴露 FFXIV 的游戏日志（聊天日志/战斗日志/系统日志）。核心挑战在于：现有 IOperation 模式全部是**拉取式（pull-based）** 的请求-响应模式，而日志是**事件驱动（event-based）** 的数据流。需要在不大幅改造框架的前提下，找到最佳的混合架构。

**推荐方案：混合架构（Hybrid Pull + Event Buffer）**。创建一个独立的 `LogBufferService` 订阅 `IChatGui.ChatMessage` 事件，线程安全地缓冲最近的日志条目，然后通过一个标准的 `IOperation<ChatLogRequest, ChatLogSnapshot>` 操作供 MCP/CLI 拉取查询。

**本次研究的三个定位：**
1. 聊天日志（Chat）— 通过 `IChatGui.ChatMessage` 事件，支持 `XivChatType` 频道过滤
2. 战斗日志（Battle）— FFXIV 没有独立战斗日志 API；战斗相关消息以部分 `XivChatType` 值的形式出现在聊天中，"战斗日志"应限定于这些频道
3. 系统日志（System）— `XivChatType.System` 等频道的内容

**完全战斗解析（damage/healing numbers, ACT-style parsing）超出本阶段范围**，涉及网络包捕获或内存扫描，与现有架构不兼容。

**Primary recommendation:** 创建 ChatLogBufferService（事件订阅+线程安全缓冲区）+ ChatLogReadOperation（标准 IOperation 拉取）的混合方案。

<user_constraints>
## User Constraints (from CONTEXT.md)

**No CONTEXT.md found for this phase.** This is the initial research pass. No locked decisions exist yet. All findings are recommendations subject to discussion.

### Phase Success Criteria (from ROADMAP)
1. 插件订阅 Dalamud 日志事件（IChatGui.ChatMessage 或 LogMessage），可读取运行时日志
2. 新增 MCP 观察工具（如 `get_chat_log`），支持按频道、时间范围过滤
3. AI 客户端可通过 MCP 实时获取聊天/战斗/系统日志的结构化数据
4. CLI 模式支持通过命令行查询日志（直接 CLI / stdio MCP / HTTP MCP）
</user_constraints>

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Chat event subscription | Plugin (Dalamud) | — | IChatGui 仅在 Dalamud 运行时可用 |
| Log buffering | Plugin (Dalamud) | — | 缓冲区必须在游戏进程内运行 |
| Log query (pull) | Plugin (Operation) | CLI (delegation) | 标准 IOperation 模式，CLI 通过 IPC 委托 |
| Log formatting | Plugin (TextFormatter) | — | 遵循现有 IResultFormatter 模式 |
| Battle/combat log | — | — | 不在 IChatGui 范围内，需明确界定 |

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Dalamud.Plugin.Services.IChatGui | API 15 | 聊天事件订阅 | 官方 Dalamud API，游戏聊天数据的唯一来源 |
| System.Collections.Concurrent | .NET 10 | 线程安全缓冲区 | 框架内置，无需额外依赖 |
| MemoryPack | 1.21.4 | 序列化快照 | 项目已有依赖 |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Dalamud.Game.Text.XivChatType | API 15 | 聊天频道枚举 | 所有频道过滤场景 |
| Dalamud.Game.Text.XivChatRelationKind | API 15 | 发送方/接收方关系 | API 15 新增，需要时用于精细化过滤 |
| Dalamud.Game.Text.SeStringHandling | API 15 | 聊天文本处理 | 从 SeString 提取纯文本 |

**Installation:**
无需新增 NuGet 包。IChatGui 由 Dalamud SDK 提供，ConcurrentQueue 是 .NET 内置。

**Version verification:** IChatGui 是 Dalamud API 15（Dalamud.NET.Sdk/15.0.0）的一部分，由 `DALAMUD_HOME` 指向的引用程序集提供。ConcurrentQueue 是 `System.Collections.Concurrent` 的一部分，随 .NET 10 运行时提供。

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| IChatGui.ChatMessage | IGameNetwork 网络包捕获 | 网络包方式可捕获完整战斗日志，但需要理解 FFXIV 协议格式，复杂度极高且不兼容现有架构 |
| ConcurrentQueue 环形缓冲区 | Channel<T> / 数据库存储 | Channel 适合流式场景但需要 MCP 流支持（当前无此基础设施）；数据库存储过于重量级 |
| 独立 LogBufferService | 在操作内部直接订阅 | 分离关注点：缓冲区管理无需理解 IOperation 契约；测试性更好；可复用 |

## Architecture Patterns

### System Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│  Dalamud Game Process                                                │
│                                                                      │
│  ┌──────────────────────┐    IChatGui.ChatMessage event              │
│  │  FFXIV Game Client   │ ──────────────────────────────────────┐   │
│  │  (chat messages,      │                                       │   │
│  │   battle announcements,│                                       │   │
│  │   system messages)    │                                       ▼   │
│  └──────────────────────┘                             ┌─────────────────┐
│                                                       │ LogBufferService │
│  ┌──────────────────┐                                 │ (Singleton)     │
│  │  PluginEntryPoint │── 注入 IChatGui ─────────────▶ │                 │
│  │  (DI container)   │                                │ OnChatMessage() │
│  └──────────────────┘                                 │ OnLogMessage()  │
│         │                                             │                 │
│         │ services.AddSingleton(chatGui)              │ buffer:         │
│         ▼                                             │ ConcurrentQueue │
│  ┌──────────────────┐                                 │ GetRecent(...)  │
│  │ ChatLogReadOp    │── 注入 LogBufferService ──────▶ └─────────────────┘
│  │ (IOperation)     │                                        ▲
│  │ get_chat_log     │                                        │
│  └───────┬──────────┘                                        │
│          │                                                    │
│          │ ExecuteAsync(ChatLogRequest)                       │
│          │ returns ChatLogSnapshot                            │
│          ▼                                                    │
│  ┌──────────────────────────────────────────────────────┐     │
│  │              OperationProtocolDispatcher              │     │
│  │  routes by operationId → calls operation.ExecuteAsync │     │
│  └──────────────────────┬───────────────────────────────┘     │
│                         │                                     │
│                    ┌────┴────┐                                │
│                    │         │                                │
│                    ▼         ▼                                │
│  ┌──────────────┐  ┌──────────────┐                          │
│  │ CLI (stdio)  │  │ HTTP MCP     │                          │
│  │ chat read    │  │ get_chat_log │                          │
│  │ --channel    │  │ {channel:}   │                          │
│  │ --max-count  │  │ {since:}     │                          │
│  └──────────────┘  └──────────────┘                          │
└──────────────────────────────────────────────────────────────┘
```

**Data Flow:**
1. FFXIV 游戏客户端产生聊天消息 → 触发 IChatGui.ChatMessage 事件
2. LogBufferService.OnChatMessage 接收事件参数，构造 LogEntry 记录，推入 ConcurrentQueue 缓冲区
3. AI 客户端调用 `get_chat_log` MCP 工具（或 CLI `chat read` 命令）
4. ChatLogReadOperation.ExecuteAsync 被调用，从 LogBufferService 查询符合条件的条目
5. 格式化为 ChatLogSnapshot 返回给客户端

**Event subscriber dependency injection path:**
PluginEntryPoint → PluginCompositionRoot.CreateFromDalamud(..., IChatGui chatGui) → PluginServiceCollectionExtensions.BuildDalamudServiceProvider(..., chatGui) → services.AddSingleton(chatGui)

### Recommended Project Structure

```
src/DalamudMCP.Plugin/
├── Readers/
│   ├── IPluginReaderStatus.cs         (existing)
│   └── ...                            (existing)
├── Operations/
│   ├── ChatLogReadOperation.cs        (NEW - log query operation)
│   └── ...                            (existing)
├── Services/
│   └── ChatLogBufferService.cs        (NEW - event subscriber + buffer)
├── ...
```

**新增文件:** 2 个
- `Services/ChatLogBufferService.cs` — 事件订阅、线程安全缓冲区、查询接口
- `Operations/ChatLogReadOperation.cs` — IOperation 实现

**修改文件:** 3 个
- `PluginCompositionRoot.cs` — 新增 IChatGui 参数
- `PluginServiceCollectionExtensions.cs` — 注册 IChatGui 和 ChatLogBufferService
- `PluginEntryPoint.cs` — 新增 IChatGui 参数

### Pattern 1: Hybrid Event Buffer + Pull Operation

**What:** 用事件驱动方式持续收集日志数据存储在内存缓冲区中，同时通过标准拉取操作提供查询接口。

**When to use:** 当需要 "持续观察" 的数据以事件方式产生，但基础设施仅支持请求-响应模式时。

**Rationale:**
- 当前 MCP 基础设施仅支持 request-response（`IOperation<TRequest, TResult>`）
- 没有 MCP 流式/通知基础设施（ModelContextProtocol 1.1.0 支持 streaming，但该项目未使用）
- 缓冲区方案可以在不改造框架的前提下满足需求
- 缓冲区大小可控（默认 1000 条），内存开销可忽略

**Example (ChatLogBufferService):**

```csharp
// Source: synthesized from codebase patterns + Dalamud IChatGui API 15 [ASSUMED]
using System.Collections.Concurrent;
using Dalamud.Plugin.Services;
using Dalamud.Game.Text;

namespace DalamudMCP.Plugin.Services;

public sealed class ChatLogBufferService : IDisposable
{
    private const int DefaultCapacity = 1000;
    
    private readonly IChatGui chatGui;
    private readonly ConcurrentQueue<ChatLogEntry> buffer = new();
    private readonly int capacity;
    private volatile bool disposed;

    public ChatLogBufferService(IChatGui chatGui, int capacity = DefaultCapacity)
    {
        this.chatGui = chatGui ?? throw new ArgumentNullException(nameof(chatGui));
        this.capacity = capacity > 0 ? capacity : DefaultCapacity;
        
        // Subscribe to chat message events
        chatGui.ChatMessage += OnChatMessage;
        // Optionally subscribe to LogMessage for additional coverage
        // chatGui.LogMessage += OnLogMessage;
    }

    public IReadOnlyList<ChatLogEntry> GetRecent(
        XivChatType[]? channels = null,
        DateTimeOffset? since = null,
        int maxCount = 100)
    {
        // Reverse iterate the buffer (newest first)
        // Apply filters:
        //   - channel filter: if channels is not null/empty, match entry.Type
        //   - time filter: if since is set, match entry.Timestamp >= since
        //   - limit: take at most maxCount entries
    }

    private void OnChatMessage(
        XivChatType type,
        uint senderId,
        ref DalamudLinkPayload? sender,
        ref DalamudLinkPayload? originalSender,
        ReadOnlySpan<XivChatEntry> message,
        ref bool isHandled,
        XivChatRelationKind sourceKind,
        XivChatRelationKind targetKind)
    {
        // This is the API 15 signature [ASSUMED - verify against reference assemblies]
        // Extract message text from ReadOnlySpan<XivChatEntry>
        // Create ChatLogEntry and enqueue
        // Trim buffer if over capacity
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        chatGui.ChatMessage -= OnChatMessage;
    }
}
```

### Pattern 2: Dual-Constructor Operation (standard pattern)

**What:** 每个 Operation 对外暴露一个注入真实 Dalamud 服务的构造函数，对内暴露一个注入 mock executor 的 internal 构造函数，用于测试。

**When to use:** 所有 IOperation 实现都应遵循此模式。

**Example (ChatLogReadOperation):**

```csharp
// Source: synthesized from NearbyInteractablesOperation pattern [VERIFIED: codebase]
[Operation("chat.read", Description = "Reads recent chat/combat/system log entries.")]
[ResultFormatter(typeof(ChatLogReadOperation.TextFormatter))]
[CliCommand("chat", "read")]
[McpTool("get_chat_log")]
public sealed partial class ChatLogReadOperation
    : IOperation<ChatLogReadOperation.Request, ChatLogSnapshot>, IPluginReaderStatus
{
    private readonly ChatLogBufferService logBuffer;
    private readonly Func<Request, CancellationToken, ValueTask<ChatLogSnapshot>> executor;

    [SupportedOSPlatform("windows")]
    public ChatLogReadOperation(ChatLogBufferService logBuffer)
    {
        ArgumentNullException.ThrowIfNull(logBuffer);
        this.logBuffer = logBuffer;
        executor = CreateExecutor(logBuffer);
        isReadyProvider = () => true; // always ready if buffer exists
    }

    internal ChatLogReadOperation(
        Func<Request, CancellationToken, ValueTask<ChatLogSnapshot>> executor,
        bool isReady = true,
        string detail = "ready") { /* test constructor */ }

    // Request with filter options
    [MemoryPackable]
    [ProtocolOperation("chat.read")]
    [LegacyBridgeRequest("ReadChatLog")]
    public sealed partial class Request
    {
        [Option("channels", Description = "Chat channels to filter by (e.g., Say,Party,System). Empty = all.", Required = false)]
        public string[]? Channels { get; init; }

        [Option("since", Description = "Only return entries after this UTC timestamp (ISO 8601).", Required = false)]
        public DateTimeOffset? Since { get; init; }

        [Option("max-count", Description = "Maximum number of entries to return.", Required = false)]
        public int? MaxCount { get; init; }
    }

    // TextFormatter, TextFormatter, and snapshot types follow standard pattern
    // ...
}
```

### Anti-Patterns to Avoid

- **直接在 Operation 构造函数中订阅事件：** Operation 被 Source Generator 注册为 DI 单例，但生命周期管理不清晰。应使用专用的 Service 管理事件生命周期。
- **阻塞框架线程：** ChatMessage 事件在框架线程上触发，事件处理器应快速完成（入队操作），不要在事件处理器中做耗时操作。
- **不释放事件订阅：** 必须实现 IDisposable 并在 Dispose 中取消事件订阅，否则会导致内存泄漏和崩溃。
- **假设 API 14 签名：** 项目已在 API 15，ChatMessage 委托签名已更改（新增 sourceKind/targetKind 参数）。必须使用 API 15 的签名。

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| 线程安全队列 | 自己实现锁 + List | `System.Collections.Concurrent.ConcurrentQueue<T>` | ConcurrentQueue 是无锁的，在高频事件下性能更好 |
| 聊天文本解析 | 手动解析 SeString payload | `SeString.TextValue` 属性 | Dalamud 已提供纯文本提取 |
| 时间戳 | DateTimeOffset.UtcNow | 收到事件时记录 | 足够精确，无需依赖服务器时间 |
| MCP/CLI 注册 | 手动注册到路由表 | `[McpTool]` / `[CliCommand]` 属性 + Source Generator | 自动发现和注册 |

**Key insight:** 日志读取的核心复杂性不在技术实现，而在于**事件模型与请求-响应模型的桥接**。缓冲区方案是经过验证的成熟模式，不应试图用 MCP 流式通知（该项目尚未实现）来解决。

## Common Pitfalls

### Pitfall 1: API 15 ChatMessage 签名变更
**What goes wrong:** 使用 API 14 的 `OnMessageDelegate` 签名（`(XivChatType, uint, ref SeString, ref SeString, ref bool)`）会导致编译错误或运行时类型不匹配。
**Why it happens:** Dalamud API 15 将原先打包在 XivChatType 中的关系数据拆分为独立的 `sourceKind` 和 `targetKind` 参数。委托签名已变更。
**How to avoid:** 在开发前确认 DALAMUD_HOME 指向 API 15 运行时，使用 API 15 的签名进行订阅。`[CITED: https://dalamud.dev/versions/v15/]`
**Warning signs:** 编译错误提示 OnMessageDelegate 参数数量不匹配；运行时 TypeLoadException。

### Pitfall 2: ConcurrentQueue 不能直接按需查询
**What goes wrong:** `ConcurrentQueue<T>` 只支持先进先出迭代，不支持随机访问或按条件过滤。
**Why it happens:** ConcurrentQueue 被设计为生产者-消费者队列，不是查询优化的数据结构。
**How to avoid:** 在查询时对整个缓冲区执行一次快照（`.ToArray()`），然后在快照上应用 LINQ 过滤。快照操作是 O(n)，在 1000 条缓冲区下性能可以接受。
**Warning signs:** 尝试使用 `buffer.FirstOrDefault(predicate)` 在等待操作中多次调用会导致性能问题。

### Pitfall 3: Event Handler 在错误线程上操作 UI 或 Dalamud 状态
**What goes wrong:** `ChatMessage` 事件在框架线程上触发。在事件处理器中直接操作 Dalamud 游戏状态是安全的，但如果操作了非线程安全的代码可能引发问题。
**Why it happens:** 事件处理器在框架线程上下文中运行。
**How to avoid:** 事件处理器只做入队操作（ConcurrentQueue.Enqueue），不调用任何 Dalamud 服务方法。查询操作使用现有的 `RunOnFrameworkThread` 模式。
**Warning signs:** 随机崩溃、AccessViolationException、死锁。

### Pitfall 4: 缓冲区无限增长
**What goes wrong:** 如果不对缓冲区大小做限制，长时间运行的插件会消耗越来越多的内存。
**Why it happens:** 每个 ChatMessage 事件都会创建一个新条目加入队列。
**How to avoid:** 设置硬性容量上限（如 1000 条），每次入队后检查队列大小，超出时批量移除旧条目。考虑使用 `CircularBuffer<T>` 模式或用 `Channel<T>` 的 Bounded 模式。
**Warning signs:** 插件进程内存随运行时间线性增长。

### Pitfall 5: SeString.TextValue 可能因 payload 类型而为空或乱码
**What goes wrong:** 某些消息（如系统消息、包含链接或嵌入数据的消息）的 `.TextValue` 可能返回空字符串或包含不可读的 payload 标记。
**Why it happens:** SeString 可以包含多种 payload 类型（链接、图标、颜色等），.TextValue 提取纯文本时可能丢失部分内容。
**How to avoid:** 总是对 `TextValue` 做 null/空检查，提供备用方案（如提取原始 payload 的描述或使用 `.ToJsonString()` 用于调试）。
**Warning signs:** 日志条目中 `Message` 字段为空或仅包含特殊字符。

## Code Examples

### 完整 ChatLogBufferService 骨架

```csharp
// Source: synthesized from existing patterns + IChatGui API 15 [ASSUMED]
using System.Collections.Concurrent;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace DalamudMCP.Plugin.Services;

public sealed class ChatLogBufferService : IDisposable
{
    private readonly IChatGui chatGui;
    private readonly ConcurrentQueue<ChatLogEntry> entries = new();
    private readonly int maxCapacity;
    private volatile bool disposed;
    private long totalEnqueued;

    public ChatLogBufferService(IChatGui chatGui, int maxCapacity = 1000)
    {
        this.chatGui = chatGui ?? throw new ArgumentNullException(nameof(chatGui));
        this.maxCapacity = maxCapacity > 0 ? maxCapacity : 1000;
        chatGui.ChatMessage += OnChatMessage;
    }

    public IReadOnlyList<ChatLogEntry> GetRecent(
        XivChatType[]? channels = null,
        DateTimeOffset? since = null,
        int maxCount = 100)
    {
        if (maxCount <= 0) maxCount = 100;

        // Snapshot the buffer for thread-safe iteration
        ChatLogEntry[] snapshot = entries.ToArray();
        
        IEnumerable<ChatLogEntry> query = snapshot.AsEnumerable();

        if (channels is { Length: > 0 })
        {
            HashSet<XivChatType> channelSet = new(channels);
            query = query.Where(e => channelSet.Contains(e.Type));
        }

        if (since.HasValue)
            query = query.Where(e => e.Timestamp >= since.Value);

        return query
            .OrderByDescending(static e => e.Timestamp)
            .Take(maxCount)
            .ToArray();
    }

    private void OnChatMessage(
        XivChatType type,
        uint senderId,
        ref DalamudLinkPayload? sender,
        ref DalamudLinkPayload? originalSender,
        ReadOnlySpan<XivChatEntry> message,
        ref bool isHandled,
        XivChatRelationKind sourceKind,
        XivChatRelationKind targetKind)
    {
        // API 15 signature [ASSUMED - verify against reference assemblies]
        string? senderName = sender?.Encode() ?? null; // or extract from SeString
        string? messageText = ExtractPlainText(message);
        
        var entry = new ChatLogEntry(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            type,
            type.ToString(), // human-readable channel name
            senderId,
            senderName,
            messageText ?? string.Empty,
            sourceKind,
            targetKind);

        entries.Enqueue(entry);
        Interlocked.Increment(ref totalEnqueued);

        // Trim oldest entries if over capacity
        while (entries.Count > maxCapacity)
        {
            entries.TryDequeue(out _);
        }
    }

    private static string? ExtractPlainText(ReadOnlySpan<XivChatEntry> message)
    {
        if (message.IsEmpty)
            return null;

        // Simplified: XivChatEntry has a TextValue or similar property
        // In practice, iterate over the span and aggregate text
        // This implementation depends on the actual XivChatEntry API
        StringBuilder sb = new();
        foreach (ref readonly XivChatEntry entry in message)
        {
            // sb.Append(entry.TextValue); // or entry.ToString()
        }
        return sb.Length > 0 ? sb.ToString().Trim() : null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        chatGui.ChatMessage -= OnChatMessage;
    }
}
```

### XivChatType 主要频道枚举值

```csharp
// Source: Dalamud API 15 documentation [CITED: https://dalamud.dev/api/Dalamud.Game.Text/]
// 以下是常用的 XivChatType 值，用于频道过滤
var chatChannels = new[]
{
    XivChatType.Say,           // /say 说话
    XivChatType.Shout,         // /shout 喊话
    XivChatType.Yell,          // /yell 大喊
    XivChatType.TellOutgoing,  // /tell 发送的私聊
    XivChatType.TellIncoming,  // 收到的私聊
    XivChatType.Party,         // /party 小队
    XivChatType.Alliance,      // /alliance 团队
    XivChatType.FreeCompany,   // /fc 部队
    XivChatType.Linkshell1..8, // /ls1-8 通讯贝
    XivChatType.CrossLinkshell1..8, // /cwls1-8 跨界通讯贝
    XivChatType.System,        // 系统消息
    XivChatType.NoviceNetwork, // 新人频道
    XivChatType.Notice,        // 通知
    XivChatType.Echo,          // 回声（玩家自己输入的命令反馈）
};

// 战斗相关：
// FFXIV 没有独立的 "战斗日志" 频道。战斗相关的消息分布在：
// - XivChatType.System (boss 喊话、击杀信息)
// - XivChatType.Notice (重要通知)
// - 部分游戏内 EventLog 消息不出现在 IChatGui 中
```

### DI 注册变更

```csharp
// Source: PluginServiceCollectionExtensions.cs [VERIFIED: codebase]
public static ServiceProvider BuildDalamudServiceProvider(
    IDalamudPluginInterface pluginInterface,
    Configuration.PluginUiConfigurationStore configurationStore,
    PluginRuntimeOptions options,
    IFramework framework,
    IClientState clientState,
    ICondition condition,
    IObjectTable objectTable,
    IPlayerState playerState,
    IGameInventory gameInventory,
    IFateTable fateTable,
    IDataManager dataManager,
    IGameGui gameGui,
    ITargetManager targetManager,
    IChatGui chatGui) // NEW PARAMETER
{
    // ... existing registrations ...
    
    services.AddSingleton(chatGui); // NEW REGISTRATION
    services.AddSingleton<ChatLogBufferService>(); // NEW SERVICE
    
    // ...
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| ChatMessage 事件的 XivChatType 包含打包的关系数据 | 关系数据拆分为独立的 sourceKind/targetKind 参数 | API 15 (v15, 2025) | 委托签名变更，编译不兼容 [CITED: dalamud.dev/versions/v15] |
| — | LogMessage 事件引入 | API 14 (v14, 2024) | 提供另一种日志订阅方式，包含时间戳但无发送者信息 |
| .NET 8 | .NET 10 | API 15 | 项目已迁移至 .NET 10，ConcurrentQueue 性能有改善 |

**Deprecated/outdated:**
- 依赖 `XivChatType` 值大于 110 来判断关系的做法：API 15 已弃用，改用 `XivChatRelationKind` [CITED: dalamud.dev/versions/v15]

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | API 15 的 `IChatGui.OnMessageDelegate` 签名包含 `XivChatRelationKind sourceKind` 和 `XivChatRelationKind targetKind` 参数 | Pattern 1, Pitfall 1 | 中等 — 编译前需在 DALAMUD_HOME 参考程序集中验证实际签名 |
| A2 | `ChatMessage` 事件在框架线程上触发 | Pitfall 3 | 低 — 已验证为 Dalamud 常见行为 |
| A3 | 完整战斗日志（damage/healing）不在 IChatGui 范围内 | Summary | 中等 — 如果用户期望 ACT 级别的战斗数据，需明确告知此限制 |
| A4 | IChatGui 可以直接通过 DI 容器注入 | DI 注册 | 低 — `IDalamudPluginInterface` 已提供 `GetService<T>()`，但查看 EntryPoint 构造函数，所有 service 都是显式构造器注入，因此 IChatGui 也需要相同方式 |

## Open Questions (RESOLVED)

1. **XivChatEntry 的确切 API 签名是什么？** `RESOLVED`
   - 已知: API 15 中 `ChatMessage` 事件使用 `ReadOnlySpan<XivChatEntry> message` 参数
   - 未知: `XivChatEntry` 公开的属性和方法（如 `.TextValue`, `.ToString()` 等）
   - **决议:** 在开发时直接从 DALAMUD_HOME 的 Dalamud.dll 引用程序集中确认实际 API 签名。ChatLogBufferService 的实现将使用实际可用的属性来提取消息文本。如果 TextValue 不可用，回退到对每个 entry 调用 `.ToString()`。

2. **LogMessage 事件是否需要同时订阅？** `RESOLVED`
   - 已知: `LogMessage` 在 API 14+ 可用，签名 `(XivChatType type, DateTime timestamp, ref SeString message)`
   - 未知: `LogMessage` 与 `ChatMessage` 的覆盖范围差异（LogMessage 是否包含 ChatMessage 没有的消息？）
   - **决议:** 先仅订阅 ChatMessage。如果测试发现 LogMessage 能覆盖 ChatMessage 未包含的消息类型，则作为后续增强添加。当前实现范围限定为 ChatMessage 单渠道覆盖。

3. **"战斗日志" 应该如何界定范围？** `RESOLVED`
   - 已知: FFXIV 没有独立的战斗日志 API
   - 未知: 用户期望的战斗日志数据是什么（boss 喊话？击杀信息？damage numbers？）
   - **决议:** 限定为 "出现在聊天框中的战斗相关 XivChatType 消息"（如 System 频道中的战斗公告、Notice 频道中的重要战斗通知等）。不包含 ACT 级别的 damage/healing 数字解析（需要网络包捕获或内存扫描，超出本阶段范围）。
   
## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| DALAMUD_HOME (API 15) | IChatGui 服务 | 待 Phase 1 确认 | API 15 | — |
| System.Collections.Concurrent | ChatLogBufferService | ✓ (框架内置) | .NET 10 | — |
| xUnit | 测试 | ✓ | 3.2.2 | — |

**Missing dependencies with no fallback:**
- DALAMUD_HOME 指向 API 15 运行时 — Phase 1 验证前提

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 3.2.2 (`xunit.v3.mtp-v2`) |
| Config file | 隐式 — `tests/DalamudMCP.Plugin.Operations.Tests` 项目文件 |
| Quick run command | `dotnet test tests/DalamudMCP.Plugin.Operations.Tests --filter "FullyQualifiedName~ChatLog" --no-build` |
| Full suite command | `dotnet test tests/DalamudMCP.Plugin.Operations.Tests` |

### Phase Requirements -> Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| REQ-01 | ChatLogReadOperation carries correct operation ID, CLI command, and MCP tool attributes | unit | `dotnet test --filter "FullyQualifiedName~ChatLogReadOperation_Carries*"` | ❌ Wave 0 |
| REQ-02 | Request type carries correct protocol identity attributes | unit | `dotnet test --filter "FullyQualifiedName~ChatLogReadOperation_Request*"` | ❌ Wave 0 |
| REQ-03 | ExecuteAsync invokes the injected executor and returns ChatLogSnapshot | unit | `dotnet test --filter "FullyQualifiedName~ExecuteAsync_UsesInjectedExecutor*"` | ❌ Wave 0 |
| REQ-04 | ChatLogBufferService filters by channels correctly | unit | `dotnet test --filter "FullyQualifiedName~ChatLogBufferService*FilterByChannel*"` | ❌ Wave 0 |
| REQ-05 | ChatLogBufferService filters by timestamp correctly | unit | `dotnet test --filter "FullyQualifiedName~ChatLogBufferService*FilterByTimestamp*"` | ❌ Wave 0 |
| REQ-06 | ChatLogBufferService respects max-count parameter | unit | `dotnet test --filter "FullyQualifiedName~ChatLogBufferService*MaxCount*"` | ❌ Wave 0 |
| REQ-07 | ChatLogBufferService enforces capacity limit | unit | `dotnet test --filter "FullyQualifiedName~ChatLogBufferService*Capacity*"` | ❌ Wave 0 |
| REQ-08 | Operation implements IPluginReaderStatus correctly | unit | `dotnet test --filter "FullyQualifiedName~ChatLogReadOperation_ReaderStatus*"` | ❌ Wave 0 |
| REQ-09 | TextFormatter produces expected output | unit | `dotnet test --filter "FullyQualifiedName~ChatLogReadOperation_TextFormatter*"` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test tests/DalamudMCP.Plugin.Operations.Tests --filter "FullyQualifiedName~ChatLog" --no-build`
- **Per wave merge:** `dotnet test tests/DalamudMCP.Plugin.Operations.Tests`
- **Phase gate:** Full suite green before `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `tests/DalamudMCP.Plugin.Operations.Tests/ChatLogReadOperationTests.cs` — all ChatLog operation unit tests
- [ ] `tests/DalamudMCP.Plugin.Tests/ChatLogBufferServiceTests.cs` — buffer service unit tests (可能在 Plugin.Tests 项目而非 Operations.Tests)

## Security Domain

> `security_enforcement` is absent from config.json (default enabled). This section is required.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | 日志读取是只读操作，不需要认证 |
| V3 Session Management | no | 无会话管理 |
| V4 Access Control | partial | 通过 PluginOperationExposurePolicy 控制日志操作的暴露 |
| V5 Input Validation | yes | Request 参数（channels, since, max-count）需验证 |
| V6 Cryptography | no | 日志数据不涉及加密 |

### Known Threat Patterns

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| 恶意频道值导致枚举转换异常 | Tampering | 使用 TryParse 而非直接强制转换；对 filter 参数做范围验证 |
| max-count 过大导致 OOM | DoS | 设置上限（如 500），在 Request 规范化中 clamp |
| since 日期值异常 | Tampering | 验证 DateTimeOffset 是否在合理范围内（拒绝未来的时间戳） |
| 日志数据泄露隐私信息 | Information Disclosure | 日志数据本身就是用户可见的游戏内聊天内容，无额外敏感信息。但需注意 Tell（私聊）内容的暴露 |

## Sources

### Primary (HIGH confidence)
- **Codebase patterns** — 已读取 20+ 个 Operations 的完整实现 [VERIFIED: codebase]
- **Dalamud API 15 changelog** — `dalamud.dev/versions/v15/` — 确认 IChatGui 变更 [CITED]
- **Dalamud Namespace docs** — `dalamud.dev/api/Dalamud.Plugin.Services/IChatGui` — 接口定义 [CITED]

### Secondary (MEDIUM confidence)
- **XivChatRelationKind enum** — `dalamud.dev/api/api15/Dalamud.Game.Text/Enums/XivChatRelationKind/` [CITED]
- **XivChatType enum** — `dalamud.dev/api/Dalamud.Game.Text/` — 频道值定义 [CITED]
- **Dalamud GitHub api15 branch** — `github.com/goatcorp/Dalamud/tree/api15` — 源代码参考 [CITED]

### Tertiary (LOW confidence)
- 具体 XivChatEntry 的属性签名 — 未从官方文档验证，需在开发时确认 [ASSUMED]
- API 15 ChatMessage 委托的确切参数顺序 — 基于文档描述推断，需编译验证 [ASSUMED]

## Metadata

**Confidence breakdown:**
- **Standard stack:** HIGH — 仅使用现有框架 + .NET 内置库
- **Architecture:** HIGH — Hybrid buffer+pull 模式在行业中被广泛验证
- **Pitfalls:** HIGH — 所有列出的陷阱来自 Dalamud 社区常见问题和 .NET 并发模式已知问题
- **API 15 签名细节:** MEDIUM — 无法直接获取 API 15 引用程序集，但文档和社区资源一致确认签名变更

**Research date:** 2026-05-01
**Valid until:** 2026-06-01 (直到 API 15 可能的后续热修复版本)
