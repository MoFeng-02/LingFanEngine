using System.Collections.Generic;

namespace LingFanEngine.DslCore;

/// <summary>
/// 补全时某光标槽位预期的「引用种类」——对应符号索引里已定义的符号（或枚举集合）。
/// <para>由 <see cref="DslStatementParser"/> 的真实语法（每条语句的参数签名）派生，本类是其补全视角的单一真相源，
/// 与 parser 同处 DslCore，避免 LSP 再写一套平面关键字集合去「猜」上下文。</para>
/// </summary>
public enum DslCompletionRef
{
    /// <summary>自由文本 / 路径 / 数字——无补全列表。</summary>
    None,
    /// <summary>场景名（scene "x" 定义）。</summary>
    Scene,
    /// <summary>标签名（label x: 定义）。</summary>
    Label,
    /// <summary>函数名（func x 定义）。</summary>
    Func,
    /// <summary>call 目标：函数或标签。</summary>
    LabelOrFunc,
    /// <summary>角色键（character "x" 定义；say 的 speaker=/by 引用）。</summary>
    Speaker,
    /// <summary>character 的定义键。</summary>
    Character,
    /// <summary>样式名（style "x" 定义；元素 class=/style= 引用）。</summary>
    Style,
    /// <summary>过渡效果名（DslTransitionNames）。</summary>
    Transition,
    /// <summary>缓动函数名（DslEasingNames）。</summary>
    Easing,
    /// <summary>存档槽（save/load，无索引列表）。</summary>
    Slot,
    /// <summary>场景类型枚举（game/ui/menu）。</summary>
    SceneType,
    /// <summary>布尔值（true/false）。</summary>
    Boolean,
    /// <summary>只能是 true 的开关（clickable/okey/noskip/instant/typewriter 等）。</summary>
    TrueOnly,
    /// <summary>表达式上下文（{...} 插值）——变量名。</summary>
    Expression,
    /// <summary>资源文件路径（图片/音频/视频/字体）——取自项目资源索引。</summary>
    Resource,
    /// <summary>按钮命令名（button cmd=）——取自 C# 命令注册表（M8 跨语言索引）。</summary>
    Command,
    /// <summary>对齐枚举值（align/halign/valign：left/center/right/stretch/top/bottom），与 ControlFactory 解析一致。</summary>
    Align,
}

/// <summary>单条语句 / UI 元素的补全语法模型。</summary>
public sealed class DslStmtGrammar
{
    /// <summary>语句关键字（行首第一个词）。</summary>
    public required string Keyword { get; init; }

    /// <summary>是否为 scene 块内的 UI 元素（用 ElementAttributes 作属性键补全）。</summary>
    public bool IsUiElement { get; init; }

    /// <summary>第一个位置参（关键字后的引号字符串 / 裸标识符）的引用种类。None=无列表。</summary>
    public DslCompletionRef PositionalRef { get; init; } = DslCompletionRef.None;

    /// <summary>位置词槽：以「单词」形式出现、其后跟引号字符串的引用（如 say 的 by "speaker"、show 的 with "transition"、navigate 的 scene "name"）。</summary>
    public IReadOnlyDictionary<string, DslCompletionRef> PositionalWordRefs { get; init; } = Empty;

    /// <summary>命名参数（key=value）允许的键名 → 值引用种类。None=自由值（不补列表）。</summary>
    public IReadOnlyDictionary<string, DslCompletionRef> NamedParams { get; init; } = Empty;

    private static readonly IReadOnlyDictionary<string, DslCompletionRef> Empty = new Dictionary<string, DslCompletionRef>();
}

/// <summary>
/// DSL 补全语法表——由 <see cref="DslStatementParser"/> 的真实语法派生，作为 LSP 补全上下文判定的单一真相源。
/// <para>消费方式：LSP 用 <see cref="DslTokenizer"/> 解析光标所在行，取首词查 <see cref="TryGet"/>，
/// 再据光标位于「字符串内 / key= 值 / 位置词 / 首个位置参 / 表达式」哪一类，从本表取出对应引用种类。</para>
/// <para>AOT 友好：纯静态数据表，无反射。</para>
/// </summary>
public static class DslGrammar
{
    private static readonly IReadOnlyDictionary<string, DslCompletionRef> Empty = new Dictionary<string, DslCompletionRef>();

    private static DslStmtGrammar Stmt(string keyword,
        DslCompletionRef positionalRef = DslCompletionRef.None,
        Dictionary<string, DslCompletionRef>? wordRefs = null,
        Dictionary<string, DslCompletionRef>? named = null,
        bool isUiElement = false) =>
        new DslStmtGrammar
        {
            Keyword = keyword,
            IsUiElement = isUiElement,
            PositionalRef = positionalRef,
            PositionalWordRefs = wordRefs ?? Empty,
            NamedParams = named ?? Empty,
        };

    private static Dictionary<string, DslCompletionRef> P(params (string Key, DslCompletionRef Value)[] pairs)
    {
        var d = new Dictionary<string, DslCompletionRef>(StringComparer.Ordinal);
        foreach (var (k, v) in pairs) d[k] = v;
        return d;
    }

    /// <summary>语句关键字 → 补全语法模型。</summary>
    public static IReadOnlyDictionary<string, DslStmtGrammar> Table { get; } = Build();

    private static IReadOnlyDictionary<string, DslStmtGrammar> Build()
    {
        var list = new List<DslStmtGrammar>
        {
            // ===== 对话与导航 =====
            Stmt("say", wordRefs: P(("by", DslCompletionRef.Speaker)),
                named: P(("speaker", DslCompletionRef.Speaker), ("okey", DslCompletionRef.TrueOnly), ("clickable", DslCompletionRef.TrueOnly), ("noskip", DslCompletionRef.TrueOnly), ("instant", DslCompletionRef.TrueOnly), ("typewriter", DslCompletionRef.TrueOnly), ("template", DslCompletionRef.Style), ("voice", DslCompletionRef.None))),
            Stmt("navigate", wordRefs: P(("scene", DslCompletionRef.Scene)),
                named: P(("scene", DslCompletionRef.Scene))),
            Stmt("scene", positionalRef: DslCompletionRef.Scene, named: P(("type", DslCompletionRef.SceneType), ("layout", DslCompletionRef.None))),
            Stmt("jump", positionalRef: DslCompletionRef.Label),
            Stmt("call", positionalRef: DslCompletionRef.LabelOrFunc),
            Stmt("return"), Stmt("back"), Stmt("forward"),

            // ===== 变量操作 =====
            // 位置参为表达式（变量名）：set x = … / define x = … 等，关键字后直接补变量名（含 C# 状态键、行内标记）。
            Stmt("set", positionalRef: DslCompletionRef.Expression),
            Stmt("define", positionalRef: DslCompletionRef.Expression),
            Stmt("let", positionalRef: DslCompletionRef.Expression),
            Stmt("local", positionalRef: DslCompletionRef.Expression),
            Stmt("undef", positionalRef: DslCompletionRef.Expression),

            // ===== 流程控制 =====
            Stmt("if", positionalRef: DslCompletionRef.Expression), Stmt("else"), Stmt("while", positionalRef: DslCompletionRef.Expression),
            Stmt("for"), Stmt("break"), Stmt("continue"), Stmt("end"),
            Stmt("switch", positionalRef: DslCompletionRef.Expression), Stmt("case"), Stmt("default"),

            // ===== 函数 / 数据结构 =====
            Stmt("func", positionalRef: DslCompletionRef.Func),
            Stmt("array"), Stmt("array_push"), Stmt("array_pop"), Stmt("foreach"),
            Stmt("dict"), Stmt("dict_set"),

            // ===== 交互 =====
            Stmt("menu"), Stmt("input", named: P(("store", DslCompletionRef.None), ("options", DslCompletionRef.None))),

            // ===== 媒体 =====
            Stmt("bgm", positionalRef: DslCompletionRef.Resource, named: P(("volume", DslCompletionRef.None))),
            Stmt("se", positionalRef: DslCompletionRef.Resource, named: P(("volume", DslCompletionRef.None))),
            Stmt("ambient", positionalRef: DslCompletionRef.Resource, named: P(("loop", DslCompletionRef.Boolean), ("volume", DslCompletionRef.None))),
            Stmt("stop_ambient"), Stmt("voice", positionalRef: DslCompletionRef.Resource, named: P(("volume", DslCompletionRef.None), ("auto_stop", DslCompletionRef.Boolean))),
            Stmt("stop_voice"),
            Stmt("video", positionalRef: DslCompletionRef.Resource, named: P(("volume", DslCompletionRef.None), ("loop", DslCompletionRef.Boolean), ("autoplay", DslCompletionRef.Boolean))),
            Stmt("stop_video"), Stmt("pause_video"), Stmt("resume_video"), Stmt("seek_video"),
            Stmt("cutscene", positionalRef: DslCompletionRef.Resource, named: P(("skipable", DslCompletionRef.Boolean), ("volume", DslCompletionRef.None))),
            Stmt("wait", named: P(("skipable", DslCompletionRef.TrueOnly))),
            Stmt("pause", named: P(("hard", DslCompletionRef.TrueOnly))),
            Stmt("transition", positionalRef: DslCompletionRef.Transition, named: P(("duration", DslCompletionRef.None))),
            Stmt("label", positionalRef: DslCompletionRef.Label),
            Stmt("show", wordRefs: P(("with", DslCompletionRef.Transition)), named: P(("duration", DslCompletionRef.None), ("at", DslCompletionRef.None))),
            Stmt("hide", wordRefs: P(("with", DslCompletionRef.Transition)), named: P(("duration", DslCompletionRef.None))),
            Stmt("animate", named: P(("duration", DslCompletionRef.None), ("easing", DslCompletionRef.Easing))),
            Stmt("animate_block", named: P(("x", DslCompletionRef.None), ("y", DslCompletionRef.None), ("opacity", DslCompletionRef.None), ("rotate", DslCompletionRef.None), ("scale", DslCompletionRef.None), ("duration", DslCompletionRef.None), ("easing", DslCompletionRef.Easing))),
            Stmt("style", positionalRef: DslCompletionRef.Style),
            Stmt("call_screen", positionalRef: DslCompletionRef.Scene, named: P(("store", DslCompletionRef.None), ("with", DslCompletionRef.None))),
            Stmt("background"),

            // ===== 角色 / 样式 / 场景管理 =====
            Stmt("character", positionalRef: DslCompletionRef.Character, named: P(("name", DslCompletionRef.None), ("color", DslCompletionRef.None), ("font", DslCompletionRef.None), ("size", DslCompletionRef.None), ("textcolor", DslCompletionRef.None), ("textfont", DslCompletionRef.None), ("typewriter", DslCompletionRef.None))),
            Stmt("window"),
            Stmt("nvl"),

            // ===== 存档增强 =====
            Stmt("auto_save", positionalRef: DslCompletionRef.Boolean),
            Stmt("save_delete", positionalRef: DslCompletionRef.Slot),
            Stmt("save", positionalRef: DslCompletionRef.Slot, named: P(("title", DslCompletionRef.None), ("screenshot", DslCompletionRef.Boolean))),
            Stmt("load", positionalRef: DslCompletionRef.Slot),

            // ===== 章节 / 成就 =====
            Stmt("chapter", named: P(("name", DslCompletionRef.None), ("unlock", DslCompletionRef.Boolean))),
            Stmt("achievement", named: P(("name", DslCompletionRef.None))),
            Stmt("auto_speed"), Stmt("no_skip"), Stmt("force_skip"),

            // ===== 视频增强 =====
            Stmt("video_skipable", positionalRef: DslCompletionRef.Boolean), Stmt("video_auto_nav", positionalRef: DslCompletionRef.Scene),

            // ===== 回溯控制 =====
            Stmt("block_rollback"), Stmt("fix_rollback"),

            // ===== UI 增强 =====
            Stmt("popup", named: P(("width", DslCompletionRef.None), ("height", DslCompletionRef.None), ("mask", DslCompletionRef.Boolean))),
            Stmt("zindex"),

            // ===== 立绘系统 =====
            Stmt("sprite", named: P(("src", DslCompletionRef.Resource), ("x", DslCompletionRef.None), ("y", DslCompletionRef.None), ("fade", DslCompletionRef.None))),
            Stmt("sprite_state", named: P(("emotion", DslCompletionRef.None))),
            Stmt("sprite_move", named: P(("x", DslCompletionRef.None), ("y", DslCompletionRef.None), ("duration", DslCompletionRef.None))),
            Stmt("sprite_hide", named: P(("fade", DslCompletionRef.None))),
            Stmt("bg_switch", named: P(("transition", DslCompletionRef.Transition), ("duration", DslCompletionRef.None))),
            Stmt("text_typewriter", named: P(("speed", DslCompletionRef.None))),

            // ===== Live2D =====
            Stmt("live2d_char", named: P(("src", DslCompletionRef.Resource), ("height", DslCompletionRef.None), ("x", DslCompletionRef.None), ("y", DslCompletionRef.None), ("fade", DslCompletionRef.None), ("loop", DslCompletionRef.Boolean), ("seamless", DslCompletionRef.Boolean), ("blink_rate", DslCompletionRef.None), ("mouse_track_head", DslCompletionRef.Boolean), ("voice_sync_mouth", DslCompletionRef.Boolean))),
            Stmt("live2d_show"), Stmt("live2d_motion", named: P(("name", DslCompletionRef.None), ("fade", DslCompletionRef.None), ("loop", DslCompletionRef.Boolean))),
            Stmt("live2d_expr", named: P(("name", DslCompletionRef.None), ("fade", DslCompletionRef.None))),
            Stmt("live2d_param", named: P(("param", DslCompletionRef.None), ("value", DslCompletionRef.None), ("weight", DslCompletionRef.None))),
            Stmt("live2d_hide", named: P(("fade", DslCompletionRef.None))),
            Stmt("live2d_pause"), Stmt("live2d_resume"),

            // ===== 时间事件与通知 =====
            Stmt("time_event", named: P(("day", DslCompletionRef.None), ("hour", DslCompletionRef.None), ("minute", DslCompletionRef.None), ("target", DslCompletionRef.Scene), ("desc", DslCompletionRef.None), ("once", DslCompletionRef.Boolean), ("condition", DslCompletionRef.Expression), ("weekday", DslCompletionRef.None), ("weekdays", DslCompletionRef.None))),
            Stmt("time_pause"), Stmt("time_resume"), Stmt("skip_time"),
            Stmt("set_time_event", named: P(("minute", DslCompletionRef.None), ("day", DslCompletionRef.None), ("once", DslCompletionRef.Boolean), ("condition", DslCompletionRef.Expression), ("desc", DslCompletionRef.None), ("weekdays", DslCompletionRef.None), ("weekday", DslCompletionRef.None))),
            Stmt("unregister_time_event", named: P(("permanent", DslCompletionRef.None), ("temporary", DslCompletionRef.None))),
            Stmt("restore_time_event"),
            Stmt("notify", named: P(("type", DslCompletionRef.None), ("duration", DslCompletionRef.None))),

            // ===== 图鉴 / 调试 =====
            Stmt("gallery_unlock", named: P(("title", DslCompletionRef.None), ("scene", DslCompletionRef.Scene))),
            Stmt("debug", named: P(("level", DslCompletionRef.None))),
            Stmt("skip"), Stmt("auto"),
        };

        // UI 元素类型（scene 块内）：用 ElementAttributes 作属性键；class/style → Style。
        // 通用属性（所有 UI 元素共享）
        var elemAttrs = new Dictionary<string, DslCompletionRef>(StringComparer.Ordinal);
        foreach (var a in DslKeywords.ElementAttributes)
            elemAttrs[a] = a switch
            {
                "class" or "style" => DslCompletionRef.Style,
                "source" or "src" or "path" => DslCompletionRef.Resource,
                "nav" => DslCompletionRef.Scene,
                "cmd" => DslCompletionRef.Command,
                "align" or "halign" or "valign" or "xalign" or "yalign" => DslCompletionRef.Align,
                _ => DslCompletionRef.None,
            };
        // 元素特定属性（按元素类型补充）
        var elementSpecificAttrs = new Dictionary<string, Dictionary<string, DslCompletionRef>>(StringComparer.OrdinalIgnoreCase)
        {
            // Grid 容器属性
            ["grid"] = new(StringComparer.Ordinal)
            {
                ["columns"] = DslCompletionRef.None,
                ["rows"] = DslCompletionRef.None,
            },
            // 容器属性（panel/vbox/hbox）
            ["panel"] = new(StringComparer.Ordinal) { ["direction"] = DslCompletionRef.None, ["spacing"] = DslCompletionRef.None },
            ["vbox"] = new(StringComparer.Ordinal) { ["direction"] = DslCompletionRef.None, ["spacing"] = DslCompletionRef.None },
            ["hbox"] = new(StringComparer.Ordinal) { ["direction"] = DslCompletionRef.None, ["spacing"] = DslCompletionRef.None },
            // 图片属性
            ["image"] = new(StringComparer.Ordinal) { ["stretch"] = DslCompletionRef.None },
            ["portrait"] = new(StringComparer.Ordinal) { ["stretch"] = DslCompletionRef.None },
            ["sprite"] = new(StringComparer.Ordinal) { ["stretch"] = DslCompletionRef.None },
            // 进度条/滑块属性
            ["slider"] = new(StringComparer.Ordinal) { ["min"] = DslCompletionRef.None, ["max"] = DslCompletionRef.None, ["orientation"] = DslCompletionRef.None },
            ["progressbar"] = new(StringComparer.Ordinal) { ["min"] = DslCompletionRef.None, ["max"] = DslCompletionRef.None },
            ["bar"] = new(StringComparer.Ordinal) { ["min"] = DslCompletionRef.None, ["max"] = DslCompletionRef.None },
            ["vbar"] = new(StringComparer.Ordinal) { ["min"] = DslCompletionRef.None, ["max"] = DslCompletionRef.None },
            // 复选框属性
            ["checkbox"] = new(StringComparer.Ordinal) { ["checked"] = DslCompletionRef.Boolean },
            // ScrollViewer 属性
            ["scrollviewer"] = new(StringComparer.Ordinal) { ["scroll_h"] = DslCompletionRef.Boolean, ["scroll_v"] = DslCompletionRef.Boolean },
            ["viewport"] = new(StringComparer.Ordinal) { ["scroll_h"] = DslCompletionRef.Boolean, ["scroll_v"] = DslCompletionRef.Boolean },
        };
        foreach (var t in DslKeywords.UiElementTypes)
        {
            // image 的位置参即图片资源路径（image "Images/lingfan.png"），须标记为 Resource 槽位，
            // 否则该字符串会被当作普通 String、既无高亮也无跳转（用户实测痛点）。其余 UI 元素位置参维持 None。
            var positional = t == "image" ? DslCompletionRef.Resource : DslCompletionRef.None;
            // 合并通用属性和元素特定属性
            var named = new Dictionary<string, DslCompletionRef>(elemAttrs, StringComparer.Ordinal);
            if (elementSpecificAttrs.TryGetValue(t, out var specific))
            {
                foreach (var kv in specific)
                    named[kv.Key] = kv.Value;
            }
            list.Add(Stmt(t, positionalRef: positional, isUiElement: true, named: named));
        }

        // 合并而非覆盖：同一关键字既作为语句又作为 UI 元素出现时（如 sprite：语句专属 src→Resource，
        // 又是 UI 元素可带 class/style/source/cmd 等），保留语句专属参数并叠加 UI 元素属性，
        // 避免 UI 元素版本把语句参数（src→Resource）覆盖掉——否则 sprite src= / live2d_char src= 的资源补全与诊断全部失效。
        var dict = new Dictionary<string, DslStmtGrammar>(StringComparer.Ordinal);
        foreach (var g in list)
        {
            if (dict.TryGetValue(g.Keyword, out var existing))
            {
                var merged = new Dictionary<string, DslCompletionRef>(existing.NamedParams, StringComparer.Ordinal);
                foreach (var kv in g.NamedParams)
                    if (!merged.ContainsKey(kv.Key)) merged[kv.Key] = kv.Value;
                dict[g.Keyword] = new DslStmtGrammar
                {
                    Keyword = g.Keyword,
                    IsUiElement = g.IsUiElement,
                    PositionalRef = existing.PositionalRef,
                    PositionalWordRefs = existing.PositionalWordRefs,
                    NamedParams = merged,
                };
            }
            else
            {
                dict[g.Keyword] = g;
            }
        }
        return dict;
    }

    /// <summary>查询某语句 / 元素的语法模型；找不到返回 null。</summary>
    public static DslStmtGrammar? TryGet(string keyword) => Table.TryGetValue(keyword, out var g) ? g : null;
}
