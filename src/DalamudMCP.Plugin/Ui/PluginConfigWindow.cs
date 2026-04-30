using System.Numerics;
using Dalamud.Bindings.ImGui;
using DalamudMCP.Framework;
using DalamudMCP.Plugin.Configuration;
using DalamudMCP.Plugin.Readers;
using DalamudMCP.Plugin.Ui.Localization;
using DalamudMCP.Protocol;

namespace DalamudMCP.Plugin.Ui;

public sealed class PluginConfigWindow
{
    private const long RefreshIntervalMilliseconds = 250;
    private static readonly Vector4 AccentColor = new(0.42f, 0.77f, 0.95f, 1f);
    private static readonly Vector4 SuccessColor = new(0.41f, 0.83f, 0.60f, 1f);
    private static readonly Vector4 WarningColor = new(0.96f, 0.72f, 0.34f, 1f);
    private static readonly Vector4 DangerColor = new(0.92f, 0.43f, 0.43f, 1f);
    private static readonly Vector4 MutedColor = new(0.67f, 0.71f, 0.77f, 1f);

    private readonly IUiLocalization localization;
    private readonly PluginUiConfigurationStore configurationStore;
    private readonly Hosting.PluginMcpServerController mcpServerController;
    private readonly PluginConfigWindowModel model;
    private readonly NamedPipeProtocolServer protocolServer;
    private bool isOpen;
    private bool showBlockedOnly;
    private bool showReaderBackedOnly;
    private bool showAdvancedDetails;
    private string operationFilter = string.Empty;
    private long nextRefreshAt;

    public PluginConfigWindow(
        PluginRuntimeOptions options,
        NamedPipeProtocolServer protocolServer,
        PluginUiConfigurationStore configurationStore,
        Hosting.PluginMcpServerController mcpServerController,
        IReadOnlyList<OperationDescriptor> operations,
        IReadOnlyList<IPluginReaderStatus> readerStatuses)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.protocolServer = protocolServer ?? throw new ArgumentNullException(nameof(protocolServer));
        this.configurationStore = configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));
        this.mcpServerController = mcpServerController ?? throw new ArgumentNullException(nameof(mcpServerController));
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(readerStatuses);

        model = PluginConfigWindowModel.Create(
            options,
            protocolServer.IsRunning,
            configurationStore.Current.AutoStartHttpServerOnLoad,
            configurationStore.Current.EnableActionOperations,
            configurationStore.Current.EnableUnsafeOperations,
            mcpServerController.GetStatus(),
            operations,
            readerStatuses);
    }

    public void Open()
    {
        isOpen = true;
        RefreshModel(force: true);
    }

    public void Draw()
    {
        if (!isOpen)
            return;

        RefreshModel(force: false);

        ImGui.SetNextWindowSize(new Vector2(980f, 760f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("DalamudMCP 设置", ref isOpen, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        DrawHeader();
        DrawOverview();
        DrawQuickStart();
        DrawAdvancedDetails();
        DrawOperations();

        ImGui.End();
    }

    private void RefreshModel(bool force)
    {
        long now = Environment.TickCount64;
        if (!force && now < nextRefreshAt)
            return;

        model.Refresh(
            protocolServer.IsRunning,
            configurationStore.Current.AutoStartHttpServerOnLoad,
            configurationStore.Current.EnableActionOperations,
            configurationStore.Current.EnableUnsafeOperations,
            mcpServerController);
        nextRefreshAt = now + RefreshIntervalMilliseconds;
    }

    private void DrawHeader()
    {
        ImGui.TextColored(AccentColor, "DalamudMCP");
        ImGui.SameLine();
        ImGui.TextDisabled("FFXIV 观察、动作与 MCP 暴露的实时桥接。");

        DrawInlineBadge(
            model.ProtocolServerRunning ? "管道在线" : "管道离线",
            model.ProtocolServerRunning ? SuccessColor : DangerColor);
        DrawInlineBadge(
            model.McpServerRunning ? "HTTP 在线" : "HTTP 已停",
            model.McpServerRunning ? SuccessColor : WarningColor);
        DrawInlineBadge(
            $"{model.ExposedOperationCount}/{model.OperationCount} 已暴露",
            AccentColor);

        ImGui.TextColored(MutedColor, "顶行为运行时健康状态，下半部分用于操作浏览和可复制命令。");
        ImGui.Separator();
    }

    private void DrawOverview()
    {
        Vector2 available = ImGui.GetContentRegionAvail();
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float leftWidth = (available.X - spacing) * 0.46f;
        float panelHeight = 248f;

        DrawRuntimePanel(leftWidth, panelHeight);
        ImGui.SameLine();
        DrawServerPanel(new Vector2(0f, panelHeight));
    }

    private void DrawRuntimePanel(float width, float height)
    {
        if (!ImGui.BeginChild("RuntimePanel", new Vector2(width, height), true))
        {
            ImGui.EndChild();
            return;
        }

        DrawPanelTitle("运行时", "连接健康、发现与暴露状态。");
        DrawKeyValue("发现", "CLI 自动发现已启用");
        DrawStatusLine("命名管道", model.ProtocolServerRunning, model.ProtocolServerStatusText);
        if (!string.IsNullOrWhiteSpace(model.ReaderStatusText))
            DrawStatusLine("读取器", model.ReadyReaderCount == model.ReaderCount, model.ReaderStatusText!);

        DrawStatusLine("动作工具", model.ActionOperationsEnabled, model.ActionOperationsStatusText);
        DrawStatusLine("非安全工具", model.UnsafeOperationsEnabled, model.UnsafeOperationsStatusText);
        DrawKeyValue("操作", $"{model.OperationCount} 总计  |  {model.ExposedOperationCount} 已暴露  |  {model.BlockedOperationCount} 已限制");

        ImGui.Spacing();
        bool actionOperationsEnabled = model.ActionOperationsEnabled;
        if (ImGui.Checkbox("启用 CLI/MCP 动作操作", ref actionOperationsEnabled))
        {
            configurationStore.Update(configuration =>
                configuration.EnableActionOperations = actionOperationsEnabled);
            RefreshModel(force: true);
        }

        ImGui.TextWrapped("观察工具保持在线。动作默认关闭，需在此处显式暴露后才能使用。");

        bool unsafeOperationsEnabled = model.UnsafeOperationsEnabled;
        if (ImGui.Checkbox("启用非安全集成工具（仅开发者）", ref unsafeOperationsEnabled))
        {
            configurationStore.Update(configuration =>
                configuration.EnableUnsafeOperations = unsafeOperationsEnabled);
            RefreshModel(force: true);
        }

        ImGui.TextWrapped("非安全工具可调用任意插件 IPC 功能。除非正在调试其他插件，否则请保持关闭。");
        ImGui.EndChild();
    }

    private void DrawQuickStart()
    {
        ImGui.Spacing();
        if (!ImGui.BeginChild("QuickStartPanel", new Vector2(0f, 166f), true))
        {
            ImGui.EndChild();
            return;
        }

        DrawPanelTitle("命令台", "复制最常用的两个入口点命令。");
        ImGui.Columns(2, "QuickStartColumns", false);
        DrawCommandCard(
            "CLI 快速检查",
            "从活动插件实例读取实时玩家快照。",
            ToCommandSummary(model.CliCommand),
            model.CliCommand,
            "复制玩家上下文命令");
        ImGui.NextColumn();
        DrawCommandCard(
            "MCP 服务",
            "通过插件发现的管道启动本地 MCP 桥接。",
            ToCommandSummary(model.McpCommand),
            model.McpCommand,
            "复制 MCP 服务命令");
        ImGui.Columns(1);
        ImGui.EndChild();
    }

    private void DrawAdvancedDetails()
    {
        ImGui.Spacing();
        if (!ImGui.CollapsingHeader("高级详情", ref showAdvancedDetails))
            return;

        if (!ImGui.BeginChild("AdvancedPanel", new Vector2(0f, 132f), true))
        {
            ImGui.EndChild();
            return;
        }

        DrawKeyValue("管道", model.PipeName);
        DrawKeyValue("CLI 命令", model.CliCommand);
        DrawKeyValue("MCP 服务", model.McpCommand);
        if (!string.IsNullOrWhiteSpace(model.McpServerCommand))
            DrawKeyValue("HTTP 命令", model.McpServerCommand);
        if (!string.IsNullOrWhiteSpace(model.McpServerErrorText))
            DrawWrappedStatus(DangerColor, model.McpServerErrorText);

        ImGui.EndChild();
    }

    private void DrawServerPanel(Vector2 size)
    {
        if (!ImGui.BeginChild("ServerPanel", size, true))
        {
            ImGui.EndChild();
            return;
        }

        DrawPanelTitle("HTTP 服务器", "稳定的 MCP 端点，供无需关心管道名的客户端使用。");
        DrawKeyValue("端点", model.McpServerEndpoint);
        DrawStatusLine("HTTP 状态", model.McpServerRunning, model.McpServerStatusText);

        bool autoStartHttpServerOnLoad = model.AutoStartHttpServerOnLoad;
        if (ImGui.Checkbox("插件加载时自动启动 MCP HTTP 服务器", ref autoStartHttpServerOnLoad))
        {
            configurationStore.Update(configuration =>
                configuration.AutoStartHttpServerOnLoad = autoStartHttpServerOnLoad);
            RefreshModel(force: true);
        }

        ImGui.Spacing();
        if (!model.McpServerRunning)
        {
            if (ImGui.Button("启动 MCP HTTP 服务器", new Vector2(220f, 0f)))
            {
                mcpServerController.Start();
                nextRefreshAt = 0;
            }
        }
        else if (ImGui.Button("停止 MCP HTTP 服务器", new Vector2(220f, 0f)))
        {
            mcpServerController.Stop();
            nextRefreshAt = 0;
        }

        ImGui.SameLine();
        if (ImGui.Button("复制 MCP 端点", new Vector2(180f, 0f)))
            ImGui.SetClipboardText(model.McpServerEndpoint);

        if (!string.IsNullOrWhiteSpace(model.McpServerCommand))
        {
            if (ImGui.Button("复制 MCP 服务器命令", new Vector2(220f, 0f)))
                ImGui.SetClipboardText(model.McpServerCommand);
        }

        ImGui.EndChild();
    }

    private void DrawOperations()
    {
        ImGui.Spacing();
        if (!ImGui.BeginChild("OperationsPanel", new Vector2(0f, 0f), true))
        {
            ImGui.EndChild();
            return;
        }

        DrawPanelTitle("操作", "在将插件交给其他客户端前，筛选暴露的接口。");
        DrawKeyValue(
            "目录",
            $"{model.OperationCount} 总计  |  {model.ActionOperationCount} 动作  |  {model.UnsafeOperationCount} 非安全  |  {model.BlockedOperationCount} 已限制");

        ImGui.SetNextItemWidth(280f);
        ImGui.InputText("搜索", ref operationFilter, 128);
        ImGui.SameLine();
        ImGui.Checkbox("仅显示已限制", ref showBlockedOnly);
        ImGui.SameLine();
        ImGui.Checkbox("仅显示有读取器", ref showReaderBackedOnly);

        IReadOnlyList<PluginConfigOperationRow> operations = model.Operations;
        const ImGuiTableFlags tableFlags =
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.BordersInnerH |
            ImGuiTableFlags.BordersOuter |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.SizingStretchProp |
            ImGuiTableFlags.ScrollY;

        if (ImGui.BeginTable("OperationsTable", 4, tableFlags, new Vector2(0f, 0f)))
        {
            ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthStretch, 0.27f);
            ImGui.TableSetupColumn("访问", ImGuiTableColumnFlags.WidthStretch, 0.23f);
            ImGui.TableSetupColumn("状态", ImGuiTableColumnFlags.WidthStretch, 0.20f);
            ImGui.TableSetupColumn("摘要", ImGuiTableColumnFlags.WidthStretch, 0.30f);
            ImGui.TableHeadersRow();

            int visibleCount = 0;
            for (int index = 0; index < operations.Count; index++)
            {
                PluginConfigOperationRow operation = operations[index];
                if (!MatchesOperationFilters(operation))
                    continue;

                visibleCount++;
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(operation.OperationId);
                DrawInlineTag(operation.IsActionOperation ? "动作" : "观察", operation.IsActionOperation ? WarningColor : SuccessColor);
                if (operation.IsUnsafeOperation)
                    DrawInlineTag("非安全", DangerColor);

                ImGui.TableSetColumnIndex(1);
                string cliPrefix = localization["label.cli_prefix"];
                if (!string.IsNullOrWhiteSpace(operation.CliCommand))
                    ImGui.TextUnformatted(cliPrefix + operation.CliCommand);
                string mcpPrefix = localization["label.mcp_prefix"];
                if (!string.IsNullOrWhiteSpace(operation.McpToolName))
                    ImGui.TextUnformatted(mcpPrefix + operation.McpToolName);

                ImGui.TableSetColumnIndex(2);
                if (!string.IsNullOrWhiteSpace(operation.ReaderStatusText))
                    DrawWrappedStatus(operation.IsReaderReady == true ? SuccessColor : WarningColor, operation.ReaderStatusText);
                if (!string.IsNullOrWhiteSpace(operation.ExposureStatusText))
                    DrawWrappedStatus(WarningColor, operation.ExposureStatusText);
                if (string.IsNullOrWhiteSpace(operation.ReaderStatusText) &&
                    string.IsNullOrWhiteSpace(operation.ExposureStatusText))
                {
                    ImGui.TextColored(SuccessColor, "可暴露");
                }

                ImGui.TableSetColumnIndex(3);
                ImGui.TextWrapped(operation.Summary);
            }

            if (visibleCount == 0)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextColored(MutedColor, "无操作匹配当前筛选条件。");
            }

            ImGui.EndTable();
        }

        ImGui.EndChild();
    }

    private bool MatchesOperationFilters(PluginConfigOperationRow operation)
    {
        if (showBlockedOnly && operation.IsExposed)
            return false;

        if (showReaderBackedOnly && operation.IsReaderReady is null)
            return false;

        if (string.IsNullOrWhiteSpace(operationFilter))
            return true;

        return operation.OperationId.Contains(operationFilter, StringComparison.OrdinalIgnoreCase) ||
               operation.Summary.Contains(operationFilter, StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(operation.CliCommand) &&
                operation.CliCommand.Contains(operationFilter, StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrWhiteSpace(operation.McpToolName) &&
                operation.McpToolName.Contains(operationFilter, StringComparison.OrdinalIgnoreCase));
    }

    private static void DrawPanelTitle(string title, string subtitle)
    {
        ImGui.TextColored(AccentColor, title);
        ImGui.TextWrapped(subtitle);
        ImGui.Spacing();
    }

    private static void DrawKeyValue(string label, string value)
    {
        ImGui.TextColored(MutedColor, label);
        ImGui.SameLine();
        ImGui.TextUnformatted(value);
    }

    private static void DrawStatusLine(string label, bool isHealthy, string text)
    {
        ImGui.TextColored(MutedColor, label);
        ImGui.SameLine();
        ImGui.TextColored(isHealthy ? SuccessColor : WarningColor, text);
    }

    private static void DrawWrappedStatus(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private static void DrawInlineBadge(string text, Vector4 color)
    {
        ImGui.SameLine();
        ImGui.TextColored(color, text);
    }

    private static void DrawInlineTag(string text, Vector4 color)
    {
        ImGui.SameLine();
        ImGui.TextColored(color, $"[{text}]");
    }

    private static void DrawCommandCard(string title, string description, string displayCommand, string copyCommand, string copyButtonLabel)
    {
        ImGui.TextColored(AccentColor, title);
        ImGui.TextWrapped(description);
        DrawCodeBlock(title, displayCommand);
        if (ImGui.Button(copyButtonLabel, new Vector2(220f, 0f)))
            ImGui.SetClipboardText(copyCommand);
    }

    private static void DrawCodeBlock(string id, string content)
    {
        if (!ImGui.BeginChild(id, new Vector2(0f, 58f), true))
        {
            ImGui.EndChild();
            return;
        }

        ImGui.TextWrapped(content);
        ImGui.EndChild();
    }

    private static string ToCommandSummary(string command)
    {
        int markerIndex = command.LastIndexOf(" -- ", StringComparison.Ordinal);
        return markerIndex >= 0
            ? command[(markerIndex + 4)..]
            : command;
    }
}
