using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 控件级动画服务实现
/// <para>每帧推进所有 active 的 animate 动画，应用缓动函数。</para>
/// </summary>
public class AnimationService : IAnimationService
{
    /// <inheritdoc/>
    public void Update(double frameDelta, IStateContainer state)
    {
        // 扫描所有 __anim_*_active 标志，推进 active 的动画
        foreach (var key in state.Keys)
        {
            if (key is string sk && sk.EndsWith(StateKeys.Animation.ActiveSuffix) && state.Get<bool>(sk))
            {
                var baseKey = sk[..^7]; // 去掉 StateKeys.Animation.ActiveSuffix
                var elapsed = state.Get<double>(baseKey + StateKeys.Animation.ElapsedSuffix) + frameDelta;
                var duration = state.Get<double>(baseKey + StateKeys.Animation.DurationSuffix);
                var easingStr = state.Get<string>(baseKey + StateKeys.Animation.EasingSuffix) ?? "EaseOutQuad";
                state.Set(baseKey + StateKeys.Animation.ElapsedSuffix, elapsed);

                if (elapsed >= duration)
                {
                    // 检查是否有剩余循环次数
                    var remaining = state.Get<int>(baseKey + StateKeys.Animation.RepeatSuffix);
                    if (remaining != 0)
                    {
                        state.Set(baseKey + StateKeys.Animation.ElapsedSuffix, 0.0);
                        state.Set(baseKey + StateKeys.Animation.FromSuffix, state.Get<double>(baseKey + StateKeys.Animation.TargetSuffix));
                        state.Set(baseKey + StateKeys.Animation.CurrentSuffix, state.Get<double>(baseKey + StateKeys.Animation.TargetSuffix));
                        if (remaining > 0) state.Set(baseKey + StateKeys.Animation.RepeatSuffix, remaining - 1);
                    }
                    else
                    {
                        // 动画结束：设为目标值后清理所有 __anim_* 键
                        state.Set(baseKey + StateKeys.Animation.CurrentSuffix, state.Get<double>(baseKey + StateKeys.Animation.TargetSuffix));
                        state.Set(sk, false);
                        state.Remove(baseKey + StateKeys.Animation.FromSuffix);
                        state.Remove(baseKey + StateKeys.Animation.TargetSuffix);
                        state.Remove(baseKey + StateKeys.Animation.DurationSuffix);
                        state.Remove(baseKey + StateKeys.Animation.EasingSuffix);
                        state.Remove(baseKey + StateKeys.Animation.ElapsedSuffix);
                        state.Remove(baseKey + StateKeys.Animation.CurrentSuffix);
                        state.Remove(baseKey + StateKeys.Animation.RepeatSuffix);
                    }
                }
                else
                {
                    var t = elapsed / duration;
                    var from = state.Get<double>(baseKey + StateKeys.Animation.FromSuffix);
                    var target = state.Get<double>(baseKey + StateKeys.Animation.TargetSuffix);
                    var eased = ApplyEasing(t, easingStr);
                    state.Set(baseKey + StateKeys.Animation.CurrentSuffix, from + (target - from) * eased);
                }
            }
        }
    }

    /// <summary>从字符串解析缓动类型并计算 easing 值</summary>
    private static double ApplyEasing(double t, string easingStr)
    {
        if (!Enum.TryParse<EasingType>(easingStr, out var easing))
            easing = EasingType.EaseOutQuad;
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
            EasingType.EaseInOutBack => t < 0.5 ? 0.5 * (t * 2) * (t * 2) * (2.70158 * (t * 2) - 1.70158) : 0.5 * (((t * 2) - 2) * ((t * 2) - 2) * (2.70158 * ((t * 2) - 2) + 1.70158) + 2),
            EasingType.EaseOutBounce => EaseOutBounce(t),
            EasingType.EaseInBounce => 1 - EaseOutBounce(1 - t),
            EasingType.EaseInOutBounce => t < 0.5 ? (1 - EaseOutBounce(1 - 2 * t)) / 2 : (1 + EaseOutBounce(2 * t - 1)) / 2,
            EasingType.EaseInElastic => t == 0 ? 0 : t == 1 ? 1 : -Math.Pow(2, 10 * t - 10) * Math.Sin((t * 10 - 10.75) * 2.094395102),
            EasingType.EaseOutElastic => t == 0 ? 0 : t == 1 ? 1 : Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * 2.094395102) + 1,
            EasingType.EaseInOutElastic => t == 0 ? 0 : t == 1 ? 1 : t < 0.5 ? -(Math.Pow(2, 20 * t - 10) * Math.Sin((20 * t - 11.125) * 1.396263402)) / 2 : Math.Pow(2, -20 * t + 10) * Math.Sin((20 * t - 11.125) * 1.396263402) / 2 + 1,
            _ => t * (2 - t)
        };
    }

    private static double EaseOutBounce(double t)
    {
        const double n1 = 7.5625;
        const double d1 = 2.75;
        if (t < 1 / d1) return n1 * t * t;
        if (t < 2 / d1) { var t2 = t - 1.5 / d1; return n1 * t2 * t2 + 0.75; }
        if (t < 2.5 / d1) { var t3 = t - 2.25 / d1; return n1 * t3 * t3 + 0.9375; }
        var t4 = t - 2.625 / d1;
        return n1 * t4 * t4 + 0.984375;
    }
}
