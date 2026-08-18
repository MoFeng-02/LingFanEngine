namespace LingFanEngine.Dsl.LanguageService;

/// <summary>
/// 关键字 / 内置函数 / 字面量的 IDE 提示文档——补全 <c>detail</c> 与悬停详情的单一真相源。
/// <para>覆盖 <see cref="DslKeywords"/> 全量词汇（语句 / 参数 / 元素属性 / 字面量 / UI 元素类型）
/// 以及表达式引擎内置函数（random / min / max / abs / clamp / true / false）。</para>
/// <para>AOT 友好：纯静态字典 + 字符串字面量初始化，无任何反射或动态代码生成。</para>
/// </summary>
public sealed class KeywordDoc
{
    /// <summary>一句话功能说明。</summary>
    public string Summary { get; }

    /// <summary>可选的使用示例（多行）。</summary>
    public string? Usage { get; }

    public KeywordDoc(string summary, string? usage = null)
    {
        Summary = summary;
        Usage = usage;
    }
}

/// <summary>关键字文档表（补全与悬停共用）。</summary>
public static class DslKeywordDocs
{
    private static readonly Dictionary<string, KeywordDoc> s_docs = new(StringComparer.Ordinal)
    {
        // ===================== 对话与导航 =====================
        ["say"] = new("显示一行对话文本（NVL / ADV 通用）。",
            "say \"你好，世界\"\nsay by \"小明\" \"台词\"\nsay clickable=true \"可点击\""),
        ["navigate"] = new("跳转到另一个场景/故事文件。位置参为场景路径，可选追加 scene \"名\" 指定进入的具体场景。",
            "navigate \"sandbox\"\nnavigate \"chapter1\" scene \"intro\""),
        ["scene"] = new("定义一个场景块；type= 指定类型(game/menu/ui)。",
            "scene \"main\" type=game"),
        ["jump"] = new("跳转到本文件内的标签（label）。",
            "jump start"),
        ["call"] = new("调用子过程（func 或 label），用 return 返回。",
            "call heal"),
        ["return"] = new("从 call 调用返回。"),
        ["back"] = new("返回历史栈中的上一个场景 / 界面。"),
        ["forward"] = new("前进到历史栈中的下一个场景 / 界面。"),
        ["call_screen"] = new("调用（打开）一个界面场景。",
            "call_screen \"settings\""),

        // ===================== 变量操作 =====================
        ["set"] = new("赋值：对已存在变量赋值，或新建全局变量。",
            "set hp = {player.hp - 10}"),
        ["define"] = new("声明并定义变量（全局、幂等，重复定义不报错）。",
            "define hp = 100"),
        ["let"] = new("声明局部变量（当前块内有效）。",
            "let i = 0"),
        ["local"] = new("声明局部变量（等同于 let）。",
            "local i = 0"),
        ["undef"] = new("注销 / 删除一个变量。",
            "undef temp"),

        // ===================== 流程控制 =====================
        ["if"] = new("条件分支；条件为表达式（{...} 包裹）。",
            "if {hp > 0}"),
        ["else"] = new("if 的否则分支。"),
        ["while"] = new("当条件成立时循环。",
            "while {n > 0}"),
        ["for"] = new("遍历集合（for x in 列表）。",
            "for i in items"),
        ["break"] = new("跳出当前循环。"),
        ["continue"] = new("跳过本次循环剩余部分。"),
        ["end"] = new("结束 if / while / for / switch 块。"),
        ["switch"] = new("多分支选择（表达式为判断值）。",
            "switch {choice}"),
        ["case"] = new("switch 的一个分支值。",
            "case 1"),
        ["default"] = new("switch 的默认分支。"),

        // ===================== 函数 / 数据结构 =====================
        ["func"] = new("定义一个可调用函数（子过程）。",
            "func heal"),
        ["array"] = new("声明数组变量。",
            "array items"),
        ["array_push"] = new("向数组末尾追加元素。",
            "array_push items {v}"),
        ["array_pop"] = new("弹出并返回数组末尾元素。"),
        ["foreach"] = new("遍历数组元素。",
            "foreach x in items"),
        ["dict"] = new("声明字典（键值映射）变量。",
            "dict m"),
        ["dict_set"] = new("设置字典的键值。",
            "dict_set m key {v}"),

        // ===================== 交互 =====================
        ["menu"] = new("显示选项菜单（每个选项指向一个标签）。",
            "menu \"选项A\" -> label_a"),
        ["input"] = new("等待玩家输入（文本或选项）。",
            "input store=\"name\""),

        // ===================== 媒体 =====================
        ["bgm"] = new("播放背景音乐（位置参为资源路径）。",
            "bgm \"Audio/bgm01.mp3\" volume=0.8"),
        ["se"] = new("播放音效（位置参为资源路径）。",
            "se \"Audio/click.wav\""),
        ["ambient"] = new("播放环境音（可循环）。",
            "ambient \"Audio/rain.mp3\" loop=true"),
        ["stop_ambient"] = new("停止环境音。"),
        ["voice"] = new("播放语音（位置参为资源路径）。",
            "voice \"Audio/voice01.mp3\""),
        ["stop_voice"] = new("停止语音。"),
        ["video"] = new("播放视频（位置参为资源路径）。",
            "video \"Video/op.mp4\" loop=false"),
        ["stop_video"] = new("停止视频。"),
        ["pause_video"] = new("暂停视频。"),
        ["resume_video"] = new("继续播放视频。"),
        ["seek_video"] = new("跳转到视频指定位置。"),
        ["cutscene"] = new("播放过场视频（可设置可跳过）。",
            "cutscene \"Video/cs.mp4\" skipable=true"),

        // ===================== 显示与动画 =====================
        ["transition"] = new("画面过渡效果（fade / crossfade 等）。",
            "transition fade duration=1.0"),
        ["show"] = new("显示元素 / 立绘；with 指定过渡。",
            "show with fade duration=0.5"),
        ["hide"] = new("隐藏元素 / 立绘。",
            "hide with fade"),
        ["animate"] = new("对元素做动画（duration / easing）。",
            "animate duration=0.5 easing=easeOut"),
        ["animate_block"] = new("批量设置动画属性（x/y/opacity/rotate/scale）。",
            "animate_block x=100 y=50 opacity=0.5 duration=0.3"),
        ["style"] = new("引用一个已定义的样式。",
            "style \"dialog\""),
        ["shake"] = new("画面抖动；intensity= 控制强度。"),
        ["background"] = new("设置 / 切换背景。"),
        ["bg_switch"] = new("切换背景（可带过渡）。",
            "bg_switch \"Images/bg2.png\" transition=fade duration=1.0"),
        ["text_typewriter"] = new("设置打字机效果速度（speed=）。"),

        // ===================== 立绘系统 =====================
        ["sprite"] = new("显示立绘图片（src= 资源路径）。",
            "sprite src=\"Images/char.png\" x=200 y=100"),
        ["sprite_state"] = new("切换立绘表情 / 状态（emotion=）。"),
        ["sprite_move"] = new("移动立绘到坐标（x/y）。",
            "sprite_move x=200 y=100 duration=0.5"),
        ["sprite_hide"] = new("隐藏立绘。"),

        // ===================== 角色 / 样式 / 场景管理 =====================
        ["character"] = new("定义一个角色（name / color / side 等）。",
            "character \"小明\" name=\"小明\" color=\"#ff5555\""),
        ["window"] = new("显示对话框窗口。"),
        ["nvl"] = new("NVL 叙事层控制（clear / exit / auto 子命令）。"),

        // ===================== 存档 =====================
        ["save"] = new("保存游戏到指定槽位。",
            "save 1 title=\"存档1\" screenshot=true"),
        ["load"] = new("读取指定存档槽位。",
            "load 1"),
        ["auto_save"] = new("自动保存开关（true / false）。",
            "auto_save true"),
        ["save_delete"] = new("删除指定存档槽位。",
            "save_delete 1"),

        // ===================== 章节 / 成就 =====================
        ["chapter"] = new("设置当前章节（name / unlock）。",
            "chapter name=\"第一章\" unlock=true"),
        ["achievement"] = new("解锁成就（name=）。",
            "achievement name=\"通关\""),
        ["auto_speed"] = new("设置自动播放速度。"),
        ["no_skip"] = new("禁止跳过当前段落。"),
        ["force_skip"] = new("强制允许跳过当前段落。"),

        // ===================== 视频增强 =====================
        ["video_skipable"] = new("视频是否可跳过（true / false）。"),
        ["video_auto_nav"] = new("视频结束后自动跳转场景。"),

        // ===================== 回溯控制 =====================
        ["block_rollback"] = new("禁止在指定段落内回溯（防剧透）。"),
        ["fix_rollback"] = new("固定一个回溯检查点。"),

        // ===================== UI 增强 =====================
        ["popup"] = new("弹出窗口（width / height / mask）。",
            "popup width=400 height=300 mask=true"),
        ["zindex"] = new("设置元素的层级（z 序）。"),

        // ===================== 时间事件与通知 =====================
        ["time_event"] = new("定义一个时间事件（day / hour / minute / target）。"),
        ["time_pause"] = new("暂停时间流逝。"),
        ["time_resume"] = new("恢复时间流逝。"),
        ["skip_time"] = new("快进（跳过）一段时间。"),
        ["set_time_event"] = new("设置 / 修改时间事件参数。"),
        ["unregister_time_event"] = new("注销时间事件（permanent / temporary）。"),
        ["restore_time_event"] = new("恢复已注销的时间事件。"),
        ["notify"] = new("显示一条通知（type / duration）。",
            "notify type=\"info\" duration=3.0"),

        // ===================== 图鉴 / 调试 =====================
        ["gallery"] = new("打开图鉴界面。"),
        ["gallery_unlock"] = new("解锁图鉴条目（title / scene）。"),
        ["debug"] = new("输出调试信息（level= 控制级别）。"),

        // ===================== 播放控制 / 杂项 =====================
        ["skip"] = new("跳过当前内容。"),
        ["auto"] = new("切换自动播放。"),
        ["wait"] = new("等待一段时间（skipable 可跳过）。",
            "wait 1.5 skipable=true"),
        ["pause"] = new("暂停等待（hard 为硬暂停）。",
            "pause hard=true"),
        ["label"] = new("定义一个标签（跳转目标）。",
            "label start:"),

        // ===================== Live2D =====================
        ["live2d_char"] = new("加载并显示一个 Live2D 角色模型（src=）。"),
        ["live2d_show"] = new("显示 Live2D 角色。"),
        ["live2d_motion"] = new("播放 Live2D 动作（name=）。"),
        ["live2d_expr"] = new("切换 Live2D 表情（name=）。"),
        ["live2d_param"] = new("设置 Live2D 模型参数（param / value）。"),
        ["live2d_hide"] = new("隐藏 Live2D 角色。"),
        ["live2d_pause"] = new("暂停 Live2D 动画。"),
        ["live2d_resume"] = new("恢复 Live2D 动画。"),

        // ===================== 参数 / 修饰符关键字 =====================
        ["speaker"] = new("say 的说话者（角色键或名字）。"),
        ["by"] = new("say 的说话者修饰符（by \"角色\"）。"),
        ["clickable"] = new("使对话 / 元素可点击（布尔开关）。"),
        ["okey"] = new("对话确认键（布尔开关）。"),
        ["noskip"] = new("当前对话不可跳过（布尔开关，仅 true）。"),
        ["instant"] = new("对话立即全文显示、不做打字机（布尔开关，仅 true）。"),
        ["typewriter"] = new("对话使用打字机效果（布尔开关，仅 true）。"),
        ["template"] = new("对话 / 角色使用的对话框模板名。"),
        ["screen"] = new("角色级模板绑定到的界面（screen=）。"),
        ["skipable"] = new("可跳过（wait / cutscene / ambient 等）。"),
        ["hard"] = new("硬暂停（pause 专用，忽略对话完成直接等待）。"),
        ["with"] = new("show / hide 时的过渡效果名。"),
        ["duration"] = new("持续时间（秒，浮点数）。"),
        ["at"] = new("目标位置（show 用）。"),
        ["easing"] = new("缓动函数（easeIn / easeOut / linear 等）。"),
        ["volume"] = new("音量（0.0 ~ 1.0）。"),
        ["loop"] = new("是否循环（布尔）。"),
        ["autoplay"] = new("是否自动播放（布尔）。"),
        ["auto_stop"] = new("语音播完是否自动停止（布尔）。"),
        ["store"] = new("input 的存储目标变量名。"),
        ["options"] = new("input 的选项列表。"),
        ["level"] = new("debug 的调试级别。"),
        ["side"] = new("角色立绘方位（left / right / center）。"),
        ["name"] = new("角色显示名 / 章节名 / 成就名。"),
        ["color"] = new("颜色（十六进制，如 #ff0000）。"),
        ["font"] = new("字体名。"),
        ["size"] = new("字号。"),
        ["textcolor"] = new("角色名文本颜色。"),
        ["textfont"] = new("角色名文本字体。"),
        ["intensity"] = new("shake 抖动强度。"),
        ["once"] = new("define / let 仅定义一次（幂等修饰符）。"),
        ["unlock"] = new("章节 / 图鉴解锁开关（布尔）。"),
        ["title"] = new("存档 / 图鉴条目标题。"),
        ["in"] = new("for / foreach 的遍历关键字。"),
        ["clear"] = new("nvl 清空叙事层。"),
        ["exit"] = new("nvl 退出叙事层。"),
        ["type"] = new("scene 类型（game / menu / ui）。"),
        ["layout"] = new("scene 布局标识。"),
        ["day"] = new("时间事件的「日」字段。"),
        ["hour"] = new("时间事件的「时」字段。"),
        ["minute"] = new("时间事件的「分」字段。"),
        ["target"] = new("时间事件触发的目标场景。"),
        ["desc"] = new("时间事件描述。"),
        ["weekdays"] = new("时间事件生效的星期集合。"),
        ["weekday"] = new("时间事件生效的单个星期。"),
        ["condition"] = new("时间事件触发条件（表达式）。"),
        ["permanent"] = new("unregister_time_event 的永久注销模式。"),
        ["temporary"] = new("unregister_time_event 的临时注销模式。"),
        ["mask"] = new("popup 遮罩开关（布尔）。"),
        ["src"] = new("资源来源路径（sprite / live2d 等）。"),
        ["fade"] = new("淡入淡出（sprite / live2d 等）。"),
        ["emotion"] = new("立绘 / Live2D 表情标识。"),
        ["weight"] = new("Live2D 参数权重。"),
        ["param"] = new("Live2D 参数名（live2d_param）。"),
        ["value"] = new("Live2D 参数值。"),
        ["screenshot"] = new("存档时是否截取画面（布尔）。"),

        // ===================== UI 元素属性键 =====================
        ["class"] = new("元素引用的样式类（style 名）。"),
        ["style"] = new("元素的内联样式名。"),
        ["source"] = new("元素资源来源（图片 / 视频路径）。"),
        ["text"] = new("元素显示的文本内容。"),
        ["align"] = new("对齐方式（left/center/right/stretch）。"),
        ["halign"] = new("水平对齐（left/center/right/stretch）。"),
        ["valign"] = new("垂直对齐（top/bottom/center/stretch）。"),
        ["width"] = new("元素宽度。"),
        ["height"] = new("元素高度。"),
        ["fontSize"] = new("字体大小。"),
        ["order"] = new("元素排序（z 序 / 布局顺序）。"),
        ["cmd"] = new("按钮点击执行的命令名（C# 注册）。"),
        ["nav"] = new("按钮点击跳转的目标场景。"),
        ["value"] = new("元素值（input / slider 等）。"),

        // ===================== 字面量 / 枚举值 =====================
        ["game"] = new("场景类型：游戏主场景。"),
        ["menu"] = new("场景类型：菜单场景。"),
        ["ui"] = new("场景类型：纯界面场景。"),
        ["true"] = new("布尔真值。"),
        ["false"] = new("布尔假值。"),

        // ===================== UI 元素类型（scene 块内） =====================
        ["text"] = new("文本元素。"),
        ["button"] = new("按钮元素（cmd= 绑定命令 / nav= 绑定跳转）。"),
        ["image"] = new("图片元素（位置参为图片资源路径）。"),
        ["portrait"] = new("立绘 / 头像元素。"),
        ["panel"] = new("面板容器（可内含子元素）。"),
        ["vbox"] = new("垂直布局容器。"),
        ["hbox"] = new("水平布局容器。"),
        ["grid"] = new("网格布局容器。"),
        ["dialog"] = new("对话框元素。"),
        ["narrator"] = new("旁白文本元素。"),
        ["speaker"] = new("说话者名元素。"),
        ["choice"] = new("选项元素（menu 内使用）。"),
        ["video"] = new("视频播放元素。"),
        ["container"] = new("通用容器。"),
        ["scrollview"] = new("可滚动视图容器。"),
        ["sprite"] = new("立绘显示元素。"),
        ["live2d"] = new("Live2D 模型显示元素。"),
        ["progressbar"] = new("进度条元素。"),
        ["slider"] = new("滑块元素。"),
        ["checkbox"] = new("复选框元素。"),
        ["input"] = new("文本输入元素。"),
        ["label"] = new("标签文本元素。"),
        ["spacer"] = new("占位 / 间距元素。"),
        ["divider"] = new("分隔线元素。"),
    };

    /// <summary>表达式引擎内置函数（出现在 {…} 表达式上下文）。</summary>
    public static readonly IReadOnlyList<(string Name, string Signature)> BuiltinFunctions =
    [
        ("random", "random(min, max) → [min, max] 间随机整数（含两端）"),
        ("min", "min(a, b) → 返回 a、b 中的较小值"),
        ("max", "max(a, b) → 返回 a、b 中的较大值"),
        ("abs", "abs(x) → 返回 x 的绝对值"),
        ("clamp", "clamp(v, min, max) → 将 v 限制在 [min, max] 区间内"),
    ];

    /// <summary>内置函数名集合（用于语义高亮 / 补全判定，O(1) 查找）。</summary>
    private static readonly HashSet<string> s_builtinFunctionNames =
        new(BuiltinFunctions.Select(b => b.Name), StringComparer.Ordinal);

    /// <summary>判断给定标识符是否为表达式引擎内置函数名（random/min/max/abs/clamp）。</summary>
    public static bool IsBuiltinFunction(string name) => s_builtinFunctionNames.Contains(name);

    static DslKeywordDocs()
    {
        // 把内置函数也并入文档表，使悬停能识别 {random(...)} 中的函数名。
        foreach (var (name, sig) in BuiltinFunctions)
            if (!s_docs.ContainsKey(name))
                s_docs[name] = new KeywordDoc(sig);
    }

    /// <summary>尝试获取某关键字 / 内置函数 / 字面量的文档；找不到返回 false。</summary>
    public static bool TryGet(string text, out KeywordDoc doc) => s_docs.TryGetValue(text, out doc!);
}
