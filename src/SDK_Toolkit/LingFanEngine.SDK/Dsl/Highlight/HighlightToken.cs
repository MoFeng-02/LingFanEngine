namespace LingFanEngine.SDK.Dsl.Highlight;

/// <summary>高亮分类</summary>
public enum HighlightCategory
{
    // 基础分类
    Keyword,
    String,
    Comment,
    Variable,
    Label,
    Number,
    Symbol,
    Plain,

    // P0-1 精细分类
    /// <summary>样式名（style "name" 中的 name，class="name" 引用）</summary>
    StyleName,

    /// <summary>角色名（character "key" 中的 key，speaker="key" 引用）</summary>
    CharacterName,

    /// <summary>属性名（key=value 中的 key）</summary>
    PropertyName,

    /// <summary>属性值（已知枚举值如 fade, slideleft, EaseOutQuad）</summary>
    PropertyValue,

    /// <summary>颜色值（#RRGGBB / #AARRGGBB）</summary>
    ColorValue,

    /// <summary>文件路径值（含扩展名的字符串）</summary>
    PathValue,

    /// <summary>场景名（scene "name" 中的 name，navigate 引用）</summary>
    SceneName,

    /// <summary>内联标记（{b}{/b}{w}{fast}{p}{color=#xxx} 等）</summary>
    InlineTag,

    /// <summary>函数名（random(), func name() 中的 name）</summary>
    Function,

    /// <summary>运算符（+ - * / >= && || ! 等）</summary>
    Operator,

    /// <summary>信息级诊断下划线</summary>
    Info,

    // P2: 语句关键字的语义子分类（缓解"全蓝视觉疲劳"）
    /// <summary>控制流关键字（if/while/for/jump/call/return/menu 等）</summary>
    ControlFlow,
    /// <summary>场景/界面导航关键字（scene/navigate/call_screen/back/forward）</summary>
    Navigation,
    /// <summary>数据操作关键字（set/define/let/local/array/dict 等）</summary>
    DataOp,
    /// <summary>UI 元素类型关键字（text/button/image/vbox/hbox 等）</summary>
    Uielement,
    /// <summary>媒体关键字（bgm/se/video/cutscene 等）</summary>
    Media,
    /// <summary>UI 容器元素（panel/vbox/hbox/container/scrollview/choice）→ 与展示型 UI 区分</summary>
    UiContainer,
    /// <summary>UI 交互元素（button/input/checkbox/slider）→ 与展示型 UI 区分</summary>
    UiInteractive,
}

/// <summary>高亮 Token</summary>
public record HighlightToken(int Start, int Length, HighlightCategory Category);
