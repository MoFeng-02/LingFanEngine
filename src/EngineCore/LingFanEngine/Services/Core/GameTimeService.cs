using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 游戏时间服务实现
/// <para>仅在 LingFanEngineOptions.EnableTimeSystem = true 时注册时间状态变量并推进时间。</para>
/// <para>关闭时 __game_time_* 键不进入状态容器，避免干扰开发者自定义时间逻辑。</para>
/// <para>时间流速由 SecondsPerGameMinute 配置（默认 1.0 = 1 现实秒 = 1 游戏分钟）。</para>
/// <para>起始时间由 TimeStartDay/TimeStartHour/TimeStartMinute 配置。</para>
/// </summary>
public class GameTimeService : IGameTimeService
{
    /// <summary>每天分钟数常量</summary>
    private const int MinutesPerDay = 1440;

    /// <summary>每小时分钟数常量</summary>
    private const int MinutesPerHour = 60;

    /// <summary>游戏累计时间分钟 (long)</summary>
    private const string KeyTotalMinutes = StateKeys.GameTime.TotalMinutes;
    private const string KeyPaused = StateKeys.GameTime.Paused;
    private const string KeyTimeScale = StateKeys.GameTime.Scale;

    private readonly IStateContainer _state;
    private readonly bool _enabled;
    private readonly int _startDay;
    private readonly long _initialMinutes;

    public GameTimeService(IStateContainer state, LingFanEngineOptions options)
    {
        _state = state;
        _enabled = options.EnableTimeSystem;
        _startDay = options.TimeStartDay;
        _initialMinutes = options.TimeStartHour * 60L + options.TimeStartMinute;
        if (!_enabled) return;

        // 初始 TotalMinutes = StartHour*60 + StartMinute（当天内偏移）
        if (!_state.ContainsKey(KeyTotalMinutes))
            _state.Set(KeyTotalMinutes, _initialMinutes);
        if (!_state.ContainsKey(KeyPaused))
            _state.Set(KeyPaused, false);
        if (!_state.ContainsKey(KeyTimeScale))
            _state.Set(KeyTimeScale, 1.0f);
    }

    private long TotalMinutesInternal
    {
        get => _enabled ? _state.Get<long>(KeyTotalMinutes) : 0;
        set { if (_enabled) _state.Set(KeyTotalMinutes, value); }
    }

    public long TotalMinutes => _enabled ? _state.Get<long>(KeyTotalMinutes) : 0;

    /// <summary>
    /// 当前游戏天数（从 TimeStartDay 开始）
    /// <para>CurrentDay = TimeStartDay + (TotalMinutes / MinutesPerDay)</para>
    /// <para>默认 TimeStartDay=1，即从 Day 1 开始。</para>
    /// </summary>
    public int CurrentDay => _startDay + (int)(TotalMinutes / MinutesPerDay);
    public int CurrentHour => (int)((TotalMinutes % MinutesPerDay) / MinutesPerHour);
    public int CurrentMinute => (int)(TotalMinutes % MinutesPerHour);

    /// <summary>
    /// 当前星期几（游戏第 1 天 = Monday，7 天一循环）
    /// <para>公式：(CurrentDay - StartDay) % 7 映射到 DayOfWeek</para>
    /// </summary>
    public DayOfWeek DayOfWeek
    {
        get
        {
            if (!_enabled) return DayOfWeek.Monday;
            // (day - startDay) % 7 → 0=Mon, 1=Tue, ..., 6=Sun
            // 映射到 System.DayOfWeek: Mon=1, Tue=2, ..., Sun=0
            var dow = ((CurrentDay - _startDay) % 7 + 1) % 7;
            return (DayOfWeek)dow;
        }
    }

    public bool IsPaused
    {
        get => _enabled && _state.Get<bool>(KeyPaused);
        set { if (_enabled) _state.Set(KeyPaused, value); }
    }

    public float TimeScale
    {
        get => _enabled ? _state.Get<float>(KeyTimeScale) : 1.0f;
        set { if (_enabled) _state.Set(KeyTimeScale, value); }
    }

    public event Action<GameTimeEventArgs>? OnTimeAdvanced;

    public void Pause() { if (_enabled) IsPaused = true; }
    public void Resume() { if (_enabled) IsPaused = false; }

    public void Tick()
    {
        if (!_enabled || IsPaused) return;
        var newVal = TotalMinutesInternal + 1;
        TotalMinutesInternal = newVal;

        OnTimeAdvanced?.Invoke(new GameTimeEventArgs
        {
            TotalMinutes = newVal,
            CurrentDay = CurrentDay,
            CurrentHour = CurrentHour,
            CurrentMinute = CurrentMinute,
            DayOfWeek = DayOfWeek
        });
    }

    /// <summary>
    /// 批量跳过指定分钟数
    /// <para>逐分钟 Tick，确保中间所有时间事件被检查。</para>
    /// <para>暂停状态下不执行跳跃。</para>
    /// </summary>
    /// <param name="minutes">要跳过的分钟数</param>
    public void SkipTime(int minutes)
    {
        if (!_enabled || minutes <= 0) return;
        for (int i = 0; i < minutes; i++)
        {
            Tick();
        }
    }

    /// <summary>
    /// 重置游戏时间到配置的起始值
    /// <para>将 TotalMinutes 恢复为 TimeStartHour*60+TimeStartMinute，</para>
    /// <para>解除暂停，重置 TimeScale 为 1.0。</para>
    /// <para>用于新开游戏或显式重新开始时间。</para>
    /// </summary>
    public void Reset()
    {
        if (!_enabled) return;
        _state.Set(KeyTotalMinutes, _initialMinutes);
        _state.Set(KeyPaused, false);
        _state.Set(KeyTimeScale, 1.0f);
    }
}
