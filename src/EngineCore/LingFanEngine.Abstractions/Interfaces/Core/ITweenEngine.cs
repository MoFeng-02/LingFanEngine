namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 补间引擎接口
/// <para>管理 Tween 队列，主循环每帧调用 Update，结果直接写入状态容器。</para>
/// </summary>
public interface ITweenEngine
{
    /// <summary>当前活跃补间数量</summary>
    int ActiveCount { get; }

    /// <summary>添加补间动画</summary>
    void AddTween(Tween tween);

    /// <summary>逐帧更新，由 GameLoop 调用</summary>
    /// <param name="deltaTime">帧时间差（秒）</param>
    /// <param name="timeScale">时间缩放系数</param>
    void Update(double deltaTime, float timeScale);

    /// <summary>清除所有补间</summary>
    void Clear();
}

/// <summary>
/// 补间动画——描述一个值从起始到结束的插值过程
/// </summary>
public class Tween
{
    /// <summary>状态容器中的 Key</summary>
    public required string TargetKey { get; init; }

    /// <summary>起始值</summary>
    public required double From { get; init; }

    /// <summary>目标值</summary>
    public required double To { get; init; }

    /// <summary>持续时间（秒）</summary>
    public required double Duration { get; init; }

    /// <summary>已过去的插值时间</summary>
    public double Elapsed { get; set; }

    /// <summary>缓动函数</summary>
    public EasingType Easing { get; init; } = EasingType.Linear;

    /// <summary>延迟开始时间</summary>
    public double Delay { get; init; }

    /// <summary>延迟计时器</summary>
    public double DelayElapsed { get; set; }

    /// <summary>是否已完成</summary>
    public bool IsCompleted => Elapsed >= Duration;

    /// <summary>补间值 x、y 坐标用（可选）</summary>
    public string? TargetKeyY { get; init; }

    /// <summary>Y 起始值</summary>
    public double? FromY { get; init; }

    /// <summary>Y 目标值</summary>
    public double? ToY { get; init; }
}
