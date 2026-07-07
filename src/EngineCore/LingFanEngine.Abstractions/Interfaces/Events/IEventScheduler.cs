using System;
using LingFanEngine.Abstractions.Entities.Events;

namespace LingFanEngine.Abstractions.Interfaces.Events;

/// <summary>
/// 事件调度器接口
/// <para>监听 IGameTimeService 的 OnTimeAdvanced 事件，检查 TimeEventEntity 是否到期并触发导航。</para>
/// </summary>
public interface IEventScheduler : IDisposable
{
    /// <summary>注册时间事件</summary>
    void RegisterEvent(TimeEventEntity evt);

    /// <summary>批量注册时间事件</summary>
    void RegisterEvents(System.Collections.Generic.IEnumerable<TimeEventEntity> events);

    /// <summary>移除时间事件</summary>
    bool RemoveEvent(TimeEventEntity evt);

    /// <summary>清除所有时间事件</summary>
    void ClearEvents();

    /// <summary>当前已注册事件数量</summary>
    int EventCount { get; }
}
