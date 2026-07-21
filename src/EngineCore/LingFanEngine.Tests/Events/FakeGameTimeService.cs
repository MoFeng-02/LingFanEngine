using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Tests.Events;

/// <summary>
/// 最小 IGameTimeService 实现，用于 EventScheduler 测试。
/// <para>时间字段可由测试直接赋值；通过 RaiseTimeAdvanced 手动触发 OnTimeAdvanced 事件以驱动时间匹配逻辑。</para>
/// </summary>
public sealed class FakeGameTimeService : IGameTimeService
{
    public long TotalMinutes { get; set; }
    public int CurrentDay { get; set; }
    public int CurrentHour { get; set; }
    public int CurrentMinute { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public bool IsPaused { get; set; }
    public float TimeScale { get; set; } = 1.0f;

    public event Action<GameTimeEventArgs>? OnTimeAdvanced;

    public void Pause() => IsPaused = true;
    public void Resume() => IsPaused = false;
    public void Tick() { }
    public void SkipTime(int minutes) { }
    public void Reset() { }

    /// <summary>
    /// 触发一次时间推进事件，使用当前字段构造参数。
    /// </summary>
    public void RaiseTimeAdvanced()
    {
        OnTimeAdvanced?.Invoke(new GameTimeEventArgs
        {
            TotalMinutes = TotalMinutes,
            CurrentDay = CurrentDay,
            CurrentHour = CurrentHour,
            CurrentMinute = CurrentMinute,
            DayOfWeek = DayOfWeek
        });
    }
}
