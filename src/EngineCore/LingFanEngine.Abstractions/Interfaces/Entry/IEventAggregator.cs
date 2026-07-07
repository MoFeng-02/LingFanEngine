using System;
using System.Threading;
using System.Threading.Tasks;

namespace LingFanEngine.Abstractions.Interfaces.Entry;

/// <summary>
/// 事件聚合器接口
/// <para>提供发布/订阅模式的事件通信，解跨模块通信耦合。</para>
/// </summary>
public interface IEventAggregator
{
    /// <summary>订阅事件</summary>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;

    /// <summary>发布事件（同步）</summary>
    void Publish<TEvent>(TEvent evt) where TEvent : class;

    /// <summary>发布事件（异步）</summary>
    Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : class;
}
