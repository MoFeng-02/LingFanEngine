namespace LingFanEngine.Dsl.LanguageService;

/// <summary>
/// 语义高亮类别——在词法 <see cref="DslTokenKind"/> 之上，按 DSL 语义派生的着色类别。
/// <para>由 <see cref="DslSemanticClassifier"/> 依据 DslCore 词汇数据（DslKeywords / DslUiElementCategories 等）派生。</para>
/// </summary>
public enum SemanticCategory
{
    /// <summary>未分类 / 兜底。</summary>
    Unknown = 0,

    /// <summary>注释。</summary>
    Comment,

    /// <summary>字符串字面量。</summary>
    String,

    /// <summary>数字字面量。</summary>
    Number,

    /// <summary>标识符（非关键字名称）。</summary>
    Identifier,

    /// <summary>符号（= : -> { } ( ) , 等）。</summary>
    Symbol,

    /// <summary>通用关键字。</summary>
    Keyword,

    /// <summary>控制流关键字（if/while/for/jump/call/return/menu/input 等）。</summary>
    ControlFlow,

    /// <summary>导航关键字（scene/navigate/call_screen/back/forward）。</summary>
    Navigation,

    /// <summary>数据操作关键字（set/define/let/local/array/dict 等）。</summary>
    DataOp,

    /// <summary>媒体关键字（bgm/se/video/cutscene 等）。</summary>
    Media,

    /// <summary>参数 / 修饰符关键字（speaker=/duration=/color= 等 key=value 键）。</summary>
    Parameter,

    /// <summary>UI 元素属性键（class=/text=/cmd= 等）。</summary>
    ElementAttribute,

    /// <summary>枚举字面量（game / ui / true / false）。</summary>
    Literal,

    /// <summary>展示型 UI 元素（text / image / portrait / sprite 等）→ 红。</summary>
    UiDisplay,

    /// <summary>容器型 UI 元素（panel / vbox / hbox / container / scrollview）→ 青绿。</summary>
    UiContainer,

    /// <summary>交互型 UI 元素（button / input / checkbox / slider）→ 橙。</summary>
    UiInteractive,

    /// <summary>符号名：定义站点（scene / label / 变量 / 角色 / 函数名）。</summary>
    SymbolDefinition,

    /// <summary>符号名：引用站点（jump / navigate / 菜单目标 / {var} 插值）。</summary>
    SymbolReference,

    /// <summary>资源文件路径引用（图片/音频/视频/字体等），取值来自项目资源索引；可经 Go To Definition 跳转至磁盘文件。</summary>
    Resource,

    /// <summary>表达式引擎内置函数名（random / min / max / abs / clamp），在 {…} 表达式内直接可用 → function 高亮。</summary>
    Function,
}
