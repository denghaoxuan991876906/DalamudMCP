using Dalamud.Game.Text;
using DalamudMCP.Framework;
using DalamudMCP.Plugin.Services;
using DalamudMCP.Protocol;

namespace DalamudMCP.Plugin.Operations.Tests;

public sealed class ChatLogReadOperationTests
{
    [Fact]
    public void ChatLogReadOperation_CarriesCliAndMcpMetadata_OnTheOperationClass()
    {
        Type operationType = typeof(ChatLogReadOperation);

        OperationAttribute? operation = operationType.GetCustomAttribute<OperationAttribute>();
        CliCommandAttribute? cli = operationType.GetCustomAttribute<CliCommandAttribute>();
        McpToolAttribute? mcp = operationType.GetCustomAttribute<McpToolAttribute>();

        Assert.NotNull(operation);
        Assert.NotNull(cli);
        Assert.NotNull(mcp);
        Assert.Equal("chat.read", operation.OperationId);
        Assert.Equal(["chat", "read"], cli.PathSegments);
        Assert.Equal("get_chat_log", mcp.Name);
    }

    [Fact]
    public void ChatLogReadOperation_RequestCarriesProtocolIdentity()
    {
        ProtocolOperationAttribute? protocol = typeof(ChatLogReadOperation.Request)
            .GetCustomAttribute<ProtocolOperationAttribute>();

        Assert.NotNull(protocol);
        Assert.Equal("chat.read", protocol.OperationId);
    }

    [Fact]
    public async Task ExecuteAsync_UsesInjectedExecutor_AndReturnsChatLogSnapshot()
    {
        ChatLogSnapshot expected = new(
            DateTimeOffset.UtcNow,
            [
                new ChatLogEntry(
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    XivChatType.Say,
                    "Say",
                    0x12345678u,
                    "TestCharacter",
                    "Hello world",
                    XivChatRelationKind.None,
                    XivChatRelationKind.None)
            ],
            1,
            "1 log entries returned.");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        CancellationToken observedCancellationToken = default;
        ChatLogReadOperation operation = new(
            (request, cancellation) =>
            {
                observedCancellationToken = cancellation;
                Assert.NotNull(request.Channels);
                Assert.Equal(["Say"], request.Channels!);
                return ValueTask.FromResult(expected);
            });

        ChatLogSnapshot actual = await operation.ExecuteAsync(
            new ChatLogReadOperation.Request { Channels = ["Say"] },
            OperationContext.ForCli("chat.read", cancellationToken: cancellationToken));

        Assert.Equal(expected, actual);
        Assert.Equal(cancellationToken, observedCancellationToken);
    }

    [Fact]
    public void ChatLogReadOperation_ImplementsReaderStatus_Correctly()
    {
        // Default: ready
        ChatLogReadOperation readyOperation = new(
            (_, _) => ValueTask.FromResult(new ChatLogSnapshot(
                DateTimeOffset.UtcNow, [], 0, "0 entries.")));

        Assert.True(readyOperation.IsReady);
        Assert.Equal("ready", readyOperation.Detail);
        Assert.Equal("chat.read", readyOperation.ReaderKey);

        // Custom readiness
        ChatLogReadOperation notReadyOperation = new(
            (_, _) => ValueTask.FromResult(new ChatLogSnapshot(
                DateTimeOffset.UtcNow, [], 0, "0 entries.")),
            isReady: false,
            detail: "not_initialized");

        Assert.False(notReadyOperation.IsReady);
        Assert.Equal("not_initialized", notReadyOperation.Detail);
    }
}
