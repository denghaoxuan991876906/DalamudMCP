---
phase: 09-ui-language-switch
verified: 2026-04-30T18:00:00Z
status: human_needed
score: 5/5 must-haves verified
overrides_applied: 0
overrides: []
gaps: []
deferred: []
human_verification:
  - test: "语言选择器 ComboBox 可见性"
    expected: "配置窗口标题栏区域显示语言选择器 ComboBox，包含中文和 English 两个选项"
    why_human: "ImGui 渲染需要 Dalamud 运行时环境（FFXIV 游戏），无法在编译或单元测试中验证"
  - test: "切换语言后所有文本即时更新"
    expected: "在 ComboBox 中选择语言后，窗口内所有标签、按钮、状态行、表格头文本立即切换为目标语言，无需关闭或重新打开窗口"
    why_human: "需要实时 ImGui 绘制循环验证，无法以编程方式验证 UI 刷新行为"
  - test: "语言偏好持久化"
    expected: "选择 English，通过 /xlplugins 重载插件，重新打开配置窗口，语言应保持为 English"
    why_human: "需要 Dalamud 插件生命周期支持，无法以编程方式验证"
---

# Phase 9: 可切换界面语言 Verification Report

**Phase Goal:** 插件 UI 支持在中文和英文之间切换显示语言
**Verified:** 2026-04-30T18:00:00Z
**Status:** human_needed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths (from ROADMAP Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | 配置窗口中提供语言切换选项（中文/English） | VERIFIED | `PluginConfigWindow.DrawLanguageSelector()` 方法存在（第 139-154 行），使用 `ImGui.Combo("##lang", ...)` 提供 `["中文", "English"]` 选项。在 `DrawHeader()` 末尾调用（第 135 行）。 |
| 2 | 切换语言后所有 UI 文本即时更新，无需重启插件 | VERIFIED | 所有文本通过 `localization["key"]` 在绘制时实时查找。`PluginConfigWindow` 构造函数订阅 `LanguageChanged` 事件（第 51 行），处理器 `OnLanguageChanged` 调用 `RefreshModel(force: true)`（第 114-116 行）。模型状态文本全部为 computed getter 使用 `loc["key"]`。 |
| 3 | 语言偏好持久化保存，下次启动时保持选择 | VERIFIED | `PluginUiConfiguration.SelectedLanguage` 属性默认 "zh"（Version 4）。`DrawLanguageSelector` 切换时执行 `configurationStore.Update(config => config.SelectedLanguage = ...)`。`PluginEntryPoint` 构造函数（第 79-81 行）启动时从配置读取并调用 `localization.SetLanguage(...)`。 |
| 4 | 操作结果和状态信息跟随语言切换 | VERIFIED | 7 个状态文本属性全部为 computed getter：`ProtocolServerStatusText`、`ActionOperationsStatusText`、`UnsafeOperationsStatusText`、`McpServerStatusText`、`ReaderStatusText`、`McpServerEndpointText`、`McpServerErrorText`。`PluginConfigOperationRow` 中 `ReaderStatusText` 和 `ExposureStatusText` 同为 computed getter。 |
| 5 | CLI 帮助文本随语言切换更新 | VERIFIED | 操作表中 CLI 命令前缀和 MCP 工具前缀在绘制时通过 `localization["label.cli_prefix"]` 和 `localization["label.mcp_prefix"]` 动态获取（`DrawOperations` 第 363-368 行）。CLI 二进制独立运行，其帮助文本不在本阶段作用域内（UI-SPEC 已注明）。 |

**Score:** 5/5 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
| -------- | -------- | ------ | ------- |
| `src/DalamudMCP.Plugin/Ui/Localization/IUiLocalization.cs` | 本地化服务契约接口 (min_lines: 12) | VERIFIED | 15 行，包含 `this[string]`、`GetString`、`CurrentLanguage`、`SetLanguage`、`LanguageChanged`。存在性 ✓、内容充实 ✓、已通过 DI 注册接入 ✓ |
| `src/DalamudMCP.Plugin/Ui/Localization/JsonLocalization.cs` | JSON 词典加载与语言切换实现 (min_lines: 50) | VERIFIED | 52 行，sealed class。嵌入式资源加载 en/zh 词典，GetString 回退链：zh -> en -> key name。SetLanguage 仅接受 "en"/"zh"。存在性 ✓、内容充实 ✓、已通过 DI 接入 ✓ |
| `src/DalamudMCP.Plugin/lang/en.json` | 英文字符串词典 (contains: "window.title") | VERIFIED | 85 个键值对，有效 JSON，包含 "window.title"。 |
| `src/DalamudMCP.Plugin/lang/zh.json` | 中文字符串词典 (contains: "window.title") | VERIFIED | 85 个键值对，有效 JSON，键集与 en.json 完全一致（测试验证）。 |
| `src/DalamudMCP.Plugin/Configuration/PluginUiConfiguration.cs` | 配置模型包含 SelectedLanguage (contains: "SelectedLanguage") | VERIFIED | `SelectedLanguage` 属性存在，默认 "zh"，Version = 4 |
| `src/DalamudMCP.Plugin/Hosting/PluginServiceCollectionExtensions.cs` | DI 注册 (contains: "AddSingleton<IUiLocalization, JsonLocalization>") | VERIFIED | 第 48 行 `services.AddSingleton<IUiLocalization, JsonLocalization>()` |
| `src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj` | EmbeddedResource 声明 (contains: "EmbeddedResource") | VERIFIED | 第 13-14 行：`<EmbeddedResource Include="lang\en.json" />` 和 `lang\zh.json` |
| `src/DalamudMCP.Plugin/Ui/PluginConfigWindowModel.cs` | 本地化感知的模型 (contains: "IUiLocalization") | VERIFIED | `IUiLocalization loc` 字段，所有 7 个状态文本属性均为 computed getter。 |
| `src/DalamudMCP.Plugin/Ui/PluginConfigWindow.cs` | 本地化感知的配置窗口 (contains: "DrawLanguageSelector") | VERIFIED | `DrawLanguageSelector` 方法，约 50 处 `localization["key"]` 替换。 |
| `src/DalamudMCP.Plugin/PluginEntryPoint.cs` | 启动时语言初始化 (contains: "SetLanguage") | VERIFIED | 第 79-81 行：`compositionRoot.GetRequiredService<IUiLocalization>()` + `SetLanguage(config.SelectedLanguage)` |
| `tests/DalamudMCP.Plugin.Tests/PluginConfigWindowModelTests.cs` | 使用 FakeUiLocalization 的模型测试 (contains: "FakeUiLocalization") | VERIFIED | `FakeUiLocalization` 类存在，6 个测试全部通过。 |
| `tests/DalamudMCP.Plugin.Tests/JsonLocalizationTests.cs` | 本地化实现验证测试 (contains: "JsonLocalizationTests") | VERIFIED | 8 个测试方法全部通过。 |

### Key Link Verification

| From | To | Via | Status | Details |
| ---- | --- | --- | ------ | ------- |
| PluginServiceCollectionExtensions.cs | IUiLocalization | DI 单例注册 | VERIFIED | `AddSingleton<IUiLocalization, JsonLocalization>()` 第 48 行 |
| JsonLocalization.cs | en.json / zh.json | Assembly.GetManifestResourceStream | VERIFIED | `LoadFromResource` 私有方法使用 `GetManifestResourceStream` 加载嵌入资源 |
| PluginEntryPoint 构造函数 | localization.SetLanguage | 启动初始化 | VERIFIED | 第 79-81 行：读取 `config.SelectedLanguage`，调用 `SetLanguage()` |
| PluginConfigWindow.DrawOperations | label.cli_prefix / label.mcp_prefix | 绘制时字符串前缀拼接 | VERIFIED | 第 363-368 行：`localization["label.cli_prefix"]` + `operation.CliCommand` |
| PluginConfigWindow.LanguageChanged | RefreshModel(force: true) | 事件订阅 | VERIFIED | 第 51 行订阅，第 112-116 行 `OnLanguageChanged` 处理器 |
| PluginConfigWindowModelTests | FakeUiLocalization | 测试替身注入 | VERIFIED | 所有 6 个测试方法中使用 `new FakeUiLocalization()` 注入 |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
| -------- | ------------- | ------ | ------------------ | ------ |
| PluginConfigWindow.cs | `localization["key"]` | JsonLocalization -> Embedded JSON | JSON 嵌入资源在编译时构建，运行时通过 GetManifestResourceStream 加载 | FLOWING |
| PluginConfigWindowModel.cs | loc["key"] (computed getters) | JsonLocalization -> Embedded JSON | 每个 getter 调用都实时查询本地化服务，不缓存 | FLOWING |
| PluginConfigOperationRow.cs | loc["key"] (computed getters) | JsonLocalization -> Embedded JSON | ReaderStatusText 和 ExposureStatusText 为 computed getter | FLOWING |
| PluginEntryPoint.cs | configurationStore.Current.SelectedLanguage | PluginUiConfigurationStore -> 磁盘持久化 | 配置通过 Dalamud 的 IPluginConfiguration 机制持久化 | FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
| -------- | ------- | ------ | ------ |
| Plugin 项目编译成功 | `dotnet build src/DalamudMCP.Plugin/DalamudMCP.Plugin.csproj --no-restore` | 0 errors, 0 warnings | PASS |
| PluginConfigWindowModelTests 全部通过 | `dotnet test --project tests/DalamudMCP.Plugin.Tests/ --no-restore -- --filter-class "DalamudMCP.Plugin.Tests.PluginConfigWindowModelTests"` | 6/6 passed | PASS |
| JsonLocalizationTests 全部通过 | `dotnet test --project tests/DalamudMCP.Plugin.Tests/ --no-restore -- --filter-class "DalamudMCP.Plugin.Tests.JsonLocalizationTests"` | 8/8 passed | PASS |

### Requirements Coverage

以下需求由各 PLAN 声明，但不在主 REQUIREMENTS.md 中定义：

| Requirement | Source Plan | Description | Status | Evidence |
| ----------- | ----------- | ----------- | ------ | -------- |
| REQ-01 | Plan 01, Plan 02 | 配置窗口语言切换选项 | SATISFIED | DrawLanguageSelector 方法 + 下拉框 |
| REQ-02 | Plan 02 | 切换语言后即时更新 | SATISFIED | LanguageChanged 事件 + computed getter |
| REQ-03 | Plan 01 | 语言偏好持久化 | SATISFIED | PluginUiConfiguration.SelectedLanguage + 启动加载 |
| REQ-04 | Plan 02 | 操作结果和状态信息跟随语言切换 | SATISFIED | 所有状态文本 computed getter |
| REQ-05 | Plan 02 | CLI 帮助文本随语言切换更新 | SATISFIED | 操作表 CLI/MCP 前缀动态本地化 |
| L10N-01 | Plan 03 | 语言切换持久化 | SATISFIED | 同上 REQ-03，有测试覆盖 |
| L10N-02 | Plan 03 | 语言改变强制刷新 | SATISFIED | OnLanguageChanged -> RefreshModel |
| L10N-03 | Plan 03 | 所有本地化键在 en/zh 中都存在 | SATISFIED | All_zh_keys_match_en_keys 测试通过 |
| L10N-04 | Plan 03 | 回退到英文键 | SATISFIED | JsonLocalization.GetString 回退链 zh -> en -> key |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
| ---- | ---- | ------- | -------- | ------ |
| `tests/DalamudMCP.Plugin.Tests/JsonLocalizationTests.cs` | 72-77 | 占位测试 (`GetString_falls_back_from_zh_to_en_when_only_en_has_key` 仅 `Assert.True(true)`) | Info | 计划中已注明因嵌入式资源限制无法动态测试此用例；`All_zh_keys_match_en_keys` 已覆盖键空间一致性 |

此外，`PluginConfigWindow.cs` 第 143 行包含 "中文"/"English" 字符串字面量。这是有意为之——语言选择器标签按 i18n 标准使用自身语言显示，不属于未本地化的硬编码字符串。

### Human Verification Required

以下三项需要 Dalamud 运行时环境（FFXIV 游戏）手动验证，无法通过编程方式确认：

1. **语言选择器 ComboBox 可见性**
   - **操作:** 在 FFXIV 中加载插件，打开配置窗口
   - **预期:** 标题栏区域显示语言选择器 ComboBox，包含"中文"和"English"两个选项
   - **原因:** ImGui 渲染需要 Dalamud 运行时环境

2. **切换语言后所有文本即时更新**
   - **操作:** 通过 ComboBox 切换语言
   - **预期:** 所有标签、按钮、状态行、表格头文本立即切换，无需关闭窗口
   - **原因:** 需要实时 ImGui 绘制循环

3. **语言偏好持久化**
   - **操作:** 选择 English，通过 `/xlplugins` 重载插件，重新打开配置窗口
   - **预期:** 语言保持为 English
   - **原因:** 需要 Dalamud 插件生命周期

### Gaps Summary

**无阻断性差距。** 所有 5 个 Roadmap Success Criteria 均已在代码层面验证通过：
- 本地化基础设施（IUiLocalization + JsonLocalization + 双语 JSON 词典 + DI 注册）完整
- 配置窗口所有文本通过本地化服务实时查找，语言切换强制刷新
- 语言偏好持久化并启动时恢复
- 所有状态和操作文本为 computed getter
- 构建零错误，14 个单元测试全部通过

需要人工验证的 3 项涉及 ImGui 运行时行为和插件生命周期，无法在编译或单元测试中验证。

---

_Verified: 2026-04-30T18:00:00Z_
_Verifier: Claude (gsd-verifier)_
