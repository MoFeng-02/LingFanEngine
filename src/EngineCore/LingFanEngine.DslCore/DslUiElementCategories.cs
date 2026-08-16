namespace LingFanEngine.DslCore;

/// <summary>UI 元素子分类（用于语义高亮按子类着色）。</summary>
public enum DslUiElementCategory
{
    /// <summary>非 UI 元素。</summary>
    None = 0,

    /// <summary>展示型（text / image / portrait / sprite 等）→ 高亮为红色。</summary>
    Display,

    /// <summary>容器型（panel / vbox / hbox / container / scrollview）→ 高亮为青绿。</summary>
    Container,

    /// <summary>交互型（button / input / checkbox / slider）→ 高亮为橙色。</summary>
    Interactive,
}

/// <summary>
/// UI 元素类型子分类数据——<see cref="DslKeywords.UiElementTypes"/> 的细化。
/// <para>SDK 高亮器不再硬编码 s_uiContainer / s_uiInteractive，统一经此处派生。</para>
/// </summary>
public static class DslUiElementCategories
{
    private static readonly HashSet<string> _container = new()
    {
        "panel", "vbox", "hbox", "container", "scrollview",
    };

    private static readonly HashSet<string> _interactive = new()
    {
        "button", "input", "checkbox", "slider",
    };

    /// <summary>容器型 UI 元素类型集合。</summary>
    public static IReadOnlySet<string> Container => _container;

    /// <summary>交互型 UI 元素类型集合。</summary>
    public static IReadOnlySet<string> Interactive => _interactive;

    /// <summary>
    /// 将 UI 元素类型归类为 <see cref="DslUiElementCategory"/>。
    /// 命中容器型→Container；命中交互型→Interactive；其余属于 UiElementTypes 的→Display；否则 None。
    /// </summary>
    public static DslUiElementCategory Classify(string elementType) =>
        _container.Contains(elementType) ? DslUiElementCategory.Container
        : _interactive.Contains(elementType) ? DslUiElementCategory.Interactive
        : DslKeywords.UiElementTypes.Contains(elementType) ? DslUiElementCategory.Display
        : DslUiElementCategory.None;
}
