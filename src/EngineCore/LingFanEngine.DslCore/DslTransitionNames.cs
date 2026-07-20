namespace LingFanEngine.DslCore;

/// <summary>
/// DSL 过渡效果别名集合——DSL 语法中可用的过渡效果名称。
/// <para>SDK 补全器和高亮器统一引用此集合，确保与引擎核心同步。</para>
/// <para>引擎 <c>MapTransitionType</c> 方法使用相同的别名映射到 <c>TransitionType</c> 枚举。</para>
/// </summary>
/// <remarks>
/// DSL 使用小写别名（如 <c>fade</c>、<c>slideleft</c>），而非枚举名（如 <c>FadeIn</c>、<c>SlideLeftIn</c>）。
/// 引擎 <c>MapTransitionType</c> 使用 <c>ToLowerInvariant()</c> 匹配，故别名以小写存储，
/// 但此集合使用 <see cref="StringComparer.OrdinalIgnoreCase"/> 比较器，匹配时大小写不敏感。
/// </remarks>
public static class DslTransitionNames
{
    /// <summary>
    /// 所有有效的 DSL 过渡效果别名（大小写不敏感）。
    /// <para>别名到 TransitionType 枚举的映射（由引擎 MapTransitionType 实现）：</para>
    /// <list type="table">
    /// <listheader><term>DSL 别名</term><description>TransitionType</description></listheader>
    /// <item><term>fade / crossfade</term><description>FadeIn</description></item>
    /// <item><term>fadeout</term><description>FadeOut</description></item>
    /// <item><term>dissolve</term><description>CrossFade</description></item>
    /// <item><term>slideleft / slideleftin</term><description>SlideLeftIn</description></item>
    /// <item><term>slideright / sliderightin</term><description>SlideRightIn</description></item>
    /// <item><term>slideup / slideupin</term><description>SlideUpIn</description></item>
    /// <item><term>slidedown / slidedownin</term><description>SlideDownIn</description></item>
    /// <item><term>fadeup</term><description>FadeUp</description></item>
    /// <item><term>fadedown</term><description>FadeDown</description></item>
    /// <item><term>blur</term><description>Blur</description></item>
    /// <item><term>zoomin / zoom</term><description>ZoomIn</description></item>
    /// <item><term>shrink</term><description>ZoomOut</description></item>
    /// <item><term>blink / blinkout</term><description>BlinkOut</description></item>
    /// </list>
    /// </summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // 淡入淡出
        "fade", "crossfade",      // → FadeIn
        "fadeout",                // → FadeOut
        "dissolve",               // → CrossFade

        // 滑动（仅 In 方向有别名）
        "slideleft", "slideleftin",   // → SlideLeftIn
        "slideright", "sliderightin", // → SlideRightIn
        "slideup", "slideupin",       // → SlideUpIn
        "slidedown", "slidedownin",   // → SlideDownIn

        // 淡入 + 移动（DSL 2.0）
        "fadeup",                 // → FadeUp
        "fadedown",               // → FadeDown

        // 其他
        "blur",                   // → Blur
        "zoomin", "zoom",         // → ZoomIn
        "shrink",                 // → ZoomOut
        "blink", "blinkout",      // → BlinkOut
    };
}
