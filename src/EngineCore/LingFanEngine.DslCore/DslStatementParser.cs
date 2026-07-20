using System.Globalization;
using LingFanEngine.Abstractions.Interfaces.Events;
using Pidgin;
using static Pidgin.Parser;
using static Pidgin.Parser<char>;

namespace LingFanEngine.DslCore;

/// <summary>
/// 基于 Pidgin 的 DSL 语句解析器——逐行解析 DSL 脚本为 AST 语句
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

    /// <summary>say "text" [by "speaker" | speaker="speaker"] [clickable=true | okey] [noskip=true] [instant=true] [typewriter=true]</summary>
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
        from noskip in (
            // noskip=true 语法（Phase 37）
            Try(String("noskip=true").Before(_ws))
        ).Optional()
        from instant in (
            // instant=true 语法（DSL 2.0）
            Try(String("instant=true").Before(_ws))
        ).Optional()
        from typewriter in (
            // typewriter=true 语法（DSL 2.0——强制启用打字机效果）
            Try(String("typewriter=true").Before(_ws))
        ).Optional()
        // Phase 65: template="xxx" —— 对话框模板名
        from template in Try(String("template=").Then(QuotedString).Before(_ws)).Optional()
        // voice="path" —— 行内语音（DSL 语音支持）
        from voice in Try(String("voice=").Then(QuotedString).Before(_ws)).Optional()
        select (DslStatement)new SayStmt
        {
            Text = text,
            Speaker = speaker.HasValue ? speaker.Value : null,
            Clickable = clickable.HasValue,
            Noskip = noskip.HasValue,
            Instant = instant.HasValue,
            Typewriter = typewriter.HasValue ? true : null,
            Template = template.HasValue ? template.Value : null,
            VoicePath = voice.HasValue ? voice.Value : null
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

    /// <summary>local "key" value [once] — DSL 2.0，与 let 等效别名</summary>
    private static readonly Parser<char, DslStatement> _local =
        from _1 in String("local").Before(_ws)
        from key in QuotedString.Before(_ws)
        from rest in AnyCharExcept('\n', '\r').ManyString()
        select MakeDefineOrLet(key, rest, false);

    /// <summary>undef "key" — DSL 2.0，销毁变量</summary>
    private static readonly Parser<char, DslStatement> _undef =
        from _1 in String("undef").Before(_ws)
        from key in QuotedString
        select (DslStatement)new UndefStmt { Key = key };

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

    /// <summary>se "path" [volume=N] — DSL 2.0</summary>
    private static readonly Parser<char, DslStatement> _se =
        from _1 in String("se").Before(_ws)
        from path in QuotedString.Before(_ws)
        from volume in Try(String("volume=").Then(Number).Before(_ws)).Optional()
        select (DslStatement)new SeStmt
        {
            Path = path,
            Volume = volume.HasValue ? (float)volume.Value : null
        };

    /// <summary>ambient "path" [loop=true|false] [volume=N] — DSL 2.0</summary>
    private static readonly Parser<char, DslStatement> _ambient =
        from _1 in String("ambient").Before(_ws)
        from path in QuotedString.Before(_ws)
        from loop in Try(String("loop=").Then(String("true").Or(String("false"))).Before(_ws)).Optional()
        from volume in Try(String("volume=").Then(Number).Before(_ws)).Optional()
        select (DslStatement)new AmbientStmt
        {
            Path = path,
            Loop = !loop.HasValue || loop.Value == "true",
            Volume = volume.HasValue ? (float)volume.Value : null
        };

    /// <summary>voice "path" [volume=N] [auto_stop=true|false]</summary>
    private static readonly Parser<char, DslStatement> _voice =
        from _1 in String("voice").Before(_ws)
        from path in QuotedString.Before(_ws)
        from volume in Try(String("volume=").Then(Number).Before(_ws)).Optional()
        from autoStop in Try(String("auto_stop=").Then(String("true").Or(String("false"))).Before(_ws)).Optional()
        select (DslStatement)new VoiceStmt
        {
            Path = path,
            Volume = volume.HasValue ? (float)volume.Value : null,
            AutoStop = autoStop.HasValue ? (bool?)(autoStop.Value == "true") : null
        };

    /// <summary>stop_ambient — DSL 2.0</summary>
    private static readonly Parser<char, DslStatement> _stopAmbient =
        String("stop_ambient").ThenReturn((DslStatement)new StopAmbientStmt());

    /// <summary>stop_voice</summary>
    private static readonly Parser<char, DslStatement> _stopVoice =
        String("stop_voice").ThenReturn((DslStatement)new StopVoiceStmt());

    /// <summary>video "path" [volume=N] [loop=true|false] [autoplay=true|false]</summary>
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

    /// <summary>return [value] — DSL 2.0 支持返回值</summary>
    private static readonly Parser<char, DslStatement> _return =
        from _1 in String("return")
        from rest in Try(_ws.Then(AnyCharExcept('\n', '\r').AtLeastOnceString())).Optional()
        select (DslStatement)new ReturnStmt
        {
            ValuePart = rest.HasValue ? rest.Value.Trim() : null
        };

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

    /// <summary>save "slot" [title "标题"] [screenshot=true|false] / load "slot"</summary>
    private static readonly Parser<char, DslStatement> _saveLoad =
        (from kw in String("save").Or(String("load"))
         from _1 in _ws
         from slot in QuotedString.Before(_ws)
         // save 专有参数：title "标题" screenshot=true|false（DSL 2.0）
         from title in Try(String("title").Before(_ws).Then(QuotedString).Before(_ws)).Optional()
         from screenshot in Try(String("screenshot=").Then(String("true").Or(String("false"))).Before(_ws)).Optional()
         select kw == "save"
             ? (DslStatement)new SaveStmt
             {
                 SlotId = slot,
                 Title = title.HasValue ? title.Value : null,
                 Screenshot = !screenshot.HasValue || screenshot.Value == "true"
             }
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

    /// <summary>style "name"|name key=value key=value ...</summary>
    private static readonly Parser<char, DslStatement> _style =
        from _1 in String("style").Before(_ws)
        from name in QuotedString.Or(Identifier).Before(_ws)
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

    /// <summary>gallery_unlock "id" [title="..."] — DSL 2.0 短语法（无路径）</summary>
    private static readonly Parser<char, DslStatement> _galleryUnlockShort =
        from _1 in String("gallery_unlock").Before(_ws)
        from id in QuotedString.Before(_ws)
        from title in Try(String("title=").Then(QuotedString).Before(_ws)).Optional()
        from sceneName in Try(String("scene=").Then(QuotedString).Before(_ws)).Optional()
        select (DslStatement)new GalleryUnlockStmt
        {
            Id = id,
            ImagePath = "",
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

/// <summary>nvl / nvl clear / nvl exit</summary>
private static readonly Parser<char, DslStatement> _nvl =
    from _1 in String("nvl")
    from sub in Try(_ws.Then(String("clear"))).Or(Try(_ws.Then(String("exit")))).Optional()
    select (DslStatement)new NvlStmt
    {
        IsClear = sub.HasValue && sub.Value == "clear",
        IsExit = sub.HasValue && sub.Value == "exit",
    };

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

    // ====== Phase 38: 时间事件与通知 ======

    /// <summary>
    /// time_event day=1 hour=12 minute=30 target="scene" once=true condition="{expr}" desc="描述"
    /// </summary>
    private static readonly Parser<char, DslStatement> _timeEvent =
        from _1 in String("time_event").Before(_ws)
        from props in _propPair.Many()
        select ParseTimeEvent(props);

    /// <summary>time_pause</summary>
    private static readonly Parser<char, DslStatement> _timePause =
        String("time_pause").ThenReturn((DslStatement)new TimePauseStmt());

    /// <summary>time_resume</summary>
    private static readonly Parser<char, DslStatement> _timeResume =
        String("time_resume").ThenReturn((DslStatement)new TimeResumeStmt());

    /// <summary>skip_time N</summary>
    private static readonly Parser<char, DslStatement> _skipTime =
        from _1 in String("skip_time").Before(_ws)
        from minutes in Number.Before(_ws)
        select (DslStatement)new SkipTimeStmt { Minutes = (int)minutes };

    /// <summary>
    /// set_time_event "id" HOUR [minute=N] [day=N] [once=true|false] [weekdays="Mon,Tue"] [condition="{expr}"] [desc="描述"]
    /// </summary>
    private static readonly Parser<char, DslStatement> _setTimeEvent =
        from _1 in String("set_time_event").Before(_ws)
        from id in QuotedString.Before(_ws)
        from hour in Number.Before(_ws)
        from props in _propPair.Many()
        select ParseSetTimeEvent(id, (int)hour, props);

    /// <summary>unregister_time_event "id" [permanent|temporary]</summary>
    private static readonly Parser<char, DslStatement> _unregisterTimeEvent =
        from _1 in String("unregister_time_event").Before(_ws)
        from id in QuotedString.Before(_ws)
        from mode in Try(String("permanent").Before(_ws)).Or(Try(String("temporary").Before(_ws))).Optional()
        select (DslStatement)new UnregisterTimeEventStmt
        {
            Id = id,
            Mode = mode.HasValue
                ? (mode.Value == "permanent" ? UnregisterMode.Permanent : UnregisterMode.Temporary)
                : UnregisterMode.Normal
        };

    /// <summary>restore_time_event "id"</summary>
    private static readonly Parser<char, DslStatement> _restoreTimeEvent =
        from _1 in String("restore_time_event").Before(_ws)
        from id in QuotedString.Before(_ws)
        select (DslStatement)new RestoreTimeEventStmt { Id = id };

    /// <summary>notify "text" [type=warning] [duration=5.0]</summary>
    private static readonly Parser<char, DslStatement> _notify =
        from _1 in String("notify").Before(_ws)
        from text in QuotedString.Before(_ws)
        from type in Try(String("type=").Then(Identifier)).Before(_ws).Optional()
        from duration in Try(String("duration=").Then(Number)).Before(_ws).Optional()
        select (DslStatement)new NotifyStmt
        {
            Text = text,
            Type = type.HasValue ? type.Value : null,
            Duration = duration.HasValue ? duration.Value : null
        };

    /// <summary>
    /// 解析 time_event 属性列表为 TimeEventStmt
    /// </summary>
    private static DslStatement ParseTimeEvent(IEnumerable<(string key, string value)> props)
    {
        int day = 0;
        DayOfWeek[]? daysOfWeek = null;
        int? hour = null;
        int? minute = null;
        string target = "";
        bool once = true;
        string? condition = null;
        string? desc = null;

        foreach (var (key, value) in props)
        {
            switch (key)
            {
                case "day":
                    int.TryParse(value, out day);
                    break;
                case "weekday":
                    daysOfWeek = ParseDaysOfWeek(value);
                    break;
                case "hour":
                    if (int.TryParse(value, out var h)) hour = h;
                    break;
                case "minute":
                    if (int.TryParse(value, out var m)) minute = m;
                    break;
                case "target":
                    target = value;
                    break;
                case "once":
                    once = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
                    break;
                case "condition":
                    // 去掉花括号
                    condition = value.Trim().Trim('{', '}');
                    break;
                case "desc":
                    desc = value;
                    break;
            }
        }

        return new TimeEventStmt
        {
            TriggerDay = day,
            DaysOfWeek = daysOfWeek,
            TriggerHour = hour,
            TriggerMinute = minute,
            Target = target,
            IsOneShot = once,
            Condition = condition,
            Description = desc
        };
    }

    /// <summary>
    /// 解析星期几字符串为 DayOfWeek 数组
    /// <para>支持缩写 Mon/Tue/Wed/Thu/Fri/Sat/Sun 或全称 Monday/Tuesday 等，逗号分隔多选。</para>
    /// </summary>
    private static DayOfWeek[]? ParseDaysOfWeek(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<DayOfWeek>(parts.Length);

        foreach (var part in parts)
        {
            var dow = part.ToLowerInvariant() switch
            {
                "mon" or "monday" => DayOfWeek.Monday,
                "tue" or "tuesday" => DayOfWeek.Tuesday,
                "wed" or "wednesday" => DayOfWeek.Wednesday,
                "thu" or "thursday" => DayOfWeek.Thursday,
                "fri" or "friday" => DayOfWeek.Friday,
                "sat" or "saturday" => DayOfWeek.Saturday,
                "sun" or "sunday" => DayOfWeek.Sunday,
                _ => throw new FormatException($"无法识别的星期几: '{part}'。支持 Mon/Tue/Wed/Thu/Fri/Sat/Sun 或全称。")
            };
            if (!result.Contains(dow))
                result.Add(dow);
        }

        return result.Count > 0 ? result.ToArray() : null;
    }

    /// <summary>
    /// 解析 set_time_event 属性列表为 SetTimeEventStmt
    /// </summary>
    private static DslStatement ParseSetTimeEvent(string id, int hour, IEnumerable<(string key, string value)> props)
    {
        int? minute = null;
        int? day = null;
        DayOfWeek[]? daysOfWeek = null;
        bool once = false;
        string? condition = null;
        string? desc = null;

        foreach (var (key, value) in props)
        {
            switch (key)
            {
                case "minute":
                    if (int.TryParse(value, out var m)) minute = m;
                    break;
                case "day":
                    if (int.TryParse(value, out var d)) day = d;
                    break;
                case "weekdays":
                case "weekday":
                    daysOfWeek = ParseDaysOfWeek(value);
                    break;
                case "once":
                    once = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "condition":
                    condition = value.Trim().Trim('{', '}');
                    break;
                case "desc":
                    desc = value;
                    break;
            }
        }

        return new SetTimeEventStmt
        {
            Id = id,
            Hour = hour,
            Minute = minute,
            Day = day,
            DaysOfWeek = daysOfWeek,
            IsOneShot = once,
            Condition = condition,
            Description = desc
        };
    }

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

    // ====== Phase 44: 叙事增强 ======

    /// <summary>switch {expr}</summary>
    private static readonly Parser<char, DslStatement> _switch =
        from _1 in String("switch").Before(_ws)
        from expr in Expression
        select (DslStatement)new SwitchStmt { Expression = expr };

    /// <summary>case N</summary>
    private static readonly Parser<char, DslStatement> _case =
        from _1 in String("case").Before(_ws)
        from val in AnyCharExcept('\n', '\r').ManyString()
        select (DslStatement)new CaseStmt { Value = val.Trim() };

    /// <summary>default</summary>
    private static readonly Parser<char, DslStatement> _default =
        String("default").ThenReturn((DslStatement)new DefaultStmt());

    /// <summary>func name(param1, param2)</summary>
    private static readonly Parser<char, DslStatement> _func =
        from _1 in String("func").Before(_ws)
        from name in Identifier.Before(_ws)
        from _2 in Char('(')
        from paramsStr in AnyCharExcept(')').ManyString()
        from _3 in Char(')')
        select (DslStatement)new FuncStmt
        {
            Name = name,
            Parameters = paramsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => p.Trim()).ToList()
        };

    /// <summary>array "key" [item1, item2, ...] [once]</summary>
    private static readonly Parser<char, DslStatement> _array =
        from _1 in String("array").Before(_ws)
        from key in QuotedString.Before(_ws)
        from _2 in Char('[')
        from itemsStr in AnyCharExcept(']').ManyString()
        from _3 in Char(']').Before(_ws)
        from once in Try(String("once").Before(_ws)).Optional()
        select (DslStatement)new ArrayStmt
        {
            Key = key,
            Items = itemsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.Trim().Trim('"')).ToList(),
            IsDefine = once.HasValue
        };

    /// <summary>array_push "key" "value"</summary>
    private static readonly Parser<char, DslStatement> _arrayPush =
        from _1 in String("array_push").Before(_ws)
        from key in QuotedString.Before(_ws)
        from value in AnyCharExcept('\n', '\r').ManyString()
        select (DslStatement)new ArrayPushStmt { Key = key, ValuePart = value.Trim() };

    /// <summary>array_pop "key"</summary>
    private static readonly Parser<char, DslStatement> _arrayPop =
        from _1 in String("array_pop").Before(_ws)
        from key in QuotedString
        select (DslStatement)new ArrayPopStmt { Key = key };

    /// <summary>foreach "var" in "key"</summary>
    private static readonly Parser<char, DslStatement> _foreach =
        from _1 in String("foreach").Before(_ws)
        from varName in QuotedString.Before(_ws)
        from _2 in String("in").Before(_ws)
        from srcKey in QuotedString
        select (DslStatement)new ForeachStmt { VarName = varName, SourceKey = srcKey };

    /// <summary>dict "key" {"field":value,...} [once]</summary>
    private static readonly Parser<char, DslStatement> _dict =
        from _1 in String("dict").Before(_ws)
        from key in QuotedString.Before(_ws)
        from _2 in Char('{')
        from body in AnyCharExcept('}').ManyString()
        from _3 in Char('}').Before(_ws)
        from once in Try(String("once").Before(_ws)).Optional()
        select ParseDictStmt(key, body, once.HasValue);

    /// <summary>dict_set "key" "field" value</summary>
    private static readonly Parser<char, DslStatement> _dictSet =
        from _1 in String("dict_set").Before(_ws)
        from key in QuotedString.Before(_ws)
        from field in QuotedString.Before(_ws)
        from value in AnyCharExcept('\n', '\r').ManyString()
        select (DslStatement)new DictSetStmt { Key = key, Field = field, ValuePart = value.Trim() };

    // ====== Phase 45: UI 增强 ======

    /// <summary>popup "name" [width=N] [height=N] [mask=true|false]</summary>
    private static readonly Parser<char, DslStatement> _popup =
        from _1 in String("popup").Before(_ws)
        from name in QuotedString.Before(_ws)
        from props in _propPair.Many()
        select ParsePopup(name, props);

    /// <summary>zindex N</summary>
    private static readonly Parser<char, DslStatement> _zindex =
        from _1 in String("zindex").Before(_ws)
        from z in Number
        select (DslStatement)new ZindexStmt { ZIndex = (int)z };

    /// <summary>sprite "id" src="path" [x=N] [y=N] [fade=N]</summary>
    private static readonly Parser<char, DslStatement> _sprite =
        from _1 in String("sprite").Before(_ws)
        from id in QuotedString.Before(_ws)
        from props in _propPair.Many()
        select ParseSprite(id, props);

    /// <summary>sprite_state "id" emotion="smile"</summary>
    private static readonly Parser<char, DslStatement> _spriteState =
        from _1 in String("sprite_state").Before(_ws)
        from id in QuotedString.Before(_ws)
        from props in _propPair.Many()
        select (DslStatement)new SpriteStateStmt
        {
            Id = id,
            Emotion = props.FirstOrDefault(p => p.key == "emotion").value ?? ""
        };

    /// <summary>sprite_move "id" [x=N] [y=N] [duration=N]</summary>
    private static readonly Parser<char, DslStatement> _spriteMove =
        from _1 in String("sprite_move").Before(_ws)
        from id in QuotedString.Before(_ws)
        from props in _propPair.Many()
        select ParseSpriteMove(id, props);

    /// <summary>sprite_hide "id" [fade=N]</summary>
    private static readonly Parser<char, DslStatement> _spriteHide =
        from _1 in String("sprite_hide").Before(_ws)
        from id in QuotedString.Before(_ws)
        from props in _propPair.Many()
        select (DslStatement)new SpriteHideStmt
        {
            Id = id,
            Fade = GetPropDouble(props, "fade")
        };

    /// <summary>bg_switch "path" [transition=fade] [duration=N]</summary>
    private static readonly Parser<char, DslStatement> _bgSwitch =
        from _1 in String("bg_switch").Before(_ws)
        from path in QuotedString.Before(_ws)
        from props in _propPair.Many()
        select (DslStatement)new BgSwitchStmt
        {
            Path = path,
            Transition = props.FirstOrDefault(p => p.key == "transition").value,
            Duration = GetPropDouble(props, "duration")
        };

    /// <summary>text_typewriter speed=N</summary>
    private static readonly Parser<char, DslStatement> _textTypewriter =
        from _1 in String("text_typewriter").Before(_ws)
        from _2 in String("speed=")
        from speed in Number
        select (DslStatement)new TextTypewriterStmt { Speed = speed };

    // ====== Phase 46: Live2D ======

    /// <summary>live2d_char "id" src="path" [height=N] [x=N] [y=N] [fade=N] ...</summary>
    private static readonly Parser<char, DslStatement> _live2dChar =
        from _1 in String("live2d_char").Before(_ws)
        from id in QuotedString.Before(_ws)
        from props in _propPair.Many()
        select ParseLive2DChar(id, props);

    /// <summary>live2d_show "id"</summary>
    private static readonly Parser<char, DslStatement> _live2dShow =
        from _1 in String("live2d_show").Before(_ws)
        from id in QuotedString
        select (DslStatement)new Live2DShowStmt { Id = id };

    /// <summary>live2d_motion "id" name="motion" [fade=N] [loop=true|false]</summary>
    private static readonly Parser<char, DslStatement> _live2dMotion =
        from _1 in String("live2d_motion").Before(_ws)
        from id in QuotedString.Before(_ws)
        from props in _propPair.Many()
        select (DslStatement)new Live2DMotionStmt
        {
            Id = id,
            Name = props.FirstOrDefault(p => p.key == "name").value ?? "",
            Fade = GetPropDouble(props, "fade"),
            Loop = GetPropBool(props, "loop", true)
        };

    /// <summary>live2d_expr "id" name="expression" [fade=N]</summary>
    private static readonly Parser<char, DslStatement> _live2dExpr =
        from _1 in String("live2d_expr").Before(_ws)
        from id in QuotedString.Before(_ws)
        from props in _propPair.Many()
        select (DslStatement)new Live2DExprStmt
        {
            Id = id,
            Name = props.FirstOrDefault(p => p.key == "name").value ?? "",
            Fade = GetPropDouble(props, "fade")
        };

    /// <summary>live2d_param "id" param="BodyAngleX" value=-8 [weight=0.6]</summary>
    private static readonly Parser<char, DslStatement> _live2dParam =
        from _1 in String("live2d_param").Before(_ws)
        from id in QuotedString.Before(_ws)
        from props in _propPair.Many()
        select (DslStatement)new Live2DParamStmt
        {
            Id = id,
            ParamName = props.FirstOrDefault(p => p.key == "param").value ?? "",
            Value = GetPropDouble(props, "value") ?? 0,
            Weight = GetPropDouble(props, "weight") ?? 1.0
        };

    /// <summary>live2d_hide "id" [fade=N]</summary>
    private static readonly Parser<char, DslStatement> _live2dHide =
        from _1 in String("live2d_hide").Before(_ws)
        from id in QuotedString.Before(_ws)
        from props in _propPair.Many()
        select (DslStatement)new Live2DHideStmt
        {
            Id = id,
            Fade = GetPropDouble(props, "fade")
        };

    /// <summary>live2d_pause "id"</summary>
    private static readonly Parser<char, DslStatement> _live2dPause =
        from _1 in String("live2d_pause").Before(_ws)
        from id in QuotedString
        select (DslStatement)new Live2DPauseStmt { Id = id };

    /// <summary>live2d_resume "id"</summary>
    private static readonly Parser<char, DslStatement> _live2dResume =
        from _1 in String("live2d_resume").Before(_ws)
        from id in QuotedString
        select (DslStatement)new Live2DResumeStmt { Id = id };

    // ====== Phase 47: 存档/成就/章节 ======

    /// <summary>auto_save true|false</summary>
    private static readonly Parser<char, DslStatement> _autoSave =
        from _1 in String("auto_save").Before(_ws)
        from enabled in String("true").Or(String("false"))
        select (DslStatement)new AutoSaveStmt { Enabled = enabled == "true" };

    /// <summary>save_delete "slot"</summary>
    private static readonly Parser<char, DslStatement> _saveDelete =
        from _1 in String("save_delete").Before(_ws)
        from slot in QuotedString
        select (DslStatement)new SaveDeleteStmt { SlotId = slot };

    /// <summary>chapter "id" name "章节名" [unlock=true|false]</summary>
    private static readonly Parser<char, DslStatement> _chapter =
        from _1 in String("chapter").Before(_ws)
        from id in QuotedString.Before(_ws)
        from _2 in String("name").Before(_ws)
        from chapterName in QuotedString.Before(_ws)
        from unlock in Try(String("unlock=").Then(String("true").Or(String("false"))).Before(_ws)).Optional()
        select (DslStatement)new ChapterStmt
        {
            Id = id,
            ChapterName = chapterName,
            Unlock = !unlock.HasValue || unlock.Value == "true"
        };

    /// <summary>achievement "id" name "成就名"</summary>
    private static readonly Parser<char, DslStatement> _achievement =
        from _1 in String("achievement").Before(_ws)
        from id in QuotedString.Before(_ws)
        from _2 in String("name").Before(_ws)
        from achName in QuotedString
        select (DslStatement)new AchievementStmt { Id = id, AchievementName = achName };

    /// <summary>auto_speed N</summary>
    private static readonly Parser<char, DslStatement> _autoSpeed =
        from _1 in String("auto_speed").Before(_ws)
        from speed in Number
        select (DslStatement)new AutoSpeedStmt { Speed = speed };

    /// <summary>no_skip</summary>
    private static readonly Parser<char, DslStatement> _noSkip =
        String("no_skip").ThenReturn((DslStatement)new NoSkipStmt());

    /// <summary>force_skip</summary>
    private static readonly Parser<char, DslStatement> _forceSkip =
        String("force_skip").ThenReturn((DslStatement)new ForceSkipStmt());

    // ====== Phase 48: 视频增强 ======

    /// <summary>video_skipable true|false</summary>
    private static readonly Parser<char, DslStatement> _videoSkipable =
        from _1 in String("video_skipable").Before(_ws)
        from enabled in String("true").Or(String("false"))
        select (DslStatement)new VideoSkipableStmt { Enabled = enabled == "true" };

    /// <summary>video_auto_nav "scene"</summary>
    private static readonly Parser<char, DslStatement> _videoAutoNav =
        from _1 in String("video_auto_nav").Before(_ws)
        from scene in QuotedString
        select (DslStatement)new VideoAutoNavStmt { SceneName = scene };

    // ====== 辅助方法 ======

    /// <summary>从属性列表中获取 double 值</summary>
    private static double? GetPropDouble(IEnumerable<(string key, string value)> props, string name)
    {
        var val = props.FirstOrDefault(p => p.key == name).value;
        if (val == null) return null;
        return double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>从属性列表中获取 bool 值</summary>
    private static bool GetPropBool(IEnumerable<(string key, string value)> props, string name, bool defaultValue)
    {
        var val = props.FirstOrDefault(p => p.key == name).value;
        if (val == null) return defaultValue;
        return !string.Equals(val, "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>解析 popup 属性</summary>
    private static DslStatement ParsePopup(string name, IEnumerable<(string key, string value)> props)
    {
        double? width = null, height = null;
        bool mask = true;
        foreach (var (key, value) in props)
        {
            switch (key)
            {
                case "width" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var w):
                    width = w; break;
                case "height" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var h):
                    height = h; break;
                case "mask":
                    mask = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase); break;
            }
        }
        return new PopupStmt { Name = name, Width = width, Height = height, Mask = mask };
    }

    /// <summary>解析 sprite 属性</summary>
    private static DslStatement ParseSprite(string id, IEnumerable<(string key, string value)> props)
    {
        string src = "";
        double? x = null, y = null, fade = null;
        foreach (var (key, value) in props)
        {
            switch (key)
            {
                case "src": src = value; break;
                case "x" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var xv):
                    x = xv; break;
                case "y" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var yv):
                    y = yv; break;
                case "fade" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv):
                    fade = fv; break;
            }
        }
        return new SpriteStmt { Id = id, Source = src, X = x, Y = y, Fade = fade };
    }

    /// <summary>解析 sprite_move 属性</summary>
    private static DslStatement ParseSpriteMove(string id, IEnumerable<(string key, string value)> props)
    {
        double? x = null, y = null, duration = null;
        foreach (var (key, value) in props)
        {
            switch (key)
            {
                case "x" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var xv):
                    x = xv; break;
                case "y" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var yv):
                    y = yv; break;
                case "duration" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv):
                    duration = dv; break;
            }
        }
        return new SpriteMoveStmt { Id = id, X = x, Y = y, Duration = duration };
    }

    /// <summary>解析 live2d_char 属性</summary>
    private static DslStatement ParseLive2DChar(string id, IEnumerable<(string key, string value)> props)
    {
        string src = "";
        double? height = null, x = null, y = null, fade = null;
        bool loop = true, seamless = false;
        double blinkRate = 3.0;
        bool mouseTrack = true, voiceSync = true;

        foreach (var (key, value) in props)
        {
            switch (key)
            {
                case "src": src = value; break;
                case "height" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hv):
                    height = hv; break;
                case "x" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var xv):
                    x = xv; break;
                case "y" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var yv):
                    y = yv; break;
                case "fade" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv):
                    fade = fv; break;
                case "loop": loop = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase); break;
                case "seamless": seamless = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase); break;
                case "blink_rate" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var br):
                    blinkRate = br; break;
                case "mouse_track_head": mouseTrack = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase); break;
                case "voice_sync_mouth": voiceSync = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase); break;
            }
        }
        return new Live2DCharStmt
        {
            Id = id, Source = src, Height = height, X = x, Y = y, Fade = fade,
            Loop = loop, Seamless = seamless, BlinkRate = blinkRate,
            MouseTrackHead = mouseTrack, VoiceSyncMouth = voiceSync
        };
    }

    /// <summary>解析 dict 语句</summary>
    private static DslStatement ParseDictStmt(string key, string body, bool isDefine)
    {
        var fields = new List<(string Field, string Value)>();
        // 简单解析 "field":value, "field2":value2
        var parts = body.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var colonIdx = part.IndexOf(':');
            if (colonIdx > 0)
            {
                var field = part[..colonIdx].Trim().Trim('"');
                var val = part[(colonIdx + 1)..].Trim().Trim('"');
                fields.Add((field, val));
            }
        }
        return new DictStmt { Key = key, Fields = fields, IsDefine = isDefine };
    }

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
        _galleryUnlockShort,
        _galleryUnlock,
        // 视频关键字（长关键字在前，避免 pause_video 被 pause 截断）
        _stopVideo,
        _pauseVideo,
        _resumeVideo,
        _seekVideo,
        _cutscene,
        _video,
        // stop_ambient 必须在 ambient 之前（长关键字优先）
        _stopAmbient,
        _ambient,
        // voice / stop_voice —— DSL 语音支持
        _stopVoice,
        _voice,
        // set_time_event / unregister_time_event / restore_time_event 必须在 set 之前（长关键字优先）
        _setTimeEvent,
        _unregisterTimeEvent,
        _restoreTimeEvent,
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
        _local,
        _undef,
        _bgm,
        _se,
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
        // Phase 38: 时间事件与通知
        _timeEvent,
        _timePause,
        _timeResume,
        _skipTime,
        _notify,
        // Phase 44: 叙事增强
        _switch,
        _default,
        _case,
        _func,
        _arrayPush,
        _arrayPop,
        _array,
        _foreach,
        _dictSet,
        _dict,
        // Phase 45: UI 增强
        _spriteState,
        _spriteMove,
        _spriteHide,
        _sprite,
        _bgSwitch,
        _textTypewriter,
        _popup,
        _zindex,
        // Phase 46: Live2D
        _live2dChar,
        _live2dShow,
        _live2dMotion,
        _live2dExpr,
        _live2dParam,
        _live2dHide,
        _live2dPause,
        _live2dResume,
        // Phase 47: 存档/成就/章节
        _autoSave,
        _saveDelete,
        _chapter,
        _achievement,
        _autoSpeed,
        _noSkip,
        _forceSkip,
        // Phase 48: 视频增强
        _videoSkipable,
        _videoAutoNav,
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
