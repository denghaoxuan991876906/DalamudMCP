using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using Dalamud.Plugin.Services;
using DalamudMCP.Framework;
using DalamudMCP.Protocol;
using DalamudMCP.Plugin.Ipc;
using MemoryPack;

namespace DalamudMCP.Plugin.Operations;

[Operation(
    "plugin.ipc",
    Description = "使用约定式命名 {PluginName}.MCP.{Action} 调用目标插件的 IPC 函数。基元类型参数自动推断并直接传递，复杂对象以 JSON 字符串信封传递。目标插件需按约定注册 IPC CallGate，无需依赖 DalamudMCP SDK。返回结构化响应包含状态码（ipc_success/ipc_missing/ipc_not_ready/ipc_type_mismatch/ipc_plugin_error）和返回值。",
    Summary = "Invokes a convention-based plugin IPC function.")]
[ResultFormatter(typeof(SafeInvokePluginIpcOperation.TextFormatter))]
[CliCommand("plugin", "ipc")]
[McpTool("invoke_plugin_ipc")]
public sealed partial class SafeInvokePluginIpcOperation
    : IOperation<SafeInvokePluginIpcOperation.Request, SafeInvokePluginIpcResult>
{
    private readonly Func<Request, CancellationToken, ValueTask<SafeInvokePluginIpcResult>> executor;

    [SupportedOSPlatform("windows")]
    public SafeInvokePluginIpcOperation(
        IPluginIpcGateway gateway,
        IFramework framework)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(framework);

        executor = CreateDalamudExecutor(gateway, framework);
    }

    internal SafeInvokePluginIpcOperation(Func<Request, CancellationToken, ValueTask<SafeInvokePluginIpcResult>> executor)
    {
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public ValueTask<SafeInvokePluginIpcResult> ExecuteAsync(Request request, OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return executor(request, context.CancellationToken);
    }

    [MemoryPackable]
    [ProtocolOperation("plugin.ipc")]
    public sealed partial class Request
    {
        [Option("plugin-name", Description = "目标插件的内部名称（InternalName），大小写不敏感。")]
        public string PluginName { get; init; } = string.Empty;

        [Option("method", Description = "IPC 方法名。完整的 IPC CallGate 名称将构造为 {PluginName}.MCP.{Method}。")]
        public string Method { get; init; } = string.Empty;

        [Option("arguments-json", Description = "JSON 数组格式的参数列表。整数→int、浮点数→double、布尔→bool、字符串→string。JSON 对象和数组将以 JSON 字符串信封形式传递（目标插件自行反序列化）。",
            Required = false)]
        public string? ArgumentsJson { get; init; }
    }

    public sealed class TextFormatter : IResultFormatter<SafeInvokePluginIpcResult>
    {
        public string? FormatText(SafeInvokePluginIpcResult result, OperationContext context)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(context);

            return result.SummaryText;
        }
    }

    [SupportedOSPlatform("windows")]
    private static Func<Request, CancellationToken, ValueTask<SafeInvokePluginIpcResult>> CreateDalamudExecutor(
        IPluginIpcGateway gateway,
        IFramework framework)
    {
        return async (request, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(request.PluginName);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Method);

            if (framework.IsInFrameworkUpdateThread)
                return InvokeSafeIpc(gateway, request);

            SafeInvokePluginIpcResult? result = null;
            await framework.RunOnFrameworkThread(() =>
            {
                result = InvokeSafeIpc(gateway, request);
            }).ConfigureAwait(false);
            return result!;
        };
    }

    internal static SafeInvokePluginIpcResult InvokeSafeIpc(IPluginIpcGateway gateway, Request request)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(request);

        string pluginName = request.PluginName.Trim();
        string method = request.Method.Trim();
        string callgate = $"{pluginName}.MCP.{method}";

        try
        {
            (object?[] arguments, Type[] argumentTypes) = ParseArguments(request.ArgumentsJson);
            Type[] typeArguments = [.. argumentTypes, typeof(object)];

            if (!gateway.TryCreate(callgate, typeArguments, out IPluginCallGateSubscriber? subscriber) ||
                subscriber is null)
            {
                return new SafeInvokePluginIpcResult(
                    PluginName: pluginName,
                    Method: method,
                    Success: false,
                    Status: "ipc_missing",
                    ReturnValue: null,
                    ErrorMessage: $"No IPC subscriber found for callgate '{callgate}'.",
                    SummaryText: $"IPC call failed: callgate '{callgate}' not found (ipc_missing).");
            }

            if (!subscriber.HasFunction)
            {
                return new SafeInvokePluginIpcResult(
                    PluginName: pluginName,
                    Method: method,
                    Success: false,
                    Status: "ipc_not_ready",
                    ReturnValue: null,
                    ErrorMessage: $"IPC function for callgate '{callgate}' is not registered yet.",
                    SummaryText: $"IPC call failed: callgate '{callgate}' not ready (ipc_not_ready).");
            }

            object? result = subscriber.InvokeFunc(arguments);
            Type returnType = typeof(object);
            string returnJson = JsonSerializer.Serialize(result, returnType);
            return new SafeInvokePluginIpcResult(
                PluginName: pluginName,
                Method: method,
                Success: true,
                Status: "ipc_success",
                ReturnValue: returnJson,
                ErrorMessage: null,
                SummaryText: $"IPC '{callgate}' succeeded. Return value: {returnJson}.");
        }
        catch (InvalidCastException ex)
        {
            return new SafeInvokePluginIpcResult(
                PluginName: pluginName,
                Method: method,
                Success: false,
                Status: "ipc_type_mismatch",
                ReturnValue: null,
                ErrorMessage: $"Type mismatch: {ex.Message}",
                SummaryText: $"IPC call failed: type mismatch for callgate '{callgate}'.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidCastException)
        {
            return new SafeInvokePluginIpcResult(
                PluginName: pluginName,
                Method: method,
                Success: false,
                Status: "ipc_type_mismatch",
                ReturnValue: null,
                ErrorMessage: $"Type mismatch: {ex.InnerException.Message}",
                SummaryText: $"IPC call failed: type mismatch for callgate '{callgate}'.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return new SafeInvokePluginIpcResult(
                PluginName: pluginName,
                Method: method,
                Success: false,
                Status: "ipc_plugin_error",
                ReturnValue: null,
                ErrorMessage: ex.InnerException.Message,
                SummaryText: $"IPC call failed: plugin error for callgate '{callgate}'. {ex.InnerException.Message}");
        }
        catch (Exception ex)
        {
            return new SafeInvokePluginIpcResult(
                PluginName: pluginName,
                Method: method,
                Success: false,
                Status: "ipc_plugin_error",
                ReturnValue: null,
                ErrorMessage: ex.Message,
                SummaryText: $"IPC call failed: plugin error for callgate '{callgate}'. {ex.Message}");
        }
    }

    private static (object?[] arguments, Type[] types) ParseArguments(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return ([], []);

        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        if (document.RootElement.ValueKind is not JsonValueKind.Array)
            throw new ArgumentException("arguments-json must be a JSON array.", nameof(argumentsJson));

        JsonElement[] elements = document.RootElement.EnumerateArray().ToArray();
        object?[] arguments = new object?[elements.Length];
        Type[] types = new Type[elements.Length];

        for (int index = 0; index < elements.Length; index++)
        {
            (arguments[index], types[index]) = ParseJsonElement(elements[index]);
        }

        return (arguments, types);
    }

    private static (object? value, Type type) ParseJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => (true, typeof(bool)),
            JsonValueKind.False => (false, typeof(bool)),
            JsonValueKind.Number when element.TryGetInt32(out int i) => (i, typeof(int)),
            JsonValueKind.Number => (element.GetDouble(), typeof(double)),
            JsonValueKind.String => (element.GetString()!, typeof(string)),
            JsonValueKind.Object or JsonValueKind.Array => (element.GetRawText(), typeof(string)),
            JsonValueKind.Null => (null!, typeof(object)),
            _ => throw new ArgumentException($"Unsupported JSON value kind: {element.ValueKind}")
        };
    }
}

[MemoryPackable]
public sealed partial record SafeInvokePluginIpcResult(
    string PluginName,
    string Method,
    bool Success,
    string Status,
    string? ReturnValue,
    string? ErrorMessage,
    string SummaryText);
