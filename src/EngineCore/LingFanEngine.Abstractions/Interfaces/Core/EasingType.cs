namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 缓动函数类型
/// <para>定义在 Abstractions 层以便 TransitionEntity 使用。</para>
/// </summary>
public enum EasingType
{
    Linear,
    EaseInQuad, EaseOutQuad, EaseInOutQuad,
    EaseInCubic, EaseOutCubic, EaseInOutCubic,
    EaseInBack, EaseOutBack, EaseInOutBack,
    EaseInElastic, EaseOutElastic, EaseInOutElastic,
    EaseInBounce, EaseOutBounce, EaseInOutBounce
}
