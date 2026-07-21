using FluentAssertions;
using LingFanEngine.Services.Entry;
using Xunit;

namespace LingFanEngine.Tests.Entry;

/// <summary>
/// EventAggregator 事件聚合器测试
/// <para>验证 publish/subscribe/多订阅/退订/PublishAsync，以及单 handler 异常不中断其他 handler。</para>
/// </summary>
public class EventAggregatorTests
{
    // TEvent 必须为引用类型，定义事件载体类
    private class TestEvent
    {
        public int Value { get; set; }
        public string? Name { get; set; }
    }

    private class OtherEvent
    {
        public bool Flag { get; set; }
    }

    private readonly EventAggregator _aggregator = new();

    [Fact]
    public void Publish_InvokesSubscribedHandler()
    {
        var received = 0;
        _aggregator.Subscribe<TestEvent>(e => received = e.Value);

        _aggregator.Publish(new TestEvent { Value = 42 });

        received.Should().Be(42);
    }

    [Fact]
    public void Publish_InvokesAllSubscribers()
    {
        var a = 0;
        var b = 0;
        _aggregator.Subscribe<TestEvent>(e => a = e.Value);
        _aggregator.Subscribe<TestEvent>(e => b = e.Value);

        _aggregator.Publish(new TestEvent { Value = 7 });

        a.Should().Be(7);
        b.Should().Be(7);
    }

    [Fact]
    public void Publish_OnlyDeliversMatchingEventType()
    {
        var testReceived = 0;
        var otherReceived = 0;
        _aggregator.Subscribe<TestEvent>(_ => testReceived++);
        _aggregator.Subscribe<OtherEvent>(_ => otherReceived++);

        _aggregator.Publish(new TestEvent { Value = 1 });

        testReceived.Should().Be(1);
        otherReceived.Should().Be(0);
    }

    [Fact]
    public void Unsubscribe_StopsDelivery()
    {
        var count = 0;
        var sub = _aggregator.Subscribe<TestEvent>(_ => count++);

        _aggregator.Publish(new TestEvent { Value = 1 });
        sub.Dispose();
        _aggregator.Publish(new TestEvent { Value = 1 });

        count.Should().Be(1);
    }

    [Fact]
    public void Publish_NoSubscribers_DoesNotThrow()
    {
        var act = () => _aggregator.Publish(new TestEvent { Value = 1 });
        act.Should().NotThrow();
    }

    [Fact]
    public void Publish_HandlerThrows_DoesNotBreakOtherHandlers()
    {
        var goodHandlerCalled = 0;
        _aggregator.Subscribe<TestEvent>(_ => throw new InvalidOperationException("boom"));
        _aggregator.Subscribe<TestEvent>(e => goodHandlerCalled = e.Value);

        var act = () => _aggregator.Publish(new TestEvent { Value = 99 });

        act.Should().NotThrow();
        goodHandlerCalled.Should().Be(99);
    }

    [Fact]
    public async Task PublishAsync_InvokesSubscribedHandler()
    {
        var received = 0;
        _aggregator.Subscribe<TestEvent>(e => received = e.Value);

        await _aggregator.PublishAsync(new TestEvent { Value = 123 });

        received.Should().Be(123);
    }

    [Fact]
    public async Task PublishAsync_HandlerThrows_DoesNotBreakOtherHandlers()
    {
        var good = 0;
        _aggregator.Subscribe<TestEvent>(_ => throw new Exception());
        _aggregator.Subscribe<TestEvent>(e => good = e.Value);

        await _aggregator.PublishAsync(new TestEvent { Value = 55 });

        good.Should().Be(55);
    }
}
