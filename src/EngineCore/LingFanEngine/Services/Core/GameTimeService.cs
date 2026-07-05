using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Extensions;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 游戏时间服务实现
/// <para>仅在 LingFanEngineOptions.EnableTimeSystem = true 时注册时间状态变量并推进时间。</para>
/// <para>关闭时 __game_time_* 键不进入状态容器，避免干扰开发者自定义时间逻辑。</para>
/// </summary>
public class GameTimeService : IGameTimeService
{
    /// <summary>游戏累计时间分钟 (long)，1 现实秒 = 1 游戏分钟</summary>
    private const string KeyTotalMinutes = StateKeys.GameTime.TotalMinutes;
    private const string KeyPaused = StateKeys.GameTime.Paused;
    private const string KeyTimeScale = StateKeys.GameTime.Scale;

    private readonly IStateContainer _state;
    private readonly bool _enabled;

    public GameTimeService(IStateContainer state, LingFanEngineOptions options)
    {
        _state = state;
        _enabled = options.EnableTimeSystem;
        if (!_enabled) return;

        if (!_state.ContainsKey(KeyTotalMinutes))
            _state.Set(KeyTotalMinutes, 0L);
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
    public int CurrentDay => (int)(TotalMinutes / 1440);
    public int CurrentHour => (int)((TotalMinutes % 1440) / 60);
    public int CurrentMinute => (int)(TotalMinutes % 60);

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
            CurrentMinute = CurrentMinute
        });
    }
}