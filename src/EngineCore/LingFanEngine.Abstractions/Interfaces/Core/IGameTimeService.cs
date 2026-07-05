namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 游戏时间服务接口
/// <para>1 现实秒 = 1 游戏分钟，主循环 Tick 推进。</para>
/// </summary>
public interface IGameTimeService
{
    /// <summary>
    /// 当前游戏总分钟数
    /// </summary>
    long TotalMinutes { get; }

    /// <summary>
    /// 当前游戏天数（基于 TotalMinutes / 1440）
    /// </summary>
    int CurrentDay { get; }

    /// <summary>
    /// 当前游戏小时（0~23）
    /// </summary>
    int CurrentHour { get; }

    /// <summary>
    /// 当前游戏分钟（0~59）
    /// </summary>
    int CurrentMinute { get; }

    /// <summary>
    /// 是否暂停
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// 暂停时间推进
    /// </summary>
    void Pause();

    /// <summary>
    /// 恢复时间推进
    /// </summary>
    void Resume();

    /// <summary>
    /// 时间缩放系数
    /// </summary>
    float TimeScale { get; set; }

    /// <summary>
    /// 推进一游戏分钟（由主循环调用）
    /// </summary>
    void Tick();

    /// <summary>
    /// 时间推进事件：每 Tick 后触发
    /// </summary>
    event Action<GameTimeEventArgs>? OnTimeAdvanced;
}

/// <summary>
/// 时间推进事件参数
/// </summary>
public class GameTimeEventArgs : EventArgs
{
    /// <summary>
    /// 当前游戏总分钟数
    /// </summary>
    public long TotalMinutes { get; init; }

    /// <summary>
    /// 当前游戏天数
    /// </summary>
    public int CurrentDay { get; init; }

    /// <summary>
    /// 当前小时
    /// </summary>
    public int CurrentHour { get; init; }

    /// <summary>
    /// 当前分钟
    /// </summary>
    public int CurrentMinute { get; init; }
}
