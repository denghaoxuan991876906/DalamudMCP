using System.Globalization;
using DalamudMCP.Framework;
using DalamudMCP.Plugin.Ui.Localization;
using DalamudMCP.Plugin.Readers;

namespace DalamudMCP.Plugin.Ui;

internal sealed class PluginConfigWindowModel
{
    private readonly IUiLocalization loc;
    private readonly IPluginReaderStatus?[] readerStatuses;
    private readonly PluginConfigOperationRow[] operations;
    private string? mcpServerCommand;
    private string? mcpServerError;
    private int readyReaderCount;
    private int readerCount;
    private int actionOperationCount;
    private int unsafeOperationCount;
    private int exposedOperationCount;
    private int blockedOperationCount;

    private PluginConfigWindowModel(
        IUiLocalization loc,
        string pipeName,
        string cliCommand,
        string mcpCommand,
        PluginConfigOperationRow[] operations,
        IPluginReaderStatus?[] readerStatuses)
    {
        this.loc = loc;
        PipeName = pipeName;
        PipeNameText = loc["label.pipe_name"] + pipeName;
        CliCommand = cliCommand;
        McpCommand = mcpCommand;
        this.operations = operations;
        this.readerStatuses = readerStatuses;
        Operations = operations;
    }

    public string PipeName { get; }

    public string PipeNameText { get; }

    public bool ProtocolServerRunning { get; private set; }

    public string ProtocolServerStatusText => loc[ProtocolServerRunning ? "status.server_running" : "status.server_stopped"];

    public bool AutoStartHttpServerOnLoad { get; private set; }

    public bool ActionOperationsEnabled { get; private set; }

    public string ActionOperationsStatusText => loc[ActionOperationsEnabled ? "status.actions_enabled" : "status.actions_disabled"];

    public bool UnsafeOperationsEnabled { get; private set; }

    public string UnsafeOperationsStatusText => loc[UnsafeOperationsEnabled ? "status.unsafe_enabled" : "status.unsafe_disabled"];

    public bool McpServerRunning { get; private set; }

    public string McpServerEndpoint { get; private set; } = string.Empty;

    public string McpServerEndpointText => loc["label.endpoint"] + McpServerEndpoint;

    public string? McpServerCommand => mcpServerCommand;

    public string? McpServerError => mcpServerError;

    public string? McpServerErrorText => string.IsNullOrWhiteSpace(mcpServerError) ? null : loc["label.last_error"] + mcpServerError;

    public string McpServerStatusText => loc[McpServerRunning ? "status.http_running" : "status.http_stopped"];

    public string CliCommand { get; }

    public string McpCommand { get; }

    public IReadOnlyList<PluginConfigOperationRow> Operations { get; }

    public int OperationCount => operations.Length;

    public int ReadyReaderCount => readyReaderCount;

    public int ReaderCount => readerCount;

    public int ActionOperationCount => actionOperationCount;

    public int UnsafeOperationCount => unsafeOperationCount;

    public int ExposedOperationCount => exposedOperationCount;

    public int BlockedOperationCount => blockedOperationCount;

    public string? ReaderStatusText
    {
        get
        {
            if (readerCount <= 0) return null;
            return string.Format(CultureInfo.InvariantCulture, loc["status.reader_format"], readyReaderCount, readerCount);
        }
    }

    public static PluginConfigWindowModel Create(
        IUiLocalization loc,
        PluginRuntimeOptions options,
        bool protocolServerRunning,
        bool autoStartHttpServerOnLoad,
        bool actionOperationsEnabled,
        bool unsafeOperationsEnabled,
        Hosting.PluginMcpServerStatus mcpServerStatus,
        IReadOnlyList<OperationDescriptor> operations,
        IReadOnlyList<IPluginReaderStatus> readerStatuses)
    {
        ArgumentNullException.ThrowIfNull(loc);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(mcpServerStatus);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(readerStatuses);

        PluginConfigOperationRow[] rows = CreateRows(loc, operations, readerStatuses, out IPluginReaderStatus?[] rowsByReader);
        PluginConfigWindowModel model = new(
            loc,
            options.PipeName,
            @"dotnet run --project .\src\DalamudMCP.Cli\DalamudMCP.Cli.csproj -- player context",
            @"dotnet run --project .\src\DalamudMCP.Cli\DalamudMCP.Cli.csproj -- serve mcp",
            rows,
            rowsByReader);
        model.ApplyStatus(
            protocolServerRunning,
            autoStartHttpServerOnLoad,
            actionOperationsEnabled,
            unsafeOperationsEnabled,
            mcpServerStatus.IsRunning,
            mcpServerStatus.EndpointUrl,
            mcpServerStatus.CommandText,
            mcpServerStatus.LastError);
        return model;
    }

    internal void Refresh(
        bool protocolServerRunning,
        bool autoStartHttpServerOnLoad,
        bool actionOperationsEnabled,
        bool unsafeOperationsEnabled,
        Hosting.PluginMcpServerController mcpServerController)
    {
        ArgumentNullException.ThrowIfNull(mcpServerController);
        ApplyStatus(
            protocolServerRunning,
            autoStartHttpServerOnLoad,
            actionOperationsEnabled,
            unsafeOperationsEnabled,
            mcpServerController.IsRunning,
            mcpServerController.EndpointUrl,
            mcpServerController.LastCommandText,
            mcpServerController.LastError);
    }

    internal void ApplyStatus(
        bool protocolServerRunning,
        bool autoStartHttpServerOnLoad,
        bool actionOperationsEnabled,
        bool unsafeOperationsEnabled,
        bool mcpServerRunning,
        string mcpServerEndpoint,
        string? mcpServerCommand,
        string? mcpServerError)
    {
        ProtocolServerRunning = protocolServerRunning;
        AutoStartHttpServerOnLoad = autoStartHttpServerOnLoad;
        ActionOperationsEnabled = actionOperationsEnabled;
        UnsafeOperationsEnabled = unsafeOperationsEnabled;
        McpServerRunning = mcpServerRunning;

        UpdateEndpointText(mcpServerEndpoint);
        UpdateCommand(mcpServerCommand);
        UpdateError(mcpServerError);
        RefreshExposureStatuses();
        RefreshReaderStatuses();
    }

    private static PluginConfigOperationRow[] CreateRows(
        IUiLocalization loc,
        IReadOnlyList<OperationDescriptor> operations,
        IReadOnlyList<IPluginReaderStatus> readerStatuses,
        out IPluginReaderStatus?[] rowsByReader)
    {
        OperationDescriptor[] sortedOperations = new OperationDescriptor[operations.Count];
        for (int index = 0; index < operations.Count; index++)
            sortedOperations[index] = operations[index];

        Array.Sort(
            sortedOperations,
            static (left, right) => string.Compare(left.OperationId, right.OperationId, StringComparison.Ordinal));

        Dictionary<string, IPluginReaderStatus> readersByKey = new(readerStatuses.Count, StringComparer.Ordinal);
        for (int index = 0; index < readerStatuses.Count; index++)
        {
            IPluginReaderStatus readerStatus = readerStatuses[index];
            readersByKey[readerStatus.ReaderKey] = readerStatus;
        }

        PluginConfigOperationRow[] rows = new PluginConfigOperationRow[sortedOperations.Length];
        rowsByReader = new IPluginReaderStatus?[sortedOperations.Length];
        for (int index = 0; index < sortedOperations.Length; index++)
        {
            OperationDescriptor operation = sortedOperations[index];
            rows[index] = new PluginConfigOperationRow(
                loc,
                operation.OperationId,
                operation.Summary ?? operation.Description ?? operation.OperationId,
                operation.CliCommandPath is { Count: > 0 } cliPath ? string.Join(' ', cliPath) : null,
                operation.McpToolName);
            readersByKey.TryGetValue(operation.OperationId, out rowsByReader[index]);
        }

        return rows;
    }

    private void UpdateEndpointText(string endpoint)
    {
        if (string.Equals(McpServerEndpoint, endpoint, StringComparison.Ordinal))
            return;
        McpServerEndpoint = endpoint;
    }

    private void UpdateCommand(string? command)
    {
        if (string.Equals(mcpServerCommand, command, StringComparison.Ordinal))
            return;

        mcpServerCommand = command;
    }

    private void UpdateError(string? error)
    {
        if (string.Equals(mcpServerError, error, StringComparison.Ordinal))
            return;
        mcpServerError = error;
    }

    private void RefreshReaderStatuses()
    {
        int newReadyReaderCount = 0;
        int newReaderCount = 0;
        for (int index = 0; index < operations.Length; index++)
        {
            PluginConfigOperationRow operation = operations[index];
            operation.UpdateReaderStatus(readerStatuses[index]);
            if (operation.IsReaderReady is null)
                continue;

            newReaderCount++;
            if (operation.IsReaderReady.Value)
                newReadyReaderCount++;
        }

        if (readyReaderCount == newReadyReaderCount &&
            readerCount == newReaderCount)
        {
            return;
        }

        readyReaderCount = newReadyReaderCount;
        readerCount = newReaderCount;
    }

    private void RefreshExposureStatuses()
    {
        int newActionOperationCount = 0;
        int newUnsafeOperationCount = 0;
        int newExposedOperationCount = 0;
        int newBlockedOperationCount = 0;
        for (int index = 0; index < operations.Length; index++)
        {
            PluginConfigOperationRow operation = operations[index];
            operation.UpdateExposureStatus(ActionOperationsEnabled, UnsafeOperationsEnabled);
            if (operation.IsActionOperation)
                newActionOperationCount++;

            if (operation.IsUnsafeOperation)
                newUnsafeOperationCount++;

            if (operation.IsExposed)
                newExposedOperationCount++;
            else
                newBlockedOperationCount++;
        }

        actionOperationCount = newActionOperationCount;
        unsafeOperationCount = newUnsafeOperationCount;
        exposedOperationCount = newExposedOperationCount;
        blockedOperationCount = newBlockedOperationCount;
    }
}

internal sealed class PluginConfigOperationRow
{
    private readonly IUiLocalization loc;
    private bool actionOperationsEnabled;
    private bool unsafeOperationsEnabled;

    public PluginConfigOperationRow(
        IUiLocalization loc,
        string operationId,
        string summary,
        string? cliCommand,
        string? mcpToolName)
    {
        this.loc = loc;
        OperationId = operationId;
        Summary = summary;
        CliCommand = cliCommand;
        McpToolName = mcpToolName;
        IsActionOperation = Hosting.PluginOperationExposurePolicy.IsActionOperation(operationId);
        IsUnsafeOperation = Hosting.PluginOperationExposurePolicy.IsUnsafeOperation(operationId);
    }

    public string OperationId { get; }

    public string Summary { get; }

    public string? CliCommand { get; }

    public string? McpToolName { get; }

    public bool? IsReaderReady { get; private set; }

    public string? ReaderDetail { get; private set; }

    public string? ReaderStatusText
    {
        get
        {
            if (IsReaderReady is null) return null;
            if (string.IsNullOrWhiteSpace(ReaderDetail))
                return IsReaderReady.Value ? loc["status.reader_ready"] : loc["status.reader_not_ready"];
            string readiness = IsReaderReady.Value
                ? loc["status.reader_ready_word"]
                : loc["status.reader_not_ready_word"];
            return string.Format(CultureInfo.InvariantCulture, loc["status.reader_detail"], readiness, ReaderDetail);
        }
    }

    public string? ExposureStatusText
    {
        get
        {
            if (IsUnsafeOperation && !unsafeOperationsEnabled)
                return loc["status.exposure_unsafe_pending"];
            if (IsActionOperation && !actionOperationsEnabled)
                return loc["status.exposure_action_pending"];
            return null;
        }
    }

    public bool IsActionOperation { get; }

    public bool IsUnsafeOperation { get; }

    public bool IsExposed { get; private set; } = true;

    internal void UpdateReaderStatus(IPluginReaderStatus? readerStatus)
    {
        bool? isReady = null;
        string? detail = null;
        if (readerStatus is not null)
            TryReadReaderStatus(readerStatus, out isReady, out detail);

        if (IsReaderReady == isReady &&
            string.Equals(ReaderDetail, detail, StringComparison.Ordinal))
        {
            return;
        }

        IsReaderReady = isReady;
        ReaderDetail = detail;
    }

    internal void UpdateExposureStatus(bool actionOperationsEnabled, bool unsafeOperationsEnabled)
    {
        this.actionOperationsEnabled = actionOperationsEnabled;
        this.unsafeOperationsEnabled = unsafeOperationsEnabled;
        IsExposed = !(IsUnsafeOperation && !unsafeOperationsEnabled
            || IsActionOperation && !actionOperationsEnabled);
    }

    private static void TryReadReaderStatus(
        IPluginReaderStatus readerStatus,
        out bool? isReady,
        out string? detail)
    {
        try
        {
            isReady = readerStatus.IsReady;
            detail = readerStatus.Detail;
        }
        catch (InvalidOperationException exception) when (string.Equals(exception.Message, "Not on main thread!", StringComparison.Ordinal))
        {
            isReady = false;
            detail = "main_thread_required";
        }
        catch
        {
            isReady = false;
            detail = "status_unavailable";
        }
    }
}
