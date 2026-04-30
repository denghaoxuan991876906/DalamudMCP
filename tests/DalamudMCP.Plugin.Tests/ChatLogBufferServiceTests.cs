using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Game.Text;
using DalamudMCP.Plugin.Services;

namespace DalamudMCP.Plugin.Tests;

public sealed class ChatLogBufferServiceTests
{
    private static readonly XivChatRelationKind Normal = XivChatRelationKind.None;
    private static readonly DateTimeOffset BaseTime = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private static ChatLogBufferService CreateService(int capacity = 1000)
    {
        var service = (ChatLogBufferService)RuntimeHelpers.GetUninitializedObject(typeof(ChatLogBufferService));

        var entriesField = typeof(ChatLogBufferService)
            .GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic)!;
        entriesField.SetValue(service, new ConcurrentQueue<ChatLogEntry>());

        var capacityField = typeof(ChatLogBufferService)
            .GetField("maxCapacity", BindingFlags.Instance | BindingFlags.NonPublic)!;
        capacityField.SetValue(service, capacity);

        var disposedField = typeof(ChatLogBufferService)
            .GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic)!;
        disposedField.SetValue(service, false);

        return service;
    }

    private static ChatLogEntry MakeEntry(XivChatType type, DateTimeOffset? timestamp = null, string message = "test")
    {
        return new ChatLogEntry(
            Guid.NewGuid(),
            timestamp ?? BaseTime,
            type,
            type.ToString(),
            0u,
            null,
            message,
            Normal,
            Normal);
    }

    private static void AddEntry(ChatLogBufferService service, ChatLogEntry entry)
    {
        var field = typeof(ChatLogBufferService)
            .GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var queue = (ConcurrentQueue<ChatLogEntry>)field.GetValue(service)!;
        queue.Enqueue(entry);
    }

    [Fact]
    public void FilterByChannel_ReturnsOnlyMatchingChannel()
    {
        ChatLogBufferService service = CreateService();
        AddEntry(service, MakeEntry(XivChatType.Say));
        AddEntry(service, MakeEntry(XivChatType.Party));
        AddEntry(service, MakeEntry(XivChatType.Shout));

        IReadOnlyList<ChatLogEntry> result = service.GetRecent(channels: [XivChatType.Say, XivChatType.Party]);

        Assert.Equal(2, result.Count);
        Assert.All(result, entry => Assert.Contains(entry.Type, new[] { XivChatType.Say, XivChatType.Party }));
    }

    [Fact]
    public void FilterByTimestamp_ReturnsOnlyEntriesAfterSince()
    {
        ChatLogBufferService service = CreateService();
        AddEntry(service, MakeEntry(XivChatType.Say, BaseTime.AddMinutes(-5)));
        AddEntry(service, MakeEntry(XivChatType.Say, BaseTime));
        AddEntry(service, MakeEntry(XivChatType.Say, BaseTime.AddMinutes(5)));

        IReadOnlyList<ChatLogEntry> result = service.GetRecent(since: BaseTime);

        Assert.Equal(2, result.Count);
        Assert.All(result, entry => Assert.True(entry.Timestamp >= BaseTime));
    }

    [Fact]
    public void MaxCount_LimitsResults()
    {
        ChatLogBufferService service = CreateService();
        for (int i = 0; i < 10; i++)
            AddEntry(service, MakeEntry(XivChatType.Say, BaseTime.AddMinutes(i)));

        IReadOnlyList<ChatLogEntry> result = service.GetRecent(maxCount: 3);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void MaxCount_ClampedToMaxAllowed()
    {
        ChatLogBufferService service = CreateService();
        for (int i = 0; i < 600; i++)
            AddEntry(service, MakeEntry(XivChatType.Say, BaseTime.AddMinutes(i)));

        IReadOnlyList<ChatLogEntry> result = service.GetRecent(maxCount: 999);

        Assert.Equal(500, result.Count); // MaxAllowedMaxCount = 500
    }
}
