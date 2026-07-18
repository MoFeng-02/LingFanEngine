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
/// <para>Phase 63：三模式注销（Normal/Permanent/Temporary）+ RestoreEvent + IsBlocked + 四层注册检查。</para>
/// <para>设计理念：时间事件生命周期——事件一旦注册即独立，场景只是挂载器。</para>
/// </summary>
public class EventScheduler : IEventScheduler
{
    /// <summary>每天分钟数常量</summary>
    private const int MinutesPerDay = 1440;

    /// <summary>旧版导航事件 ID 前缀</summary>
    private const string LegacyNavIdPrefix = "nav_";

    private readonly IGameTimeService _timeService;
    private readonly IStateContainer _state;

    /// <summary>当前活跃的事件</summary>
    private readonly ConcurrentDictionary<string, TimeEventRegistration> _events = new();

    /// <summary>待执行的回调队列</summary>
    private readonly ConcurrentQueue<TimeEventRegistration> _pending = new();

    /// <summary>已触发的单次事件 ID（防止重复触发）</summary>
    private readonly ConcurrentDictionary<string, byte> _firedOneShotIds = new();

    /// <summary>永久销毁的事件 ID（永不注册，即使代码再次执行 set_time_event）</summary>
    private readonly ConcurrentDictionary<string, byte> _destroyedIds = new();

    /// <summary>暂时销毁的事件 ID（不重注册，但可通过 restore_time_event 恢复）</summary>
    private readonly ConcurrentDictionary<string, byte> _suspendedIds = new();

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
    /// <remarks>
    /// Phase 63 四层检查顺序（严格，不可调整）：
    /// 1. Destroyed（永远不可能发生）→ 绝对优先，最强约束
    /// 2. Suspended（暂时挂起）→ 次强约束
    /// 3. FiredOneShot（已发生过）→ 语义约束
    /// 4. TryAdd（已存在）→ 去重
    /// </remarks>
    public bool RegisterEvent(TimeEventRegistration registration)
    {
        // 1. 永久销毁 → 绝对优先，最强约束（"永远不可能发生"）
        if (_destroyedIds.ContainsKey(registration.Id))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[EventScheduler] 事件 [{registration.Id}] 已永久销毁，拒绝注册");
            return false;
        }

        // 2. 暂时销毁 → 次强约束（"暂时挂起，等显式 restore"）
        if (_suspendedIds.ContainsKey(registration.Id))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[EventScheduler] 事件 [{registration.Id}] 已暂时销毁，拒绝注册（需先 restore）");
            return false;
        }

        // 3. 单次事件已触发 → 语义约束（"这件事发生过了"）
        if (registration.IsOneShot && _firedOneShotIds.ContainsKey(registration.Id))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[EventScheduler] 事件 [{registration.Id}] 已触发（单次），跳过注册");
            return false;
        }

        // 4. ID 已存在 → 去重
        if (!_events.TryAdd(registration.Id, registration))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[EventScheduler] 事件 [{registration.Id}] 已注册，跳过重复注册");
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public bool UnregisterEvent(string id) => UnregisterEvent(id, UnregisterMode.Normal);

    /// <inheritdoc/>
    public bool UnregisterEvent(string id, UnregisterMode mode)
    {
        var removed = _events.TryRemove(id, out _);

        switch (mode)
        {
            case UnregisterMode.Permanent:
                _destroyedIds.TryAdd(id, 0);
                // Permanent 模式：标记成功即返回 true（即使事件不在 _events 中）
                return true;
            case UnregisterMode.Temporary:
                _suspendedIds.TryAdd(id, 0);
                // Temporary 模式：标记成功即返回 true（即使事件不在 _events 中）
                return true;
            case UnregisterMode.Normal:
            default:
                // 正常注销：只从 _events 移除，不加任何标记
                // DSL 事件：Run() 重执行 set_time_event 时自然恢复
                // C# 声明式事件：不会随 Run() 重执行恢复（InTimeEvents 不在 Run() 中），
                //   但可通过 restore_time_event 或读档恢复（全局注册表仍保留定义）
                // C# 动态事件：Run() 重执行 SetTimeEventAsync 时自然恢复
                return removed;
        }
    }

    /// <inheritdoc/>
    public bool RestoreEvent(string id)
    {
        return _suspendedIds.TryRemove(id, out _);
    }

    /// <inheritdoc/>
    public bool IsBlocked(string id)
    {
        return _destroyedIds.ContainsKey(id)
            || _suspendedIds.ContainsKey(id)
            || _firedOneShotIds.ContainsKey(id);
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
        _destroyedIds.Clear();
        _suspendedIds.Clear();
    }

    // ========== 出队（DslExecutor 调用） ==========

    /// <inheritdoc/>
    public bool TryDequeuePendingEvent(out TimeEventRegistration? evt)
    {
        return _pending.TryDequeue(out evt);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Phase 63 审计修复（双重修复）：
    /// 1. 先检查 IsOneShot 再移除——防止重复事件被错误移除（原实现 TryRemove 先执行会导致重复事件永久丢失）
    /// 2. 先加入 _firedOneShotIds 再移除 _events——消除竞态窗口
    ///    原顺序（先移除再标记）在两者之间存在窗口，此时 RegisterEvent 的四层检查
    ///    （Destroyed > Suspended > FiredOneShot > TryAdd）会因 FiredOneShot 尚未加入
    ///    且 _events 已移除而通过检查，导致单次事件被重新注册——违背"只触发一次"语义。
    ///    修复后顺序：先 TryAdd 标记 → 再 TryRemove 移除，窗口内 RegisterEvent 会被
    ///    第三层 FiredOneShot 检查阻止。
    /// </remarks>
    public void MarkFired(string eventId)
    {
        // 先检查是否为单次事件
        if (_events.TryGetValue(eventId, out var reg) && reg.IsOneShot)
        {
            // 先标记已触发——阻止竞态窗口内的 RegisterEvent 通过四层检查
            _firedOneShotIds.TryAdd(eventId, 0);
            // 再从活跃事件中移除
            _events.TryRemove(eventId, out _);
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
            FiredOneShotIds = [.. _firedOneShotIds.Keys],
            DestroyedIds = [.. _destroyedIds.Keys],
            SuspendedIds = [.. _suspendedIds.Keys]
        };
    }

    /// <inheritdoc/>
    public void ApplySaveState(TimeEventSaveState? state)
    {
        _events.Clear();
        while (_pending.TryDequeue(out _)) { }
        _firedOneShotIds.Clear();
        _destroyedIds.Clear();
        _suspendedIds.Clear();

        if (state != null)
        {
            foreach (var id in state.FiredOneShotIds)
                _firedOneShotIds.TryAdd(id, 0);
            foreach (var id in state.DestroyedIds)
                _destroyedIds.TryAdd(id, 0);
            foreach (var id in state.SuspendedIds)
                _suspendedIds.TryAdd(id, 0);
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
                    // 重复：每 N 天——第 0 天（游戏开始当天）不触发，
                    // 因为"每 N 天"意味着经过 N 天后才第一次触发
                    : e.Day.Value > 0 && daysSinceStart > 0 && daysSinceStart % e.Day.Value == 0
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
