using DalamudMCP.Plugin.Ipc;

namespace DalamudMCP.Plugin.Operations.Tests;

public sealed class ReflectionPluginCallGateSubscriberTests
{
    [Fact]
    public void Constructor_caches_HasFunction_and_InvokeFunc_for_valid_subscriber()
    {
        DummySubscriber dummy = new() { HasFunction = true };

        ReflectionPluginCallGateSubscriber subscriber = new(dummy);

        Assert.True(subscriber.HasFunction);
        Assert.Equal("hello:42", subscriber.InvokeFunc(["hello", 42]));
    }

    [Fact]
    public void HasFunction_returns_subscriber_actual_value()
    {
        DummySubscriber dummyWithFunc = new() { HasFunction = true };
        DummySubscriber dummyWithoutFunc = new() { HasFunction = false };

        ReflectionPluginCallGateSubscriber subTrue = new(dummyWithFunc);
        ReflectionPluginCallGateSubscriber subFalse = new(dummyWithoutFunc);

        Assert.True(subTrue.HasFunction);
        Assert.False(subFalse.HasFunction);
    }

    [Fact]
    public void InvokeFunc_calls_subscriber_and_returns_result()
    {
        DummySubscriber dummy = new() { HasFunction = true };
        ReflectionPluginCallGateSubscriber subscriber = new(dummy);

        object? result = subscriber.InvokeFunc(["alpha", 123]);

        Assert.Equal("alpha:123", result);
    }

    [Fact]
    public void Constructor_throws_ArgumentNullException_for_null_subscriber()
    {
        Assert.Throws<ArgumentNullException>(() => new ReflectionPluginCallGateSubscriber(null!));
    }

    [Fact]
    public void Constructor_throws_InvalidOperationException_when_subscriber_lacks_HasFunction()
    {
        object invalid = new SubscriberWithoutHasFunction();

        Assert.Throws<InvalidOperationException>(() => new ReflectionPluginCallGateSubscriber(invalid));
    }

    [Fact]
    public void Constructor_throws_InvalidOperationException_when_subscriber_lacks_InvokeFunc()
    {
        object invalid = new SubscriberWithoutInvokeFunc();

        Assert.Throws<InvalidOperationException>(() => new ReflectionPluginCallGateSubscriber(invalid));
    }

    [Fact]
    public void InvokeFunc_throws_ArgumentNullException_for_null_arguments()
    {
        DummySubscriber dummy = new() { HasFunction = true };
        ReflectionPluginCallGateSubscriber subscriber = new(dummy);

        Assert.Throws<ArgumentNullException>(() => subscriber.InvokeFunc(null!));
    }

    /// <summary>
    /// 测试用 subscriber 类，包含 HasFunction 属性和 InvokeFunc 方法。
    /// </summary>
    private sealed class DummySubscriber
    {
        public bool HasFunction { get; set; } = true;

#pragma warning disable CA1822 // 必须是实例方法，供 ReflectionPluginCallGateSubscriber 反射调用
        public string InvokeFunc(string name, int count) => $"{name}:{count}";
#pragma warning restore CA1822
    }

    /// <summary>
    /// 缺少 HasFunction 属性的类。
    /// </summary>
    private sealed class SubscriberWithoutHasFunction
    {
        public static object? InvokeFunc(params object?[] args) => null;
    }

    /// <summary>
    /// 缺少 InvokeFunc 方法的类。
    /// </summary>
    private sealed class SubscriberWithoutInvokeFunc
    {
        public bool HasFunction { get; set; }
    }
}
