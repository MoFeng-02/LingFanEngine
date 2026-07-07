using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Entities.Transitions;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 过渡动画引擎
/// <para>所有状态在状态容器中（__transition_*），不维护私有字段。</para>
/// </summary>
public class TransitionEngine : ITransitionEngine
{
    private readonly IStateContainer _state;
    private readonly LingFanEngineOptions _options;

    public TransitionEngine(IStateContainer state, LingFanEngineOptions? options = null)
    {
        _state = state;
        _options = options ?? new  LingFanEngineOptions();
    }

    /// <summary>
    /// 是否为活跃状态（从状态容器读取）
    /// </summary>
    public bool IsActive => _state.Get<bool>(StateKeys.Transition.Active);

    /// <summary>
    /// 开始一个过渡动画
    /// </summary>
    public void StartTransition(TransitionEntity? transition)
    {
        var t = transition ?? new TransitionEntity
        {
            Type = TransitionType.CrossFade,
            Duration = 0.5,
            Easing = EasingType.EaseOutQuad
        };
        _state.Set(StateKeys.Transition.Active, true);
        _state.Set(StateKeys.Transition.Type, t.Type.ToString());
        _state.Set(StateKeys.Transition.Progress, 0.0);
        _state.Set(StateKeys.Transition.Duration, t.Duration);
        _state.Set(StateKeys.Transition.OffsetX, 0.0);
        _state.Set(StateKeys.Transition.OffsetY, 0.0);
        _state.Set(StateKeys.Transition.Scale, 1.0);
        _state.Set(StateKeys.Transition.Easing, t.Easing.ToString());
        _state.Set(StateKeys.Transition.Elapsed, 0.0);
    }

    /// <summary>
    /// 逐帧更新，由 GameLoop 每帧调用
    /// </summary>
    public void Update(double deltaTime)
    {
        if (!_state.Get<bool>(StateKeys.Transition.Active)) return;

        var elapsed = _state.Get<double>(StateKeys.Transition.Elapsed) + deltaTime;
        var duration = _state.Get<double>(StateKeys.Transition.Duration);
        var typeStr = _state.Get<string>(StateKeys.Transition.Type) ?? "CrossFade";

        _state.Set(StateKeys.Transition.Elapsed, elapsed);

        var t = duration > 0 ? Math.Min(elapsed / duration, 1.0) : 1.0;

        if (!Enum.TryParse<TransitionType>(typeStr, true, out var type))
            type = TransitionType.CrossFade;

        var easingStr = _state.Get<string>(StateKeys.Transition.Easing) ?? "EaseOutQuad";
        Enum.TryParse<EasingType>(easingStr, out var easing);
        var eased = ApplyEasing(t, easing);

        switch (type)
        {
            case TransitionType.FadeIn:
            case TransitionType.CrossFade:
                _state.Set(StateKeys.Transition.Progress, eased);
                break;
            case TransitionType.FadeOut:
                _state.Set(StateKeys.Transition.Progress, 1.0 - eased);
                break;
            case TransitionType.SlideLeftIn:
                _state.Set(StateKeys.Transition.OffsetX, -_options.WindowWidth + _options.WindowWidth * eased);
                break;
            case TransitionType.SlideLeftOut:
                _state.Set(StateKeys.Transition.OffsetX, -_options.WindowWidth * eased);
                break;
            case TransitionType.SlideRightIn:
                _state.Set(StateKeys.Transition.OffsetX, _options.WindowWidth - _options.WindowWidth * eased);
                break;
            case TransitionType.SlideRightOut:
                _state.Set(StateKeys.Transition.OffsetX, _options.WindowWidth * eased);
                break;
            case TransitionType.SlideUpIn:
                _state.Set(StateKeys.Transition.OffsetY, -_options.WindowHeight + _options.WindowHeight * eased);
                break;
            case TransitionType.SlideUpOut:
                _state.Set(StateKeys.Transition.OffsetY, -_options.WindowHeight * eased);
                break;
            case TransitionType.SlideDownIn:
                _state.Set(StateKeys.Transition.OffsetY, _options.WindowHeight - _options.WindowHeight * eased);
                break;
            case TransitionType.SlideDownOut:
                _state.Set(StateKeys.Transition.OffsetY, _options.WindowHeight * eased);
                break;
            case TransitionType.ZoomIn:
                _state.Set(StateKeys.Transition.Scale, 0.5 + 0.5 * eased);
                _state.Set(StateKeys.Transition.Progress, eased);
                break;
            case TransitionType.ZoomOut:
                _state.Set(StateKeys.Transition.Scale, 1.0 - 0.5 * eased);
                _state.Set(StateKeys.Transition.Progress, eased);
                break;
            case TransitionType.BlinkOut:
                // 快速闪烁 4 周期，亮度震荡衰减
                var blink = Math.Abs(Math.Cos(t * Math.PI * 4)) * (1.0 - t);
                _state.Set(StateKeys.Transition.Progress, blink);
                break;
            default:
                _state.Set(StateKeys.Transition.Progress, eased);
                break;
        }

        if (t >= 1.0)
        {
            _state.Remove(StateKeys.Transition.Type);
            _state.Remove(StateKeys.Transition.Easing);
            _state.Remove(StateKeys.Transition.Duration);
            _state.Set(StateKeys.Transition.Active, false);
            _state.Set(StateKeys.Transition.Progress, 1.0);
            _state.Set(StateKeys.Transition.OffsetX, 0.0);
            _state.Set(StateKeys.Transition.OffsetY, 0.0);
            _state.Set(StateKeys.Transition.Scale, 1.0);
            _state.Set(StateKeys.Transition.Elapsed, 0.0);
        }
    }

    /// <summary>
    /// 立即结束过渡
    /// </summary>
    public void CompleteTransition()
    {
        _state.Remove(StateKeys.Transition.Type);
        _state.Remove(StateKeys.Transition.Easing);
        _state.Remove(StateKeys.Transition.Duration);
        _state.Set(StateKeys.Transition.Active, false);
        _state.Set(StateKeys.Transition.Progress, 1.0);
        _state.Set(StateKeys.Transition.OffsetX, 0.0);
        _state.Set(StateKeys.Transition.OffsetY, 0.0);
        _state.Set(StateKeys.Transition.Scale, 1.0);
        _state.Set(StateKeys.Transition.Elapsed, 0.0);
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
            EasingType.EaseInOutBack => t < 0.5
                ? 0.5 * (t * 2) * (t * 2) * (2.70158 * (t * 2) - 1.70158)
                : 0.5 * (((t * 2) - 2) * ((t * 2) - 2) * (2.70158 * ((t * 2) - 2) + 1.70158) + 2),
            EasingType.EaseInElastic => t == 0 ? 0 : t == 1 ? 1
                : -Math.Pow(2, 10 * t - 10) * Math.Sin((t * 10 - 10.75) * 2.094395102),
            EasingType.EaseOutElastic => t == 0 ? 0 : t == 1 ? 1
                : Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * 2.094395102) + 1,
            EasingType.EaseInOutElastic => t == 0 ? 0 : t == 1 ? 1
                : t < 0.5
                    ? -(Math.Pow(2, 20 * t - 10) * Math.Sin((20 * t - 11.125) * 1.396263402)) / 2
                    : Math.Pow(2, -20 * t + 10) * Math.Sin((20 * t - 11.125) * 1.396263402) / 2 + 1,
            EasingType.EaseInBounce => 1 - EaseOutBounce(1 - t),
            EasingType.EaseOutBounce => EaseOutBounce(t),
            EasingType.EaseInOutBounce => t < 0.5
                ? (1 - EaseOutBounce(1 - 2 * t)) / 2
                : (1 + EaseOutBounce(2 * t - 1)) / 2,
            _ => t * (2 - t)
        };
    }
}
