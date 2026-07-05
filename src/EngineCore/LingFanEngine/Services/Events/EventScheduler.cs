using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Events;

/// <summary>
/// 事件调度器
/// <para>监听 GameTimeService 的 OnTimeAdvanced 事件，检查 TimeEventEntity 是否到期并触发导航。</para>
/// <para>支持可选的条件表达式：时间条件满足后，通过 DslExpressionEvaluator 求值 Condition 字段。</para>
/// </summary>
public class EventScheduler : IDisposable
{
    private readonly IGameTimeService _timeService;
    private readonly ICommandPipeline _pipeline;
    private readonly IStateContainer _state;
    private readonly List<TimeEventEntity> _events = [];
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="timeService">游戏时间服务</param>
    /// <param name="pipeline">命令管道（用于发送导航命令）</param>
    /// <param name="state">状态容器（用于条件表达式求值）</param>
    public EventScheduler(IGameTimeService timeService, ICommandPipeline pipeline, IStateContainer state)
    {
        _timeService = timeService;
        _pipeline = pipeline;
        _state = state;
        _timeService.OnTimeAdvanced += OnTimeAdvanced;
    }

    /// <summary>
    /// 注册事件
    /// </summary>
    public void RegisterEvent(TimeEventEntity evt)
    {
        lock (_lock)
        {
            _events.Add(evt);
        }
    }

    /// <summary>
    /// 批量注册事件
    /// </summary>
    public void RegisterEvents(IEnumerable<TimeEventEntity> events)
    {
        lock (_lock)
        {
            _events.AddRange(events);
        }
    }

    /// <summary>
    /// 移除事件
    /// </summary>
    public bool RemoveEvent(TimeEventEntity evt)
    {
        lock (_lock)
        {
            return _events.Remove(evt);
        }
    }

    /// <summary>
    /// 清除所有事件
    /// </summary>
    public void ClearEvents()
    {
        lock (_lock)
        {
            _events.Clear();
        }
    }

    /// <summary>
    /// 获取当前已注册事件数量
    /// </summary>
    public int EventCount
    {
        get
        {
            lock (_lock)
            {
                return _events.Count;
            }
        }
    }

    private void OnTimeAdvanced(GameTimeEventArgs args)
    {
        if (_disposed) return;

        var day = args.CurrentDay;
        var hour = args.CurrentHour;
        var minute = args.CurrentMinute;

        // 快照当前事件列表（避免遍历时修改）
        List<TimeEventEntity> snapshot;
        lock (_lock)
        {
            snapshot = _events.ToList();
        }

        // 按优先级排序，只检查当前时间点的事件
        var triggered = snapshot
            .Where(e => e.TriggerDay == day)
            .Where(e => e.TriggerHour == null || e.TriggerHour == hour)
            .Where(e => e.TriggerMinute == null || e.TriggerMinute == minute)
            .OrderByDescending(e => e.Priority)
            .ToList();

        var toRemove = new List<TimeEventEntity>();

        foreach (var evt in triggered)
        {
            // 检查条件表达式（如果有）
            if (!string.IsNullOrWhiteSpace(evt.Condition))
            {
                try
                {
                    if (!DslExpressionEvaluator.EvaluateBool(evt.Condition, _state))
                        continue; // 条件不满足，跳过
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[EventScheduler] Condition evaluation failed for event '{evt.Description ?? evt.TargetPath}': {ex.Message}");
                    continue; // 条件求值出错，安全跳过
                }
            }

            // 触发导航
            if (!string.IsNullOrEmpty(evt.TargetPath))
            {
                _pipeline.SendAsync(new NavigateCommand
                {
                    Path = evt.TargetPath,
                    Priority = CommandPriority.High
                });
            }

            // 一次性事件标记移除
            if (evt.IsOneShot)
            {
                toRemove.Add(evt);
            }
        }

        // 移除已触发的一次性事件
        if (toRemove.Count > 0)
        {
            lock (_lock)
            {
                foreach (var evt in toRemove)
                {
                    _events.Remove(evt);
                }
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _timeService.OnTimeAdvanced -= OnTimeAdvanced;
            _disposed = true;
        }
    }
}
