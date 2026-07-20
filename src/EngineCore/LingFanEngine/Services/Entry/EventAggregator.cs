using System.Collections.Concurrent;
using System.Collections.Immutable;
using LingFanEngine.Abstractions.Interfaces.Entry;

namespace LingFanEngine.Services.Entry;

/// <summary>
/// 简单的事件聚合器（Phase 64：无锁 ImmutableArray 替代 List+lock）
/// </summary>
public class EventAggregator : IEventAggregator
{
    private readonly ConcurrentDictionary<Type, ImmutableArray<Delegate>> _handlers = new();

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        var key = typeof(TEvent);
        _handlers.AddOrUpdate(
            key,
            ImmutableArray.Create<Delegate>(handler),
            (_, existing) => existing.Add(handler));

        return new Subscription(() =>
            _handlers.AddOrUpdate(
                key,
                ImmutableArray<Delegate>.Empty,
                (_, existing) => existing.Remove(handler)));
    }

    public void Publish<TEvent>(TEvent evt) where TEvent : class
    {
        if (_handlers.TryGetValue(typeof(TEvent), out var handlers))
        {
            // ImmutableArray 无需快照——遍历的是不可变快照
            // 逐个调用，单个 handler 异常不中断其他 handler
            foreach (var handler in handlers)
            {
                try { ((Action<TEvent>)handler)(evt); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[EventAggregator] Publish handler failed: {ex.Message}"); }
            }
        }
    }

    public Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : class
    {
        if (_handlers.TryGetValue(typeof(TEvent), out var handlers))
        {
            // 直接同步调用——handler 是 Action<TEvent>，无需 Task.RunAsync
            // 逐个调用，单个 handler 异常不中断其他 handler
            foreach (var handler in handlers)
            {
                try { ((Action<TEvent>)handler)(evt); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[EventAggregator] PublishAsync handler failed: {ex.Message}"); }
            }
        }
        return Task.CompletedTask;
    }

    private class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
