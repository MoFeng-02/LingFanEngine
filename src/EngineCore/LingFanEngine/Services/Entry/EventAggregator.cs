using System.Collections.Concurrent;
using LingFanEngine.Abstractions.Interfaces.Entry;

namespace LingFanEngine.Services.Entry;

/// <summary>
/// 简单的事件聚合器
/// </summary>
public class EventAggregator : IEventAggregator
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        var handlers = _handlers.GetOrAdd(typeof(TEvent), _ => new List<Delegate>());
        lock (handlers)
        {
            handlers.Add(handler);
        }
        return new Subscription(() =>
        {
            lock (handlers)
            {
                handlers.Remove(handler);
            }
        });
    }

    public void Publish<TEvent>(TEvent evt) where TEvent : class
    {
        if (_handlers.TryGetValue(typeof(TEvent), out var handlers))
        {
            List<Delegate> snapshot;
            lock (handlers)
            {
                snapshot = handlers.ToList();
            }
            foreach (var handler in snapshot)
            {
                ((Action<TEvent>)handler)(evt);
            }
        }
    }

    public async Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : class
    {
        if (_handlers.TryGetValue(typeof(TEvent), out var handlers))
        {
            List<Delegate> snapshot;
            lock (handlers)
            {
                snapshot = handlers.ToList();
            }
            var tasks = snapshot.Select(handler => Task.Run(() => ((Action<TEvent>)handler)(evt), ct)).ToList();
            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
        }
    }

    private class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}