using LingFanEngine.Abstractions.Interfaces.Core;
// Tween 类已迁移至 Abstractions/Interfaces/Core/ITweenEngine.cs
// EasingType 移至 Abstractions/Interfaces/Core/EasingType.cs

namespace LingFanEngine.Services.Tweens;

/// <summary>
/// 补间引擎
/// <para>管理 Tween 队列，主循环每帧调用 Update，结果直接写入状态容器。</para>
/// </summary>
public class TweenEngine : ITweenEngine
{
    private readonly IStateContainer _state;
    private readonly List<Tween> _activeTweens = [];
    private readonly List<Tween> _pendingTweens = [];

    /// <summary>
    /// 当前活跃补间数量
    /// </summary>
    public int ActiveCount => _activeTweens.Count;

    /// <summary>
    /// 构造函数
    /// </summary>
    public TweenEngine(IStateContainer state)
    {
        _state = state;
    }

    /// <summary>
    /// 添加补间动画
    /// </summary>
    public void AddTween(Tween tween)
    {
        if (tween.Delay > 0)
        {
            _pendingTweens.Add(tween);
        }
        else
        {
            _activeTweens.Add(tween);
        }
    }

    /// <summary>
    /// 逐帧更新，由 GameLoop 调用
    /// </summary>
    /// <param name="deltaTime">帧时间差（秒）</param>
    /// <param name="timeScale">时间缩放系数</param>
    public void Update(double deltaTime, float timeScale)
    {
        var scaledDelta = deltaTime * timeScale;

        // 处理延迟队列
        for (int i = _pendingTweens.Count - 1; i >= 0; i--)
        {
            var tween = _pendingTweens[i];
            tween.DelayElapsed += scaledDelta;
            if (tween.DelayElapsed >= tween.Delay)
            {
                _pendingTweens.RemoveAt(i);
                _activeTweens.Add(tween);
            }
        }

        // 更新活跃补间
        for (int i = _activeTweens.Count - 1; i >= 0; i--)
        {
            var tween = _activeTweens[i];
            tween.Elapsed += scaledDelta;

            if (tween.Elapsed >= tween.Duration)
            {
                // 最终值
                _state.Set(tween.TargetKey, tween.To);
                if (tween.TargetKeyY != null && tween.ToY.HasValue)
                    _state.Set(tween.TargetKeyY, tween.ToY.Value);

                _activeTweens.RemoveAt(i);
            }
            else
            {
                var t = tween.Elapsed / tween.Duration;
                var eased = ApplyEasing(t, tween.Easing);
                var value = tween.From + (tween.To - tween.From) * eased;

                _state.Set(tween.TargetKey, value);

                if (tween.TargetKeyY != null && tween.ToY.HasValue && tween.FromY.HasValue)
                {
                    var valueY = tween.FromY.Value + (tween.ToY.Value - tween.FromY.Value) * eased;
                    _state.Set(tween.TargetKeyY, valueY);
                }
            }
        }
    }

    /// <summary>
    /// 清除所有补间
    /// </summary>
    public void Clear()
    {
        _activeTweens.Clear();
        _pendingTweens.Clear();
    }

    /// <summary>
    /// 缓动函数计算
    /// </summary>
    private static double ApplyEasing(double t, EasingType easing)
    {
        return easing switch
        {
            EasingType.Linear => t,

            EasingType.EaseInQuad => t * t,
            EasingType.EaseOutQuad => t * (2 - t),
            EasingType.EaseInOutQuad => t < 0.5 ? 2 * t * t : -1 + (4 - 2 * t) * t,

            EasingType.EaseInCubic => t * t * t,
            EasingType.EaseOutCubic => (t - 1) * (t - 1) * (t - 1) + 1,
            EasingType.EaseInOutCubic => t < 0.5 ? 4 * t * t * t : (t - 1) * (2 * t - 2) * (2 * t - 2) + 1,

            EasingType.EaseInBack => t * t * (2.70158 * t - 1.70158),
            EasingType.EaseOutBack => (t - 1) * (t - 1) * (2.70158 * (t - 1) + 1.70158) + 1,
            EasingType.EaseInOutBack => EaseInOutBack(t),
            EasingType.EaseOutElastic => EaseOutElastic(t),
            EasingType.EaseInElastic => EaseInElastic(t),

            EasingType.EaseOutBounce => BounceOut(t),
            EasingType.EaseInBounce => 1 - BounceOut(1 - t),
            EasingType.EaseInOutBounce => t < 0.5
                ? (1 - BounceOut(1 - 2 * t)) / 2
                : (1 + BounceOut(2 * t - 1)) / 2,

            _ => t
        };
    }

    private static double EaseInOutBack(double t)
    {
        const double c1 = 1.70158;
        const double c2 = c1 * 1.525;
        return t < 0.5
            ? (2 * t) * (2 * t) * ((c2 + 1) * 2 * t - c2) / 2
            : ((2 * t - 2) * (2 * t - 2) * ((c2 + 1) * (t * 2 - 2) + c2) + 2) / 2;
    }

    private static double EaseOutElastic(double t)
    {
        const double c4 = 2 * Math.PI / 3;
        return t == 0 ? 0 : (t >= 1 ? 1 : Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * c4) + 1);
    }

    private static double EaseInElastic(double t)
    {
        const double c4 = 2 * Math.PI / 3;
        return t == 0 ? 0 : (t >= 1 ? 1 : -Math.Pow(2, 10 * t - 10) * Math.Sin((t * 10 - 10.75) * c4));
    }

    private static double BounceOut(double t)
    {
        const double n1 = 7.5625;
        const double d1 = 2.75;
        if (t < 1 / d1) return n1 * t * t;
        if (t < 2 / d1) return n1 * (t -= 1.5 / d1) * t + 0.75;
        if (t < 2.5 / d1) return n1 * (t -= 2.25 / d1) * t + 0.9375;
        return n1 * (t -= 2.625 / d1) * t + 0.984375;
    }
}
