namespace LingFanEngine.DslCore;

/// <summary>
/// DSL 关键字集合——引擎核心和 SDK 共享的唯一关键字源。
/// <para>SDK 的 Lexer/Highlighter 可根据类别分别引用，新增 DSL 语句时只需在对应类别中添加。</para>
/// </summary>
public static class DslKeywords
{
    // ====== UI 元素类型 ======
    // scene 块内可用的元素类型。权威源 = StoryLoader.s_uiElementTypes（scene 块元素行判定白名单）
    // ∪ ControlFactory.Create 的 case 集（真实渲染）。二者皆无的类型不进补全（写了也不渲染）；
    // 纯语句关键字（sprite/live2d/input/label）不在此表——语句身份在 _statements / _display。
    private static readonly HashSet<string> _uiElementTypes = new()
    {
        // 文本类（ControlFactory text 组）
        "text", "dialog", "narrator", "speaker",
        // 交互类
        "button", "choice", "imagebutton",
        // 图像类
        "image", "background", "portrait", "video",
        // 容器类（ControlFactory panel 组 + LayoutHelper.IsContainerType）
        "panel", "frame", "window", "dialogbox", "choicebox", "infobox",
        "overlay", "popup", "vbox", "hbox", "grid",
        "stack", "stackpanel", "canvas", "border",
        // 滚动（scroll/scrollviewer → ScrollViewer；viewport → scroll_h/scroll_v）
        "scroll", "scrollviewer", "viewport",
        // 进度/滑块/选择（bar/vbar → ProgressBar；progressbar 白名单同义保留）
        "bar", "vbar", "progressbar", "slider", "checkbox",
        // 间隔
        "separator", "spacer",
    };

    // ====== 语句关键字 ======
    // 可以作为 DSL 行首单词的关键字（DslStatementParser 中每个解析器匹配的第一个 String）
    private static readonly HashSet<string> _statements = new()
    {
        // 对话与导航
        "say", "navigate", "scene", "jump", "call", "return", "back", "forward",
        // 变量操作
        "set", "define", "let", "local", "undef",
        // 流程控制
        "if", "else", "while", "for", "break", "continue", "end",
        "switch", "case", "default",
        // 函数
        "func",
        // 数据结构
        "array", "array_push", "array_pop", "foreach",
        "dict", "dict_set",
        // 交互
        "menu", "input",
        // 媒体
        "bgm", "se", "ambient", "stop_ambient", "voice", "stop_voice", "background", "video", "stop_video", "pause_video",
        "resume_video", "seek_video", "cutscene",
        // 显示与动画
        "transition", "show", "hide", "animate", "animate_block", "style", "shake",
        // 立绘系统
        "sprite", "sprite_state", "sprite_move", "sprite_hide",
        // 背景切换
        "bg_switch",
        // 文字动画
        "text_typewriter",
        // UI 增强
        "popup", "zindex",
        // 场景管理
        "window", "nvl", "save", "load",
        // 存档增强
        "auto_save", "save_delete",
        // 章节/成就
        "chapter", "achievement",
        // 播放控制
        "auto_speed", "no_skip", "force_skip",
        // 视频增强
        "video_skipable", "video_auto_nav",
        // 回溯控制
        "block_rollback", "fix_rollback",
        // 角色
        "character",
        // 界面调用
        "call_screen",
        // 图鉴
        "gallery", "gallery_unlock",
        // 调试
        "debug",
        // 时间事件
        "time_event", "time_pause", "time_resume", "skip_time", "set_time_event", "unregister_time_event", "restore_time_event",
        // 通知
        "notify",
        // Live2D
        "live2d_char", "live2d_show", "live2d_motion", "live2d_expr",
        "live2d_param", "live2d_hide", "live2d_pause", "live2d_resume",
        // 杂项
        "skip", "auto", "wait", "pause", "label",
    };

    // ====== 语句关键字的语义子分组（用于 SDK 语义高亮，均为 _statements 的子集）======
    // 目的：让 SDK 高亮器把不同语义的语句关键字着成不同颜色，缓解"全蓝视觉疲劳"。
    private static readonly HashSet<string> _controlFlow = new()
    {
        // 分支 / 循环 / 块结束
        "if", "else", "while", "for", "break", "continue", "end",
        "switch", "case", "default",
        // 跳转 / 调用 / 返回 / 目标
        "jump", "call", "return", "label", "menu", "input",
        // 播放控制
        "wait", "skip", "auto", "pause",
    };

    private static readonly HashSet<string> _navigation = new()
    {
        // 场景定义 / 跳转 / 界面调用 / 回溯
        "scene", "navigate", "call_screen", "back", "forward",
    };

    private static readonly HashSet<string> _dataOp = new()
    {
        // 变量 / 数组 / 字典
        "set", "define", "let", "local", "undef",
        "array", "array_push", "array_pop", "foreach", "dict", "dict_set",
    };

    private static readonly HashSet<string> _media = new()
    {
        // 音频 / 视频 / 过场
        "bgm", "se", "ambient", "stop_ambient", "voice", "stop_voice",
        "video", "stop_video", "pause_video", "resume_video", "seek_video", "cutscene",
    };

    // ====== 新增语义子分组（缓解"全蓝视觉疲劳"）======

    private static readonly HashSet<string> _display = new()
    {
        // 显示与动画
        "transition", "show", "hide", "animate", "animate_block",
        "background", "shake",
        // 立绘系统
        "sprite", "sprite_state", "sprite_move", "sprite_hide",
        "bg_switch", "text_typewriter",
        // Live2D
        "live2d_char", "live2d_show", "live2d_motion", "live2d_expr",
        "live2d_param", "live2d_hide", "live2d_pause", "live2d_resume",
    };

    private static readonly HashSet<string> _saveLoad = new()
    {
        // 存档系统
        "save", "load", "auto_save", "save_delete",
    };

    private static readonly HashSet<string> _chapter = new()
    {
        // 章节 / 成就 / 图鉴
        "chapter", "achievement", "gallery", "gallery_unlock",
    };

    private static readonly HashSet<string> _rollback = new()
    {
        // 回溯控制
        "block_rollback", "fix_rollback",
    };

    private static readonly HashSet<string> _playback = new()
    {
        // 播放控制增强
        "auto_speed", "no_skip", "force_skip",
        "video_skipable", "video_auto_nav",
    };

    private static readonly HashSet<string> _timeEvent = new()
    {
        // 时间事件系统
        "time_event", "time_pause", "time_resume", "skip_time",
        "set_time_event", "unregister_time_event", "restore_time_event",
    };

    private static readonly HashSet<string> _notify = new()
    {
        // 通知 / 调试
        "notify", "debug",
    };

    private static readonly HashSet<string> _uiEnhance = new()
    {
        // UI 增强
        "popup", "zindex", "window",
    };

    // ====== 参数 / 修饰符关键字 ======
    // 出现在语句关键字之后的 key=value 参数名或单词修饰符
    private static readonly HashSet<string> _parameters = new()
    {
        // say 参数
        "speaker", "by", "clickable", "okey",
        // wait / pause 修饰符
        "skipable", "hard",
        // show / hide / transition 参数
        "with", "duration", "at",
        // animate 参数
        "easing",
        // animate_block 属性键
        "x", "y", "opacity", "rotate", "scale",
        // bgm / video / cutscene 参数
        "volume", "loop", "autoplay", "auto_stop",
        // call_screen 参数
        "store",
        // input 参数
        "options",
        // debug 参数
        "level",
        // character / style 属性键
        "side", "name", "color", "font", "size",
        // shake 参数
        "intensity",
        // define / let 修饰符
        "once",
        // gallery unlock 参数
        "unlock", "title",
        // for 循环结构
        "in",
        // nvl 子命令
        "clear", "exit", "auto",
        // scene 头属性键
        "type", "layout",
        // time_event / set_time_event 参数
        "day", "hour", "minute", "target", "desc", "weekdays", "weekday", "condition",
        // unregister_time_event 注销模式（Phase 63）
        "permanent", "temporary",
        // notify 参数
        "duration",
        // popup 参数
        "mask",
        // sprite / live2d 参数
        "src", "fade", "emotion", "weight",
        // live2d_param 参数
        "param",
        // save 增强
        "screenshot",
        // say 增强
        "instant",
        "typewriter",
        // Phase 65: 对话框模板
        "template",
        // Phase 65: 角色级模板绑定（character screen="xxx"）
        "screen",
        // bg_switch 参数
        "transition",
    };

    // ====== UI 元素属性键 ======
    // DslParser.BuildEntity 中处理的 UI 元素 key=value 属性名
    private static readonly HashSet<string> _elementAttributes = new()
    {
        // 通用属性
        "class", "style", "source", "src", "path", "text",
        "align", "halign", "valign", "xalign", "yalign",
        "width", "height", "fontSize", "font", "order",
        "cmd", "nav", "value", "color", "fontColor", "textColor",
        // 交互属性（InteractionBinder：disabled > nav > cmd > hover_* > selected_*）
        "disabled",
        "hover_source", "hover_color", "hover_opacity",
        "selected_source", "selected_color",
        // Grid 附着属性
        "col", "row", "colspan", "rowspan",
        // 通用布局属性
        "x", "y", "xoffset", "yoffset", "xanchor", "yanchor",
        "margin", "padding", "right", "bottom",
        "minWidth", "minHeight", "maxWidth", "maxHeight",
        // 外观属性
        "opacity", "visible", "enabled", "zindex", "clipToBounds", "cursor",
        // 变换属性
        "rotation", "scale", "scaleX", "scaleY",
        // 边框属性
        "cornerRadius", "borderBrush", "borderColor", "borderThickness",
        // 容器属性
        "spacing", "direction", "columns", "rows",
        // 文本属性
        "textAlign", "maxWidth",
        // 图片属性
        "stretch",
    };

    // ====== 枚举值 / 字面量 ======
    // 作为属性值出现的关键字（如 type=game 中的 game）
    private static readonly HashSet<string> _literals = new()
    {
        "game", "ui", "true", "false", "menu",
    };

    /// <summary>语句关键字（可作为 DSL 行首单词）</summary>
    public static IReadOnlySet<string> Statements => _statements;

    /// <summary>参数 / 修饰符关键字（出现在语句之后的 key=value 或单词修饰符）</summary>
    public static IReadOnlySet<string> Parameters => _parameters;

    /// <summary>UI 元素属性键（image/text/button 等元素行的 key=value 属性名）</summary>
    public static IReadOnlySet<string> ElementAttributes => _elementAttributes;

    /// <summary>枚举值 / 字面量关键字（如 type=game 中的 game）</summary>
    public static IReadOnlySet<string> Literals => _literals;

    /// <summary>UI 元素类型（scene 块内的元素名）</summary>
    public static IReadOnlySet<string> UiElementTypes => _uiElementTypes;

    /// <summary>控制流关键字（if/while/for/jump/call/return/menu 等）→ 用于 SDK 语义高亮（紫）</summary>
    public static IReadOnlySet<string> ControlFlow => _controlFlow;

    /// <summary>场景/界面导航关键字（scene/navigate/call_screen/back/forward）→ 用于 SDK 语义高亮（青绿）</summary>
    public static IReadOnlySet<string> Navigation => _navigation;

    /// <summary>数据操作关键字（set/define/let/local/array/dict 等）→ 用于 SDK 语义高亮（黄）</summary>
    public static IReadOnlySet<string> DataOp => _dataOp;

    /// <summary>媒体关键字（bgm/se/video/cutscene 等）→ 用于 SDK 语义高亮（橙绿）</summary>
    public static IReadOnlySet<string> Media => _media;

    /// <summary>显示/动画/Live2D 关键字 → 用于 SDK 语义高亮（粉红）</summary>
    public static IReadOnlySet<string> Display => _display;

    /// <summary>存档系统关键字 → 用于 SDK 语义高亮（深青）</summary>
    public static IReadOnlySet<string> SaveLoad => _saveLoad;

    /// <summary>章节/成就/图鉴关键字 → 用于 SDK 语义高亮（靛蓝）</summary>
    public static IReadOnlySet<string> Chapter => _chapter;

    /// <summary>回溯控制关键字 → 用于 SDK 语义高亮（深红）</summary>
    public static IReadOnlySet<string> Rollback => _rollback;

    /// <summary>播放控制增强关键字 → 用于 SDK 语义高亮（深绿）</summary>
    public static IReadOnlySet<string> Playback => _playback;

    /// <summary>时间事件系统关键字 → 用于 SDK 语义高亮（棕色）</summary>
    public static IReadOnlySet<string> TimeEvent => _timeEvent;

    /// <summary>通知/调试关键字 → 用于 SDK 语义高亮（灰色）</summary>
    public static IReadOnlySet<string> Notify => _notify;

    /// <summary>UI 增强关键字 → 用于 SDK 语义高亮（浅蓝）</summary>
    public static IReadOnlySet<string> UiEnhance => _uiEnhance;

    /// <summary>所有关键字的合并只读集合（自动去重）</summary>
    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(
            _statements
                .Concat(_parameters)
                .Concat(_elementAttributes)
                .Concat(_literals)
                .Concat(_uiElementTypes));
}
