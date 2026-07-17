using System.Collections.Concurrent;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Events;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Services.Events;

/// <summary>
/// 事件调度器（回调驱动）
/// <para>监听 GameTimeService 的 OnTimeAdvanced 事件，匹配时间条件后将事件入队。</para>
/// <para>DslExecutor 在命令循环顶部调用 TryDequeuePendingEvent 出队并执行回调。</para>
/// <para>支持 C# Func&lt;Task&gt; 回调和 DSL ICommand[] 两种形式。</para>
/// <para>事件持久存活——不自动清理，只能通过 UnregisterEvent(id) 手动删除或单次触发后自动移除。</para>
/// <para>支持 ID 去重和单次事件已触发标记，存档时持久化 ID 集合。</para>
/// </summary>
public class EventScheduler : IEventScheduler
{
    /// <summary>每天分钟数常量</summary>
    private const int MinutesPerDay = 1440;

    /// <summary>旧版导航事件 ID 前缀</summary>
    private const string LegacyNavIdPrefix = "nav_";

    private readonly IGameTimeService _timeService;
    private readonly IStateContainer _state;
    private readonly ConcurrentDictionary<string, TimeEventRegistration> _events = new();
    private readonly ConcurrentQueue<TimeEventRegistration> _pending = new();
    private readonly ConcurrentDictionary<string, byte> _firedOneShotIds = new();
    private bool _disposed;

    /// <summary>
    /// 构造函数
    /// </summary>
    public EventScheduler(IGameTimeService timeService, IStateContainer state)
    {
        _timeService = timeService;
        _state = state;
        _timeService.OnTimeAdvanced += OnTimeAdvanced;
    }

    // ========== 注册（回调驱动） ==========

    /// <inheritdoc/>
    public bool RegisterEvent(TimeEventRegistration registration)
    {
        // 单次事件已触发 → 跳过
        if (registration.IsOneShot && _firedOneShotIds.ContainsKey(registration.Id))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[EventScheduler] 事件 [{registration.Id}] 已触发（单次），跳过注册");
            return false;
        }

        // ID 已存在 → 跳过
        if (!_events.TryAdd(registration.Id, registration))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[EventScheduler] 事件 [{registration.Id}] 已注册，跳过重复注册");
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public bool UnregisterEvent(string id)
    {
        return _events.TryRemove(id, out _);
    }

    // ========== 兼容旧 API（导航驱动） ==========

    /// <inheritdoc/>
    public void RegisterEvent(TimeEventEntity evt)
    {
        var id = string.IsNullOrEmpty(evt.TargetPath)
            ? $"{LegacyNavIdPrefix}{evt.TriggerDay}_{evt.TriggerHour}_{evt.TriggerMinute}_{Guid.NewGuid():N}"
            : $"{LegacyNavIdPrefix}{evt.TargetPath}_{evt.TriggerDay}_{evt.TriggerHour}_{evt.TriggerMinute}";

        var reg = new TimeEventRegistration
        {
            Id = id,
            Hour = evt.TriggerHour ?? 0,
            Minute = evt.TriggerMinute,
            // Day 仅在 DaysOfWeek 为空时生效（与 TimeEventEntity 文档一致：
            // "仅当 DaysOfWeek 为空时生效"）
            Day = evt.TriggerDay > 0 && evt.DaysOfWeek == null ? evt.TriggerDay : null,
            DaysOfWeek = evt.DaysOfWeek,
            IsOneShot = evt.IsOneShot,
            Commands = [new NavigateCommand { Path = evt.TargetPath ?? "", Priority = CommandPriority.High }],
            Condition = evt.Condition,
            Description = evt.Description,
            IsLegacyNavigation = true
        };

        RegisterEvent(reg);
    }

    /// <inheritdoc/>
    public void RegisterEvents(IEnumerable<TimeEventEntity> events)
    {
        foreach (var evt in events)
            RegisterEvent(evt);
    }

    // ========== 清理 ==========

    /// <inheritdoc/>
    public void ClearEvents()
    {
        _events.Clear();
        while (_pending.TryDequeue(out _)) { }
        _firedOneShotIds.Clear();
    }

    // ========== 出队（DslExecutor 调用） ==========

    /// <inheritdoc/>
    public bool TryDequeuePendingEvent(out TimeEventRegistration? evt)
    {
        return _pending.TryDequeue(out evt);
    }

    /// <inheritdoc/>
    public void MarkFired(string eventId)
    {
        if (_events.TryRemove(eventId, out var reg) && reg.IsOneShot)
        {
            _firedOneShotIds.TryAdd(eventId, 0);
        }
    }

    // ========== 查询 ==========

    /// <inheritdoc/>
    public int EventCount => _events.Count;

    /// <inheritdoc/>
    public IReadOnlyList<TimeEventEntity> GetRegisteredEvents()
    {
        // 只返回旧版导航驱动事件——回调驱动事件不通过 TimeEvents 序列化
        // （它们的持久化通过 TimeEventSaveState 的 ID 集合 + 场景初始化重注册完成）
        return _events.Values
            .Where(e => e.IsLegacyNavigation)
            .Select(e =>
            {
                // 从 Commands[0]（NavigateCommand）提取原始导航目标
                string targetPath = "";
                if (e.Commands != null && e.Commands.Count > 0 && e.Commands[0] is NavigateCommand nav)
                {
                    targetPath = nav.Path;
                }

                return new TimeEventEntity
                {
                    TriggerDay = e.Day ?? 0,
                    DaysOfWeek = e.DaysOfWeek,
                    TriggerHour = e.Hour,
                    TriggerMinute = e.Minute,
                    TargetPath = targetPath,
                    IsOneShot = e.IsOneShot,
                    Condition = e.Condition,
                    Description = e.Description
                };
            }).ToList();
    }

    // ========== 存档 ==========

    /// <inheritdoc/>
    public TimeEventSaveState GetSaveState()
    {
        return new TimeEventSaveState
        {
            RegisteredIds = [.. _events.Keys],
            FiredOneShotIds = [.. _firedOneShotIds.Keys]
        };
    }

    /// <inheritdoc/>
    public void ApplySaveState(TimeEventSaveState? state)
    {
        _events.Clear();
        while (_pending.TryDequeue(out _)) { }
        _firedOneShotIds.Clear();

        if (state != null)
        {
            foreach (var id in state.FiredOneShotIds)
                _firedOneShotIds.TryAdd(id, 0);
            // RegisteredIds 不直接恢复——场景初始化时重新注册回调
            // 未重新注册的 ID 自然不在 _events 中，即被当作垃圾丢弃
        }
    }

    // ========== 内部：时间匹配 ==========

    private void OnTimeAdvanced(GameTimeEventArgs args)
    {
        if (_disposed) return;

        var hour = args.CurrentHour;
        var minute = args.CurrentMinute;
        var dayOfWeek = args.DayOfWeek;
        var currentDay = args.CurrentDay;
        // 从游戏开始算起的天数（第 0 天 = 游戏第一天）
        var daysSinceStart = args.TotalMinutes / MinutesPerDay;

        _state.Set(StateKeys.GameTime.CurrentDay, currentDay);

        var matched = _events.Values
            // 小时匹配
            .Where(e => e.Hour == hour)
            // 分钟匹配：null=整点（minute==0 时触发），指定值=精确匹配
            .Where(e => (e.Minute == null && minute == 0) || e.Minute == minute)
            // 天匹配：null=不限制，IsOneShot=true=绝对天数，IsOneShot=false=间隔天数
            .Where(e => !e.Day.HasValue || (
                e.IsOneShot
                    ? currentDay == e.Day.Value            // 单次：指定第 N 天
                    : e.Day.Value > 0 && daysSinceStart % e.Day.Value == 0  // 重复：每 N 天
            ))
            // 星期匹配
            .Where(e => e.DaysOfWeek == null || e.DaysOfWeek.Length == 0 ||
                        Array.IndexOf(e.DaysOfWeek, dayOfWeek) >= 0)
            .OrderByDescending(e => e.Priority)
            .ToList();

        foreach (var evt in matched)
        {
            _pending.Enqueue(evt);
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
