namespace LingFanEngine.DslCore;

/// <summary>
/// DSL 关键字集合——引擎核心和 SDK 共享的唯一关键字源。
/// <para>SDK 的 Lexer/Highlighter 可根据类别分别引用，新增 DSL 语句时只需在对应类别中添加。</para>
/// </summary>
public static class DslKeywords
{
    // ====== UI 元素类型 ======    // scene 块内可用的元素类型
    private static readonly HashSet<string> _uiElementTypes = new()
    {
        "text", "button", "image", "background", "portrait",
        "panel", "vbox", "hbox", "dialog", "narrator", "speaker",
        "choice", "video", "container", "scrollview",
        "sprite", "live2d", "progressbar", "slider",
        "checkbox", "input", "label", "spacer", "divider",
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
        "bgm", "se", "ambient", "stop_ambient", "background", "video", "stop_video", "pause_video",
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
        "volume", "loop", "autoplay",
        // call_screen 参数
        "store",
        // input 参数
        "options",
        // debug 参数
        "level",
        // character / style 属性键
        "side", "name", "color", "font", "size", "fontFamily",
        // shake 参数
        "intensity",
        // define / let 修饰符
        "once",
        // gallery unlock 参数
        "unlock", "title",
        // for 循环结构
        "in",
        // nvl 子命令
        "clear", "exit",
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
        // bg_switch 参数
        "transition",
    };

    // ====== UI 元素属性键 ======
    // DslParser.BuildEntity 中处理的 UI 元素 key=value 属性名
    private static readonly HashSet<string> _elementAttributes = new()
    {
        "class", "style", "source", "text",
        "align", "halign", "valign",
        "width", "height", "fontSize", "order",
        "cmd", "nav", "value",
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

    /// <summary>所有关键字的合并只读集合（自动去重）</summary>
    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(
            _statements
                .Concat(_parameters)
                .Concat(_elementAttributes)
                .Concat(_literals)
                .Concat(_uiElementTypes));
}
