using System.Globalization;
using LingFanEngine.Abstractions.Entities.UIs;
using Pidgin;
using static Pidgin.Parser;
using static Pidgin.Parser<char>;

namespace LingFanEngine.Services.Scripting;

/// <summary>
/// 基于 Pidgin 的 DSL 语句解析器——逐行解析 DSL 脚本为 AST 语句
/// <para>替代 LingFanDslEngine 中的 20+ 个正则模式，提供更好的可维护性和错误信息。</para>
/// </summary>
public static class DslStatementParser
{
    // ====== 基础 token ======

    /// <summary>跳过空白</summary>
    private static readonly Parser<char, Unit> _ws = SkipWhitespaces;

    /// <summary>带空白的 token 包装</summary>
    private static Parser<char, T> Tok<T>(Parser<char, T> p) => p.Before(_ws);

    /// <summary>双引号字符串</summary>
    private static readonly Parser<char, string> QuotedString =
        Char('"').Then(AnyCharExcept('"').ManyString()).Before(Char('"'))
            .Labelled("quoted string");

    /// <summary>标识符 \w+（字母/数字/下划线）</summary>
    private static readonly Parser<char, string> Identifier =
        Token(c => char.IsLetterOrDigit(c) || c == '_').AtLeastOnceString()
            .Labelled("identifier");

    /// <summary>数字（整数或浮点数）</summary>
    private static readonly Parser<char, double> Number =
        (from intPart in Digit.AtLeastOnceString()
         from fracPart in Try(Char('.').Then(Digit.AtLeastOnceString())).Optional()
         select double.Parse(intPart + (fracPart.HasValue ? "." + fracPart.Value : ""), CultureInfo.InvariantCulture))
        .Labelled("number");

    /// <summary>{表达式}——返回花括号内的内容</summary>
    private static readonly Parser<char, string> Expression =
        Char('{').Then(AnyCharExcept('}').ManyString()).Before(Char('}'))
            .Select(s => s.Trim())
            .Labelled("expression");

    /// <summary>坐标 (x, y)</summary>
    private static readonly Parser<char, (double x, double y)> _position =
        from _1 in String("at").Before(_ws)
        from _2 in Char('(').Before(_ws)
        from x in Number.Before(_ws)
        from _3 in Char(',').Before(_ws)
        from y in Number.Before(_ws)
        from _4 in Char(')')
        select (x, y);

    // ====== 语句解析器 ======

    /// <summary>say "text" [by "speaker" | speaker="speaker"] [clickable=true | okey]</summary>
    private static readonly Parser<char, DslStatement> _say =
        from _1 in String("say").Before(_ws)
        from text in QuotedString.Before(_ws)
        from speaker in (
            // speaker="speaker" 语法（故事文件中使用的格式）
            Try(String("speaker=").Then(QuotedString).Before(_ws))
            // by "speaker" 语法（兼容旧格式）
            .Or(Try(String("by").Before(_ws).Then(QuotedString)).Before(_ws))
        ).Optional()
        from clickable in (
            // clickable=true 语法
            Try(String("clickable=true").Before(_ws))
            // okey 语法糖（等价于 clickable=true）
            .Or(Try(String("okey").Before(_ws)))
        ).Optional()
        select (DslStatement)new SayStmt
        {
            Text = text,
            Speaker = speaker.HasValue ? speaker.Value : null,
            Clickable = clickable.HasValue
        };

    /// <summary>navigate "path" [scene "name"]</summary>
    private static readonly Parser<char, DslStatement> _navigate =
        from _1 in String("navigate").Before(_ws)
        from path in QuotedString.Before(_ws)
        from scene in Try(String("scene").Before(_ws).Then(QuotedString)).Optional()
        select (DslStatement)new NavigateStmt
        {
            Path = path,
            SceneName = scene.HasValue ? scene.Value : null
        };

    /// <summary>character "key" name="xxx" color="#xxx" font="xxx" ...</summary>
    private static readonly Parser<char, string> PropKey =
        Token(c => char.IsLetter(c) || c == '_').AtLeastOnceString();

    /// <summary>属性值：引号字符串或裸值（到下一个空格）</summary>
    private static readonly Parser<char, string> PropValue =
        QuotedString
        .Or(AnyCharExcept(' ', '\t', '\n', '\r').AtLeastOnceString());

    /// <summary>单个 key=value 属性对</summary>
    private static readonly Parser<char, (string key, string value)> _propPair =
        from key in PropKey.Before(_ws)
        from _eq in Char('=').Before(_ws)
        from value in PropValue.Before(_ws)
        select (key, value);

    /// <summary>character "key" name="xxx" color="#xxx" ...</summary>
    private static readonly Parser<char, DslStatement> _character =
        from _1 in String("character").Before(_ws)
        from key in QuotedString.Before(_ws)
        from props in _propPair.Many()
        select (DslStatement)new CharacterStmt
        {
            Key = key,
            Properties = props.ToDictionary(p => p.key, p => p.value)
        };

    /// <summary>set "key" value</summary>
    private static readonly Parser<char, DslStatement> _set =
        from _1 in String("set").Before(_ws)
        from key in QuotedString.Before(_ws)
        from value in AnyCharExcept('\n', '\r').ManyString()
        select (DslStatement)new SetStmt
        {
            Key = key,
            ValuePart = value.Trim()
        };

    /// <summary>define "key" value once</summary>
    private static readonly Parser<char, DslStatement> _define =
        from _1 in String("define").Before(_ws)
        from key in QuotedString.Before(_ws)
        from rest in AnyCharExcept('\n', '\r').ManyString()
        select MakeDefineOrLet(key, rest, true);

    /// <summary>let "key" value once</summary>
    private static readonly Parser<char, DslStatement> _let =
        from _1 in String("let").Before(_ws)
        from key in QuotedString.Before(_ws)
        from rest in AnyCharExcept('\n', '\r').ManyString()
        select MakeDefineOrLet(key, rest, false);

    /// <summary>bgm "path" [volume=N]</summary>
    private static readonly Parser<char, DslStatement> _bgm =
        from _1 in String("bgm").Before(_ws)
        from path in QuotedString.Before(_ws)
        from volume in Try(String("volume=").Then(Number).Before(_ws)).Optional()
        select (DslStatement)new BgmStmt
        {
            Path = path,
            Volume = volume.HasValue ? (float)volume.Value : null
        };

    /// <summary>video "path" [volume=N] [loop=true|false] [autoplay=true|false]</summary>
    /// <para>参数顺序固定：volume → loop → autoplay。未指定的参数使用默认值。</para>
    private static readonly Parser<char, DslStatement> _video =
        from _1 in String("video").Before(_ws)
        from path in QuotedString.Before(_ws)
        from volume in Try(String("volume=").Then(Number).Before(_ws)).Optional()
        from loop in Try(String("loop=").Then(String("true").Or(String("false"))).Before(_ws)).Optional()
        from autoplay in Try(String("autoplay=").Then(String("true").Or(String("false"))).Before(_ws)).Optional()
        select (DslStatement)new VideoStmt
        {
            Path = path,
            Volume = volume.HasValue ? (float)volume.Value : null,
            Loop = loop.HasValue && loop.Value == "true",
            AutoPlay = !autoplay.HasValue || autoplay.Value == "true"
        };

    /// <summary>stop_video</summary>
    private static readonly Parser<char, DslStatement> _stopVideo =
        from _1 in String("stop_video").Before(_ws.Or(End))
        select (DslStatement)new StopVideoStmt();

    /// <summary>pause_video</summary>
    private static readonly Parser<char, DslStatement> _pauseVideo =
        from _1 in String("pause_video").Before(_ws.Or(End))
        select (DslStatement)new PauseVideoStmt();

    /// <summary>resume_video</summary>
    private static readonly Parser<char, DslStatement> _resumeVideo =
        from _1 in String("resume_video").Before(_ws.Or(End))
        select (DslStatement)new ResumeVideoStmt();

    /// <summary>seek_video N</summary>
    private static readonly Parser<char, DslStatement> _seekVideo =
        from _1 in String("seek_video").Before(_ws)
        from position in Number
        select (DslStatement)new SeekVideoStmt { Position = position };

    /// <summary>cutscene "path" [skipable=true|false] [volume=N]</summary>
    /// <para>参数顺序固定：skipable → volume。未指定的参数使用默认值。</para>
    private static readonly Parser<char, DslStatement> _cutscene =
        from _1 in String("cutscene").Before(_ws)
        from path in QuotedString.Before(_ws)
        from skipable in Try(String("skipable=").Then(String("true").Or(String("false"))).Before(_ws)).Optional()
        from volume in Try(String("volume=").Then(Number).Before(_ws)).Optional()
        select (DslStatement)new CutsceneStmt
        {
            Path = path,
            Skipable = !skipable.HasValue || skipable.Value == "true",
            Volume = volume.HasValue ? (float)volume.Value : null
        };

    /// <summary>wait N [skipable]</summary>
    private static readonly Parser<char, DslStatement> _wait =
        from _1 in String("wait").Before(_ws)
        from seconds in Number.Before(_ws)
        from skipable in Try(String("skipable").Before(_ws)).Optional()
        select (DslStatement)new WaitStmt { Seconds = seconds, IsSkipable = skipable.HasValue };

    /// <summary>pause [N] [hard]</summary>
    private static readonly Parser<char, DslStatement> _pause =
        from _1 in String("pause").Before(_ws)
        from seconds in Try(Number.Before(_ws)).Optional()
        from hard in Try(String("hard").Before(_ws)).Optional()
        select (DslStatement)new PauseStmt
        {
            Seconds = seconds.HasValue ? seconds.Value : null,
            IsHard = hard.HasValue
        };

    /// <summary>transition "type" [duration=N] [easing=xxx]</summary>
    private static readonly Parser<char, DslStatement> _transition =
        from _1 in String("transition").Before(_ws)
        from type in QuotedString.Before(_ws)
        from duration in Try(String("duration=").Then(Number).Before(_ws)).Optional()
        select (DslStatement)new TransitionStmt
        {
            Type = type,
            Duration = duration.HasValue ? duration.Value : null
        };

    /// <summary>label name:</summary>
    private static readonly Parser<char, DslStatement> _label =
        from _1 in String("label").Before(_ws)
        from name in Identifier.Before(_ws)
        from _2 in Char(':').Optional()
        select (DslStatement)new LabelStmt { Name = name };

    /// <summary>jump target</summary>
    private static readonly Parser<char, DslStatement> _jump =
        from _1 in String("jump").Before(_ws)
        from target in Identifier
        select (DslStatement)new JumpStmt { TargetLabel = target };

    /// <summary>call target</summary>
    private static readonly Parser<char, DslStatement> _call =
        from _1 in String("call").Before(_ws)
        from target in Identifier
        select (DslStatement)new CallStmt { TargetLabel = target };

    /// <summary>return</summary>
    private static readonly Parser<char, DslStatement> _return =
        String("return").ThenReturn((DslStatement)new ReturnStmt());

    /// <summary>back</summary>
    private static readonly Parser<char, DslStatement> _back =
        String("back").ThenReturn((DslStatement)new BackStmt());

    /// <summary>forward</summary>
    private static readonly Parser<char, DslStatement> _forward =
        String("forward").ThenReturn((DslStatement)new ForwardStmt());

    /// <summary>end</summary>
    private static readonly Parser<char, DslStatement> _end =
        String("end").ThenReturn((DslStatement)new EndStmt());

    /// <summary>scene "name"</summary>
    private static readonly Parser<char, DslStatement> _scene =
        from _1 in String("scene").Before(_ws)
        from name in QuotedString
        select (DslStatement)new SceneStmt { SceneName = name };

    /// <summary>save "slot" / load "slot"</summary>
    private static readonly Parser<char, DslStatement> _saveLoad =
        (from kw in String("save").Or(String("load"))
         from _1 in _ws
         from slot in QuotedString
         select kw == "save"
             ? (DslStatement)new SaveStmt { SlotId = slot }
             : (DslStatement)new LoadStmt { SlotId = slot });

    /// <summary>background "path"</summary>
    private static readonly Parser<char, DslStatement> _background =
        from _1 in String("background").Before(_ws)
        from path in QuotedString
        select (DslStatement)new BackgroundStmt { Path = path };

    /// <summary>duration=N 参数解析</summary>
    private static readonly Parser<char, double> _durationParam =
        from _1 in String("duration=")
        from d in Number
        select d;

    /// <summary>with "transition" duration=N — 过渡参数解析</summary>
    private static readonly Parser<char, (string name, double? duration)> _withTransition =
        from _1 in String("with").Before(_ws)
        from name in QuotedString.Before(_ws)
        from duration in Try(_durationParam).Optional()
        select (name, duration.HasValue ? (double?)duration.Value : null);

    /// <summary>show "target" [at (x, y)] [with "transition" duration=N]</summary>
    private static readonly Parser<char, DslStatement> _show =
        from _1 in String("show").Before(_ws)
        from target in QuotedString.Before(_ws)
        from pos in Try(_position).Optional()
        from transition in Try(_withTransition).Optional()
        select (DslStatement)new ShowStmt
        {
            Target = target,
            X = pos.HasValue ? pos.Value.x : null,
            Y = pos.HasValue ? pos.Value.y : null,
            Transition = transition.HasValue ? transition.Value.name : null,
            TransitionDuration = transition.HasValue ? transition.Value.duration : null
        };

    /// <summary>hide "target" [with "transition" duration=N]</summary>
    private static readonly Parser<char, DslStatement> _hide =
        from _1 in String("hide").Before(_ws)
        from target in QuotedString.Before(_ws)
        from transition in Try(_withTransition).Optional()
        select (DslStatement)new HideStmt
        {
            Target = target,
            Transition = transition.HasValue ? transition.Value.name : null,
            TransitionDuration = transition.HasValue ? transition.Value.duration : null
        };

    /// <summary>style "name" key=value key=value ...</summary>
    private static readonly Parser<char, DslStatement> _style =
        from _1 in String("style").Before(_ws)
        from name in QuotedString.Before(_ws)
        from props in _propPair.Many()
        select (DslStatement)new StyleStmt
        {
            Name = name,
            Properties = props.ToDictionary(p => p.key, p => p.value)
        };

    /// <summary>animate_block "target" x=100 y=200 opacity=0.5 duration=1.0 easing=xxx</summary>
    private static readonly Parser<char, DslStatement> _animateBlock =
        from _1 in String("animate_block").Before(_ws)
        from target in QuotedString.Before(_ws)
        from props in _propPair.Many()
        select ParseAnimateBlock(target, props);

    /// <summary>call_screen "scene_name" [store="key"] [with "k=v,k=v"]</summary>
    private static readonly Parser<char, DslStatement> _callScreen =
        from _1 in String("call_screen").Before(_ws)
        from sceneName in QuotedString.Before(_ws)
        from store in Try(String("store=").Then(QuotedString).Before(_ws)).Optional()
        from withParams in Try(String("with").Before(_ws).Then(QuotedString).Before(_ws)).Optional()
        select (DslStatement)new CallScreenStmt
        {
            SceneName = sceneName,
            StoreKey = store.HasValue ? store.Value : null,
            Params = withParams.HasValue
                ? withParams.Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => {
                        var eqIdx = p.IndexOf('=');
                        return eqIdx > 0
                            ? (p[..eqIdx].Trim(), p[(eqIdx + 1)..].Trim().Trim('"'))
                            : (p.Trim(), "");
                    }).ToList()
                : null
        };

    /// <summary>animate "target" property value [duration=N] [easing=xxx]</summary>
    private static readonly Parser<char, DslStatement> _animate =
        from _1 in String("animate").Before(_ws)
        from target in QuotedString.Before(_ws)
        from prop in Identifier.Before(_ws)
        from val in Number.Before(_ws)
        from duration in Try(String("duration=").Then(Number).Before(_ws)).Optional()
        from easing in Try(String("easing=").Then(Identifier).Before(_ws)).Optional()
        select (DslStatement)new AnimateStmt
        {
            Target = target,
            Property = prop,
            TargetValue = val,
            Duration = duration.HasValue ? duration.Value : null,
            Easing = easing.HasValue ? easing.Value : null
        };

    /// <summary>menu "title"</summary>
    private static readonly Parser<char, DslStatement> _menu =
        from _1 in String("menu").Before(_ws)
        from prompt in QuotedString
        select (DslStatement)new MenuStmt { Prompt = prompt };

    /// <summary>"选项文本" -> target_label</summary>
    private static readonly Parser<char, DslStatement> _menuOption =
        from text in QuotedString.Before(_ws)
        from _1 in String("->").Before(_ws)
        from target in Identifier
        select (DslStatement)new MenuOptionStmt { Text = text, TargetLabel = target };

    /// <summary>shake [intensity=N] [duration=N]</summary>
    private static readonly Parser<char, DslStatement> _shake =
        from _1 in String("shake").Before(_ws)
        from intensity in Try(String("intensity=").Then(Number).Before(_ws)).Optional()
        from duration in Try(String("duration=").Then(Number).Before(_ws)).Optional()
        select (DslStatement)new ShakeStmt
        {
            Intensity = intensity.HasValue ? intensity.Value : null,
            Duration = duration.HasValue ? duration.Value : null
        };

    /// <summary>skip</summary>
    private static readonly Parser<char, DslStatement> _skip =
        String("skip").ThenReturn((DslStatement)new ToggleSkipStmt());

    /// <summary>auto</summary>
    private static readonly Parser<char, DslStatement> _auto =
        String("auto").ThenReturn((DslStatement)new ToggleAutoStmt());

    /// <summary>gallery unlock "id" "imagePath" [title="..."] [scene="..."]</summary>
    private static readonly Parser<char, DslStatement> _galleryUnlock =
        from _1 in String("gallery").Before(_ws)
        from _2 in String("unlock").Before(_ws)
        from id in QuotedString.Before(_ws)
        from imagePath in QuotedString.Before(_ws)
        from title in Try(String("title=").Then(QuotedString).Before(_ws)).Optional()
        from sceneName in Try(String("scene=").Then(QuotedString).Before(_ws)).Optional()
        select (DslStatement)new GalleryUnlockStmt
        {
            Id = id,
            ImagePath = imagePath,
            Title = title.HasValue ? title.Value : null,
            SceneName = sceneName.HasValue ? sceneName.Value : null
        };

    /// <summary>debug "message" [level=Info]</summary>
    private static readonly Parser<char, DslStatement> _debugLog =
        from _1 in String("debug").Before(_ws)
        from message in QuotedString.Before(_ws)
        from level in Try(String("level=").Then(Identifier)).Optional()
        select (DslStatement)new DebugLogStmt
        {
            Message = message,
            Level = level.HasValue ? level.Value : null
        };

    /// <summary>nvl / nvl clear</summary>
    private static readonly Parser<char, DslStatement> _nvl =
        from _1 in String("nvl")
        from clear in Try(_ws.Then(String("clear"))).Optional()
        select (DslStatement)new NvlStmt { IsClear = clear.HasValue };

    /// <summary>input "prompt" store "key" [options=[...]]</summary>
    private static readonly Parser<char, DslStatement> _input =
        from _1 in String("input").Before(_ws)
        from prompt in QuotedString.Before(_ws)
        from _2 in String("store").Before(_ws)
        from storeKey in QuotedString.Before(_ws)
        from options in Try(
            String("options=").Then(Char('['))
                .Then(AnyCharExcept(']').ManyString())
                .Before(Char(']'))
        ).Optional()
        select (DslStatement)new InputStmt
        {
            Prompt = prompt,
            StoreKey = storeKey,
            Options = options.HasValue
                ? options.Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(o => o.Trim().Trim('"')).ToArray()
                : null
        };

    // ====== 块结构（缩进式，无花括号）======

    /// <summary>if {cond}</summary>
    private static readonly Parser<char, DslStatement> _if =
        from _1 in String("if").Before(_ws)
        from cond in Expression
        select (DslStatement)new IfStmt { Condition = cond };

    /// <summary>else if {cond}</summary>
    private static readonly Parser<char, DslStatement> _elseIf =
        from _1 in String("else").Before(_ws)
        from _2 in String("if").Before(_ws)
        from cond in Expression
        select (DslStatement)new ElseIfStmt { Condition = cond };

    /// <summary>else</summary>
    private static readonly Parser<char, DslStatement> _else =
        String("else").ThenReturn((DslStatement)new ElseStmt());

    /// <summary>while {cond}</summary>
    private static readonly Parser<char, DslStatement> _while =
        from _1 in String("while").Before(_ws)
        from cond in Expression
        select (DslStatement)new WhileStmt { Condition = cond };

    // ====== Phase 24: Ren'Py 功能对齐 ======

    /// <summary>window auto | window show | window hide</summary>
    private static readonly Parser<char, DslStatement> _window =
        from _1 in String("window").Before(_ws)
        from mode in OneOf(
            String("auto").ThenReturn("auto"),
            String("show").ThenReturn("show"),
            String("hide").ThenReturn("hide")
        )
        select (DslStatement)new WindowStmt { Mode = mode };

    /// <summary>block_rollback</summary>
    private static readonly Parser<char, DslStatement> _blockRollback =
        String("block_rollback").ThenReturn((DslStatement)new BlockRollbackStmt());

    /// <summary>fix_rollback</summary>
    private static readonly Parser<char, DslStatement> _fixRollback =
        String("fix_rollback").ThenReturn((DslStatement)new FixRollbackStmt());

    /// <summary>break — 退出当前循环</summary>
    private static readonly Parser<char, DslStatement> _break =
        String("break").ThenReturn((DslStatement)new BreakStmt());

    /// <summary>continue — 跳过当前迭代</summary>
    private static readonly Parser<char, DslStatement> _continue =
        String("continue").ThenReturn((DslStatement)new ContinueStmt());

    /// <summary>for "var" in {expr}</summary>
    private static readonly Parser<char, DslStatement> _for =
        from _1 in String("for").Before(_ws)
        from varName in QuotedString.Before(_ws)
        from _2 in String("in").Before(_ws)
        from src in Expression
        select (DslStatement)new ForStmt { VarName = varName, SourceExpr = src };

    // ====== 辅助方法 ======

    /// <summary>
    /// 解析 animate_block 的属性列表，分离动画属性和全局参数
    /// </summary>
    private static DslStatement ParseAnimateBlock(string target, IEnumerable<(string key, string value)> props)
    {
        var animations = new List<(string Property, double Value)>();
        double? duration = null;
        string? easing = null;

        foreach (var (key, value) in props)
        {
            if (key is "x" or "y" or "opacity" or "rotate" or "scale")
            {
                if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var val))
                    animations.Add((key, val));
            }
            else if (key == "duration")
            {
                if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var val))
                    duration = val;
            }
            else if (key == "easing")
            {
                easing = value;
            }
        }

        return new AnimateBlockStmt
        {
            Target = target,
            Animations = animations,
            Duration = duration,
            Easing = easing
        };
    }

    // ====== 主解析器 ======

    /// <summary>
    /// 处理 define/let 的 value + once 后缀
    /// </summary>
    private static DslStatement MakeDefineOrLet(string key, string rest, bool isDefine)
    {
        var trimmed = rest.Trim();
        bool hasOnce = trimmed.EndsWith(" once", StringComparison.OrdinalIgnoreCase);
        var valuePart = hasOnce ? trimmed[..^5].Trim() : trimmed;

        if (isDefine)
            return new DefineStmt { Key = key, ValuePart = valuePart };
        return new LetStmt { Key = key, ValuePart = valuePart };
    }

    /// <summary>
    /// 所有语句解析器（按优先级排列——长的关键字在前避免前缀冲突）
    /// </summary>
    private static readonly Parser<char, DslStatement>[] _statementParsers =
    [
        // 块结构（缩进式，无花括号）
        _elseIf,
        _else,
        _if,
        _while,
        _for,
        // 多字符关键字（长的在前）
        _navigate,
        _transition,
        _character,
        _background,
        _callScreen,
        _animateBlock,
        _blockRollback,
        _fixRollback,
        _continue,
        _break,
        _forward,
        _return,
        _animate,
        _galleryUnlock,
        // 视频关键字（长关键字在前，避免 pause_video 被 pause 截断）
        _stopVideo,
        _pauseVideo,
        _resumeVideo,
        _seekVideo,
        _cutscene,
        _video,
        // 单关键字
        _define,
        _scene,
        _input,
        _menu,
        _menuOption,
        _debugLog,
        _label,
        _jump,
        _call,
        _say,
        _set,
        _let,
        _bgm,
        _wait,
        _pause,
        _show,
        _hide,
        _shake,
        _back,
        _end,
        _nvl,
        _skip,
        _auto,
        _window,
        _saveLoad,
        _style,
    ];

    /// <summary>
    /// 解析单行 DSL 脚本为语句 AST
    /// </summary>
    /// <param name="line">DSL 行文本（已 Trim）</param>
    /// <param name="lineNumber">行号（0-based）</param>
    /// <returns>解析结果：成功返回 DslStatement，失败返回 null</returns>
    public static DslStatement? ParseLine(string line, int lineNumber = 0)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        foreach (var parser in _statementParsers)
        {
            var result = parser.Before(_ws).Before(End).Parse(line);
            if (result.Success)
            {
                result.Value.LineNumber = lineNumber;
                return result.Value;
            }
        }

        // 兜底：尝试解析为 UI 元素行（image/text/button 等）
        // 这些行在 scene 块内作为 ShowElementStmt 编译为 ShowElementCommand
        var element = DslParser.ParseElement(line);
        if (element != null)
        {
            var stmt = new ShowElementStmt { Element = element };
            stmt.LineNumber = lineNumber;
            return stmt;
        }

        return null;
    }

    /// <summary>
    /// 尝试解析单行 DSL 脚本
    /// </summary>
    /// <param name="line">DSL 行文本</param>
    /// <param name="lineNumber">行号</param>
    /// <param name="stmt">解析结果</param>
    /// <param name="error">错误信息</param>
    /// <returns>是否解析成功</returns>
    public static bool TryParseLine(string line, int lineNumber, out DslStatement? stmt, out string? error)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            stmt = null;
            error = null;
            return true;
        }

        foreach (var parser in _statementParsers)
        {
            var result = parser.Before(_ws).Before(End).Parse(line);
            if (result.Success)
            {
                result.Value.LineNumber = lineNumber;
                stmt = result.Value;
                error = null;
                return true;
            }
        }

        stmt = null;
        error = $"行 {lineNumber + 1}: 无法解析 DSL 语句 '{line}'";
        return false;
    }
}
