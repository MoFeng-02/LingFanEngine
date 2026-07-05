using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Abstractions.Entities.Transitions;

/// <summary>
/// 过渡动画类型
/// </summary>
public enum TransitionType
{
    /// <summary>淡入</summary>
    FadeIn,
    /// <summary>淡出</summary>
    FadeOut,
    /// <summary>淡入淡出交叉</summary>
    CrossFade,
    /// <summary>从左滑入</summary>
    SlideLeftIn,
    /// <summary>从左滑出</summary>
    SlideLeftOut,
    /// <summary>从右滑入</summary>
    SlideRightIn,
    /// <summary>从右滑出</summary>
    SlideRightOut,
    /// <summary>从上滑入</summary>
    SlideUpIn,
    /// <summary>从上滑出</summary>
    SlideUpOut,
    /// <summary>从下滑入</summary>
    SlideDownIn,
    /// <summary>从下滑出</summary>
    SlideDownOut,
    /// <summary>缩放进入</summary>
    ZoomIn,
    /// <summary>缩放退出</summary>
    ZoomOut,
    /// <summary>闪烁消失</summary>
    BlinkOut
}

/// <summary>
/// 过渡动画实体
/// <para>描述场景切换或元素变化时的视觉过渡效果。</para>
/// </summary>
public class TransitionEntity : BaseEntity
{
    /// <summary>
    /// 过渡类型
    /// </summary>
    public TransitionType Type { get; set; } = TransitionType.CrossFade;

    /// <summary>
    /// 持续时间（秒），默认 0.5
    /// </summary>
    public double Duration { get; set; } = 0.5;

    /// <summary>
    /// 缓动函数
    /// </summary>
    public EasingType Easing { get; set; } = EasingType.Linear;

    /// <summary>
    /// 延迟（秒），默认 0
    /// </summary>
    public double Delay { get; set; }

    /// <summary>
    /// 过渡完成后触发的事件/路由
    /// </summary>
    public string? OnCompleteTarget { get; set; }

    /// <summary>
    /// 过渡描述（日志/调试用）
    /// </summary>
    public string? Description { get; set; }
}
