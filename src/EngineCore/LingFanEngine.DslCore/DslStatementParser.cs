using System.Globalization;
using LingFanEngine.Abstractions.Interfaces.Events;
using Parlot;
using Parlot.Fluent;
using static Parlot.Fluent.Parsers;

namespace LingFanEngine.DslCore;

/// <summary>
/// 基于 Parlot 的 DSL 语句解析器——逐行解析 DSL 脚本为 AST 语句
/// <para>2026-09：由 Pidgin（本地 fork DLL）迁移至 Parlot 1.5.8（NuGet），语义同构。</para>
/// <para>迁移要点：① Pidgin 的 Try 显式回溯全部移除（Parlot 组合子失败自动复位游标）；
/// ② Ws 必须用 ZeroOrOne(Literals.WhiteSpace) 包裹——原生 WhiteSpaceLiteral 零匹配即失败，
/// 会导致行尾空白跳过在 EOF 必然失败（v1.5.8 源码实证）；③ 108 条语句解析器顺序表原样保留。</para>
/// </summary>
public static class DslStatementParser
{
    // ====== 基础 token ======

    /// <summary>跳过空白（贪婪，零或多——与 Pidgin SkipWhitespaces 语义严格对齐；原生 WhiteSpaceLiteral 零匹配会失败，必须 ZeroOrOne 包裹）</summary>
    private static readonly Parser<TextSpan> _ws =
        ZeroOrOne(Literals.WhiteSpace(includeNewLines: true));

    /// <summary>转义序列：\" \\ \n \t \r；未知转义保留两字符原样（如 Windows 路径 "C:\dir"），与 DslParser/ExpressionParser 语义一致</summary>
    private static readonly Parser<string> EscapeSequence =
        Literals.Char('\\')
            .SkipAnd(Literals.Pattern(_ => true, 1, 1))
            .Then(c => c.ToString() switch
            {
                "\"" => "\"",
                "\\" => "\\",
                "n" => "\n",
                "t" => "\t",
                "r" => "\r",
                var other => "\\" + other,
            });

    /// <summary>字符串段：普通字符段（至少一个非引号非反斜杠）或转义序列（声明顺序：先转义后组合）</summary>
    private static readonly Parser<string> StringSegment =
        Literals.NoneOf("\"\\")
            .Then(s => s.ToString())
            .Or(EscapeSequence);

    /// <summary>双引号字符串（支持 \" 与 \\ 转义；未知转义保留两字符原样，存量零破坏）</summary>
    private static readonly Parser<string> QuotedString =
        Literals.Char('"')
            .SkipAnd(ZeroOrMany(StringSegment).Then(ss => string.Concat(ss)))
            .AndSkip(Literals.Char('"'))
            .Named("quoted string");

    /// <summary>标识符 \w+（字母/数字/下划线）</summary>
    private static readonly Parser<string> Identifier =
        Literals.Pattern(c => char.IsLetterOrDigit(c) || c == '_')
            .Then(s => s.ToString())
            .Named("identifier");

    /// <summary>数字（整数或浮点数）</summary>
    private static readonly Parser<double> Number =
        Capture(
            Literals.Pattern(char.IsDigit)
                .AndSkip(ZeroOrOne(
                    Literals.Char('.').AndSkip(Literals.Pattern(char.IsDigit))
                ))
        ).Then(s => double.Parse(s.ToString(), CultureInfo.InvariantCulture))
        .Named("number");

    /// <summary>行尾文本：任意非换行字符，零或多（对应 Pidgin AnyCharExcept('\n','\r').ManyString()）</summary>
    private static readonly Parser<string> RestToEol =
        AnyCharBefore(Literals.Char('\n').Or(Literals.Char('\r')), canBeEmpty: true)
            .Then(s => s.ToString());

    /// <summary>行尾文本（至少一字符——对应 Pidgin AnyCharExcept(...).AtLeastOnceString()）</summary>
    private static readonly Parser<string> RestToEolNonEmpty =
        AnyCharBefore(Literals.Char('\n').Or(Literals.Char('\r')), canBeEmpty: false)
            .Then(s => s.ToString());

    /// <summary>{表达式}——返回花括号内的内容</summary>
    private static readonly Parser<string> Expression =
        Literals.Char('{')
            .SkipAnd(AnyCharBefore(Literals.Char('}'), canBeEmpty: true))
            .AndSkip(Literals.Char('}'))
            .Then(s => s.ToString().Trim())
            .Named("expression");

    /// <summary>坐标 (x, y)——尾部跳过空白，使 at (x, y) 与后续 with "..." 连写可用（文档声明的完整语法）</summary>
    private static readonly Parser<(double x, double y)> _position =
        Literals.Text("at").AndSkip(_ws)
            .AndSkip(Literals.Char('(')).AndSkip(_ws)
            .And(Number.AndSkip(_ws))
            .AndSkip(Literals.Char(',')).AndSkip(_ws)
            .And(Number.AndSkip(_ws))
            .AndSkip(Literals.Char(')'))
            .AndSkip(_ws)
            .Then(t => (t.Item2, t.Item3));

    // ====== 语句解析器 ======

    /// <summary>say 前段收窄载体（Parlot Sequence 元组上限 7，say 需 8 个连续值，故中段打包）</summary>
    private sealed record SayPrefix(string Text, string? Speaker, bool Clickable);

    /// <summary>say "text" [by "speaker" | speaker="speaker"] [clickable=true | okey] [noskip=true] [instant=true] [typewriter=true|false]</summary>
    private static readonly Parser<DslStatement> _say =
        Literals.Text("say").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(
                // speaker="speaker" 语法（故事文件中使用的格式）
                Literals.Text("speaker=").SkipAnd(QuotedString).AndSkip(_ws)
                // by "speaker" 语法（兼容旧格式）
                .Or(Literals.Text("by").AndSkip(_ws).SkipAnd(QuotedString).AndSkip(_ws))
                .Optional())
            .And(
                // clickable=true 语法
                Literals.Text("clickable=true").AndSkip(_ws)
                // okey 语法糖（等价于 clickable=true）
                .Or(Literals.Text("okey").AndSkip(_ws))
                .Optional())
            .Then(t => new SayPrefix(t.Item1, t.Item2.HasValue ? t.Item2.Value : null, t.Item3.HasValue))
            .And(Literals.Text("noskip=true").AndSkip(_ws).Optional())
            .And(Literals.Text("instant=true").AndSkip(_ws).Optional())
            .And(Literals.Text("typewriter=true").Then(true).Or(Literals.Text("typewriter=false").Then(false)).AndSkip(_ws).Optional())
            .And(Literals.Text("template=").SkipAnd(QuotedString).AndSkip(_ws).Optional())
            .And(Literals.Text("voice=").SkipAnd(QuotedString).AndSkip(_ws).Optional())
            .Then(t => (DslStatement)new SayStmt
            {
                Text = t.Item1.Text,
                Speaker = t.Item1.Speaker,
                Clickable = t.Item1.Clickable,
                Noskip = t.Item2.HasValue,
                Instant = t.Item3.HasValue,
                Typewriter = t.Item4.HasValue ? t.Item4.Value : null,
                Template = t.Item5.HasValue ? t.Item5.Value : null,
                VoicePath = t.Item6.HasValue ? t.Item6.Value : null
            });

    /// <summary>navigate "path" [scene "name"]</summary>
    private static readonly Parser<DslStatement> _navigate =
        Literals.Text("navigate").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(Literals.Text("scene").AndSkip(_ws).SkipAnd(QuotedString).Optional())
            .Then(t => (DslStatement)new NavigateStmt
            {
                Path = t.Item1,
                SceneName = t.Item2.HasValue ? t.Item2.Value : null
            });

    /// <summary>character "key" name="xxx" color="#xxx" font="xxx" ...</summary>
    private static readonly Parser<string> PropKey =
        Literals.Pattern(c => char.IsLetter(c) || c == '_')
            .Then(s => s.ToString());

    /// <summary>属性值：引号字符串或裸值（到下一个空白）</summary>
    private static readonly Parser<string> PropValue =
        QuotedString
            .Or(Literals.Pattern(c => c != ' ' && c != '\t' && c != '\n' && c != '\r').Then(s => s.ToString()));

    /// <summary>单个 key=value 属性对</summary>
    private static readonly Parser<(string key, string value)> _propPair =
        PropKey.AndSkip(_ws)
            .AndSkip(Literals.Char('='))
            .AndSkip(_ws)
            .And(PropValue.AndSkip(_ws))
            .Then(t => (t.Item1, t.Item2));

    /// <summary>character "key" name="xxx" color="#xxx" ...</summary>
    private static readonly Parser<DslStatement> _character =
        Literals.Text("character").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(ZeroOrMany(_propPair))
            .Then(t => (DslStatement)new CharacterStmt
            {
                Key = t.Item1,
                Properties = t.Item2.ToDictionary(p => p.key, p => p.value)
            });

    /// <summary>set "key" value</summary>
    private static readonly Parser<DslStatement> _set =
        Literals.Text("set").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(RestToEol)
            .Then(t => (DslStatement)new SetStmt
            {
                Key = t.Item1,
                ValuePart = t.Item2.Trim()
            });

    /// <summary>define "key" value once</summary>
    private static readonly Parser<DslStatement> _define =
        Literals.Text("define").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(RestToEol)
            .Then(t => MakeDefineOrLet(t.Item1, t.Item2, true));

    /// <summary>let "key" value once</summary>
    private static readonly Parser<DslStatement> _let =
        Literals.Text("let").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(RestToEol)
            .Then(t => MakeDefineOrLet(t.Item1, t.Item2, false));

    /// <summary>local "key" value [once] — DSL 2.0，与 let 等效别名</summary>
    private static readonly Parser<DslStatement> _local =
        Literals.Text("local").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(RestToEol)
            .Then(t => MakeDefineOrLet(t.Item1, t.Item2, false));

    /// <summary>undef "key" — DSL 2.0，销毁变量</summary>
    private static readonly Parser<DslStatement> _undef =
        Literals.Text("undef").AndSkip(_ws)
            .SkipAnd(QuotedString)
            .Then(key => (DslStatement)new UndefStmt { Key = key });

    /// <summary>bgm "path" [volume=N]</summary>
    private static readonly Parser<DslStatement> _bgm =
        Literals.Text("bgm").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(Literals.Text("volume=").SkipAnd(Number).AndSkip(_ws).Optional())
            .Then(t => (DslStatement)new BgmStmt
            {
                Path = t.Item1,
                Volume = t.Item2.HasValue ? (float)t.Item2.Value : null
            });

    /// <summary>se "path" [volume=N] — DSL 2.0</summary>
    private static readonly Parser<DslStatement> _se =
        Literals.Text("se").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(Literals.Text("volume=").SkipAnd(Number).AndSkip(_ws).Optional())
            .Then(t => (DslStatement)new SeStmt
            {
                Path = t.Item1,
                Volume = t.Item2.HasValue ? (float)t.Item2.Value : null
            });

    /// <summary>ambient "path" [loop=true|false] [volume=N] — DSL 2.0</summary>
    private static readonly Parser<DslStatement> _ambient =
        Literals.Text("ambient").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(Literals.Text("loop=").SkipAnd(Literals.Text("true").Or(Literals.Text("false"))).AndSkip(_ws).Optional())
            .And(Literals.Text("volume=").SkipAnd(Number).AndSkip(_ws).Optional())
            .Then(t => (DslStatement)new AmbientStmt
            {
                Path = t.Item1,
                Loop = !t.Item2.HasValue || t.Item2.Value == "true",
                Volume = t.Item3.HasValue ? (float)t.Item3.Value : null
            });

    /// <summary>voice "path" [volume=N] [auto_stop=true|false]</summary>
    private static readonly Parser<DslStatement> _voice =
        Literals.Text("voice").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(Literals.Text("volume=").SkipAnd(Number).AndSkip(_ws).Optional())
            .And(Literals.Text("auto_stop=").SkipAnd(Literals.Text("true").Or(Literals.Text("false"))).AndSkip(_ws).Optional())
            .Then(t => (DslStatement)new VoiceStmt
            {
                Path = t.Item1,
                Volume = t.Item2.HasValue ? (float)t.Item2.Value : null,
                AutoStop = t.Item3.HasValue ? (bool?)(t.Item3.Value == "true") : null
            });

    /// <summary>stop_ambient — DSL 2.0</summary>
    private static readonly Parser<DslStatement> _stopAmbient =
        Literals.Text("stop_ambient").Then((DslStatement)new StopAmbientStmt());

    /// <summary>stop_voice</summary>
    private static readonly Parser<DslStatement> _stopVoice =
        Literals.Text("stop_voice").Then((DslStatement)new StopVoiceStmt());

    /// <summary>video "path" [volume=N] [loop=true|false] [autoplay=true|false]</summary>
    private static readonly Parser<DslStatement> _video =
        Literals.Text("video").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(Literals.Text("volume=").SkipAnd(Number).AndSkip(_ws).Optional())
            .And(Literals.Text("loop=").SkipAnd(Literals.Text("true").Or(Literals.Text("false"))).AndSkip(_ws).Optional())
            .And(Literals.Text("autoplay=").SkipAnd(Literals.Text("true").Or(Literals.Text("false"))).AndSkip(_ws).Optional())
            .Then(t => (DslStatement)new VideoStmt
            {
                Path = t.Item1,
                Volume = t.Item2.HasValue ? (float)t.Item2.Value : null,
                Loop = t.Item3.HasValue && t.Item3.Value == "true",
                AutoPlay = !t.Item4.HasValue || t.Item4.Value == "true"
            });

    /// <summary>stop_video</summary>
    private static readonly Parser<DslStatement> _stopVideo =
        // 原 Pidgin 写法 Before(_ws.Or(End))：SkipWhitespaces 从不失败，Or(End) 为死分支，行为等价于 Before(_ws)
        Literals.Text("stop_video").AndSkip(_ws)
            .Then((DslStatement)new StopVideoStmt());

    /// <summary>pause_video</summary>
    private static readonly Parser<DslStatement> _pauseVideo =
        Literals.Text("pause_video").AndSkip(_ws)
            .Then((DslStatement)new PauseVideoStmt());

    /// <summary>resume_video</summary>
    private static readonly Parser<DslStatement> _resumeVideo =
        Literals.Text("resume_video").AndSkip(_ws)
            .Then((DslStatement)new ResumeVideoStmt());

    /// <summary>seek_video N</summary>
    private static readonly Parser<DslStatement> _seekVideo =
        Literals.Text("seek_video").AndSkip(_ws)
            .SkipAnd(Number)
            .Then(position => (DslStatement)new SeekVideoStmt { Position = position });

    /// <summary>cutscene "path" [skipable=true|false] [volume=N]</summary>
    private static readonly Parser<DslStatement> _cutscene =
        Literals.Text("cutscene").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(Literals.Text("skipable=").SkipAnd(Literals.Text("true").Or(Literals.Text("false"))).AndSkip(_ws).Optional())
            .And(Literals.Text("volume=").SkipAnd(Number).AndSkip(_ws).Optional())
            .Then(t => (DslStatement)new CutsceneStmt
            {
                Path = t.Item1,
                Skipable = !t.Item2.HasValue || t.Item2.Value == "true",
                Volume = t.Item3.HasValue ? (float)t.Item3.Value : null
            });

    /// <summary>wait N [skipable]</summary>
    private static readonly Parser<DslStatement> _wait =
        Literals.Text("wait").AndSkip(_ws)
            .And(Number.AndSkip(_ws))
            .And(Literals.Text("skipable").AndSkip(_ws).Optional())
            .Then(t => (DslStatement)new WaitStmt { Seconds = t.Item2, IsSkipable = t.Item3.HasValue });

    /// <summary>pause [N] [hard]</summary>
    private static readonly Parser<DslStatement> _pause =
        Literals.Text("pause").AndSkip(_ws)
            .And(Number.AndSkip(_ws).Optional())
            .And(Literals.Text("hard").AndSkip(_ws).Optional())
            .Then(t => (DslStatement)new PauseStmt
            {
                Seconds = t.Item2.HasValue ? t.Item2.Value : null,
                IsHard = t.Item3.HasValue
            });

    /// <summary>transition "type" [duration=N]</summary>
    private static readonly Parser<DslStatement> _transition =
        Literals.Text("transition").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(Literals.Text("duration=").SkipAnd(Number).AndSkip(_ws).Optional())
            .Then(t => (DslStatement)new TransitionStmt
            {
                Type = t.Item1,
                Duration = t.Item2.HasValue ? t.Item2.Value : null
            });

    /// <summary>label name:</summary>
    private static readonly Parser<DslStatement> _label =
        Literals.Text("label").AndSkip(_ws)
            .SkipAnd(Identifier.AndSkip(_ws))
            .AndSkip(Literals.Char(':').Optional())
            .Then(name => (DslStatement)new LabelStmt { Name = name });

    /// <summary>jump target</summary>
    private static readonly Parser<DslStatement> _jump =
        Literals.Text("jump").AndSkip(_ws)
            .SkipAnd(Identifier)
            .Then(target => (DslStatement)new JumpStmt { TargetLabel = target });

    /// <summary>call target</summary>
    private static readonly Parser<DslStatement> _call =
        Literals.Text("call").AndSkip(_ws)
            .SkipAnd(Identifier)
            .Then(target => (DslStatement)new CallStmt { TargetLabel = target });

    /// <summary>return [value] — DSL 2.0 支持返回值</summary>
    private static readonly Parser<DslStatement> _return =
        Literals.Text("return")
            .And(_ws.SkipAnd(RestToEolNonEmpty).Optional())
            .Then(t => (DslStatement)new ReturnStmt
            {
                ValuePart = t.Item2.HasValue ? t.Item2.Value.Trim() : null
            });

    /// <summary>back</summary>
    private static readonly Parser<DslStatement> _back =
        Literals.Text("back").Then((DslStatement)new BackStmt());

    /// <summary>forward</summary>
    private static readonly Parser<DslStatement> _forward =
        Literals.Text("forward").Then((DslStatement)new ForwardStmt());

    /// <summary>end</summary>
    private static readonly Parser<DslStatement> _end =
        Literals.Text("end").Then((DslStatement)new EndStmt());

    /// <summary>scene "name"</summary>
    private static readonly Parser<DslStatement> _scene =
        Literals.Text("scene").AndSkip(_ws)
            .SkipAnd(QuotedString)
            .Then(name => (DslStatement)new SceneStmt { SceneName = name });

    /// <summary>save "slot" [title "标题"] [screenshot=true|false] / load "slot"</summary>
    private static readonly Parser<DslStatement> _saveLoad =
        Literals.Text("save").Or(Literals.Text("load")).AndSkip(_ws)
            .And(QuotedString.AndSkip(_ws))
            // save 专有参数：title "标题" screenshot=true|false（DSL 2.0）
            .And(Literals.Text("title").AndSkip(_ws).SkipAnd(QuotedString).AndSkip(_ws).Optional())
            .And(Literals.Text("screenshot=").SkipAnd(Literals.Text("true").Or(Literals.Text("false"))).AndSkip(_ws).Optional())
            .Then(t => t.Item1 == "save"
                ? (DslStatement)new SaveStmt
                {
                    SlotId = t.Item2,
                    Title = t.Item3.HasValue ? t.Item3.Value : null,
                    Screenshot = !t.Item4.HasValue || t.Item4.Value == "true"
                }
                : (DslStatement)new LoadStmt { SlotId = t.Item2 });

    /// <summary>background "path"</summary>
    private static readonly Parser<DslStatement> _background =
        Literals.Text("background").AndSkip(_ws)
            .SkipAnd(QuotedString)
            .Then(path => (DslStatement)new BackgroundStmt { Path = path });

    /// <summary>duration=N 参数解析</summary>
    private static readonly Parser<double> _durationParam =
        Literals.Text("duration=").SkipAnd(Number);

    /// <summary>with "transition" duration=N — 过渡参数解析</summary>
    private static readonly Parser<(string name, double? duration)> _withTransition =
        Literals.Text("with").AndSkip(_ws)
            .And(QuotedString.AndSkip(_ws))
            .And(_durationParam.Optional())
            .Then(t => (t.Item2, t.Item3.HasValue ? (double?)t.Item3.Value : null));

    /// <summary>show "target" [at (x, y)] [with "transition" duration=N]</summary>
    private static readonly Parser<DslStatement> _show =
        Literals.Text("show").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(_position.Optional())
            .And(_withTransition.Optional())
            .Then(t => (DslStatement)new ShowStmt
            {
                Target = t.Item1,
                X = t.Item2.HasValue ? t.Item2.Value.x : null,
                Y = t.Item2.HasValue ? t.Item2.Value.y : null,
                Transition = t.Item3.HasValue ? t.Item3.Value.name : null,
                TransitionDuration = t.Item3.HasValue ? t.Item3.Value.duration : null
            });

    /// <summary>hide "target" [with "transition" duration=N]</summary>
    private static readonly Parser<DslStatement> _hide =
        Literals.Text("hide").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(_withTransition.Optional())
            .Then(t => (DslStatement)new HideStmt
            {
                Target = t.Item1,
                Transition = t.Item2.HasValue ? t.Item2.Value.name : null,
                TransitionDuration = t.Item2.HasValue ? t.Item2.Value.duration : null
            });

    /// <summary>style "name"|name key=value key=value ...</summary>
    private static readonly Parser<DslStatement> _style =
        Literals.Text("style").AndSkip(_ws)
            .SkipAnd(QuotedString.Or(Identifier).AndSkip(_ws))
            .And(ZeroOrMany(_propPair))
            .Then(t => (DslStatement)new StyleStmt
            {
                Name = t.Item1,
                Properties = t.Item2.ToDictionary(p => p.key, p => p.value)
            });

    /// <summary>animate_block "target" x=100 y=200 opacity=0.5 duration=1.0 easing=xxx</summary>
    private static readonly Parser<DslStatement> _animateBlock =
        Literals.Text("animate_block").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(ZeroOrMany(_propPair))
            .Then(t => ParseAnimateBlock(t.Item1, t.Item2));

    /// <summary>call_screen "scene_name" [store="key"] [with "k=v,k=v"]</summary>
    private static readonly Parser<DslStatement> _callScreen =
        Literals.Text("call_screen").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(Literals.Text("store=").SkipAnd(QuotedString).AndSkip(_ws).Optional())
            .And(Literals.Text("with").AndSkip(_ws).SkipAnd(QuotedString).AndSkip(_ws).Optional())
            .Then(t => (DslStatement)new CallScreenStmt
            {
                SceneName = t.Item1,
                StoreKey = t.Item2.HasValue ? t.Item2.Value : null,
                Params = t.Item3.HasValue
                    ? t.Item3.Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => {
                            var eqIdx = p.IndexOf('=');
                            return eqIdx > 0
                                ? (p[..eqIdx].Trim(), p[(eqIdx + 1)..].Trim().Trim('"'))
                                : (p.Trim(), "");
                        }).ToList()
                    : null
            });

    /// <summary>animate "target" property value [duration=N] [easing=xxx]</summary>
    private static readonly Parser<DslStatement> _animate =
        Literals.Text("animate").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(Identifier.AndSkip(_ws))
            .And(Number.AndSkip(_ws))
            .And(Literals.Text("duration=").SkipAnd(Number).AndSkip(_ws).Optional())
            .And(Literals.Text("easing=").SkipAnd(Identifier).AndSkip(_ws).Optional())
            .Then(t => (DslStatement)new AnimateStmt
            {
                Target = t.Item1,
                Property = t.Item2,
                TargetValue = t.Item3,
                Duration = t.Item4.HasValue ? t.Item4.Value : null,
                Easing = t.Item5.HasValue ? t.Item5.Value : null
            });

    /// <summary>menu "title"</summary>
    private static readonly Parser<DslStatement> _menu =
        Literals.Text("menu").AndSkip(_ws)
            .SkipAnd(QuotedString)
            .Then(prompt => (DslStatement)new MenuStmt { Prompt = prompt });

    /// <summary>"选项文本" -> target_label</summary>
    private static readonly Parser<DslStatement> _menuOption =
        QuotedString.AndSkip(_ws)
            .AndSkip(Literals.Text("->").AndSkip(_ws))
            .And(Identifier)
            .Then(t => (DslStatement)new MenuOptionStmt { Text = t.Item1, TargetLabel = t.Item2 });

    /// <summary>shake [intensity=N] [duration=N]</summary>
    private static readonly Parser<DslStatement> _shake =
        Literals.Text("shake").AndSkip(_ws)
            .And(Literals.Text("intensity=").SkipAnd(Number).AndSkip(_ws).Optional())
            .And(Literals.Text("duration=").SkipAnd(Number).AndSkip(_ws).Optional())
            .Then(t => (DslStatement)new ShakeStmt
            {
                Intensity = t.Item2.HasValue ? t.Item2.Value : null,
                Duration = t.Item3.HasValue ? t.Item3.Value : null
            });

    /// <summary>skip</summary>
    private static readonly Parser<DslStatement> _skip =
        Literals.Text("skip").Then((DslStatement)new ToggleSkipStmt());

    /// <summary>auto</summary>
    private static readonly Parser<DslStatement> _auto =
        Literals.Text("auto").Then((DslStatement)new ToggleAutoStmt());

    /// <summary>gallery unlock "id" "imagePath" [title="..."] [scene="..."]</summary>
    private static readonly Parser<DslStatement> _galleryUnlock =
        Literals.Text("gallery").AndSkip(_ws)
            .AndSkip(Literals.Text("unlock").AndSkip(_ws))
            .And(QuotedString.AndSkip(_ws))
            .And(QuotedString.AndSkip(_ws))
            .And(Literals.Text("title=").SkipAnd(QuotedString).AndSkip(_ws).Optional())
            .And(Literals.Text("scene=").SkipAnd(QuotedString).AndSkip(_ws).Optional())
            .Then(t => (DslStatement)new GalleryUnlockStmt
            {
                Id = t.Item2,
                ImagePath = t.Item3,
                Title = t.Item4.HasValue ? t.Item4.Value : null,
                SceneName = t.Item5.HasValue ? t.Item5.Value : null
            });

    /// <summary>gallery_unlock "id" [title="..."] — DSL 2.0 短语法（无路径）</summary>
    private static readonly Parser<DslStatement> _galleryUnlockShort =
        Literals.Text("gallery_unlock").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(Literals.Text("title=").SkipAnd(QuotedString).AndSkip(_ws).Optional())
            .And(Literals.Text("scene=").SkipAnd(QuotedString).AndSkip(_ws).Optional())
            .Then(t => (DslStatement)new GalleryUnlockStmt
            {
                Id = t.Item1,
                ImagePath = "",
                Title = t.Item2.HasValue ? t.Item2.Value : null,
                SceneName = t.Item3.HasValue ? t.Item3.Value : null
            });

    /// <summary>debug "message" [level=Info]</summary>
    private static readonly Parser<DslStatement> _debugLog =
        Literals.Text("debug").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(Literals.Text("level=").SkipAnd(Identifier).Optional())
            .Then(t => (DslStatement)new DebugLogStmt
            {
                Message = t.Item1,
                Level = t.Item2.HasValue ? t.Item2.Value : null
            });

    /// <summary>nvl / nvl clear / nvl exit / nvl auto</summary>
    private static readonly Parser<DslStatement> _nvl =
        Literals.Text("nvl")
            .And(
                _ws.SkipAnd(Literals.Text("clear"))
                .Or(_ws.SkipAnd(Literals.Text("exit")))
                .Or(_ws.SkipAnd(Literals.Text("auto")))
                .Optional())
            .Then(t => (DslStatement)new NvlStmt
            {
                IsClear = t.Item2.HasValue && t.Item2.Value == "clear",
                IsExit = t.Item2.HasValue && t.Item2.Value == "exit",
                IsAuto = t.Item2.HasValue && t.Item2.Value == "auto",
            });

    /// <summary>input "prompt" store "key" [options=[...]]</summary>
    private static readonly Parser<DslStatement> _input =
        Literals.Text("input").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .AndSkip(Literals.Text("store").AndSkip(_ws))
            .And(QuotedString.AndSkip(_ws))
            .And(
                Literals.Text("options=").SkipAnd(Literals.Char('['))
                    .SkipAnd(AnyCharBefore(Literals.Char(']'), canBeEmpty: true).Then(s => s.ToString()))
                    .AndSkip(Literals.Char(']'))
                    .Optional())
            .Then(t => (DslStatement)new InputStmt
            {
                Prompt = t.Item1,
                StoreKey = t.Item2,
                Options = t.Item3.HasValue
                    ? t.Item3.Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(o => o.Trim().Trim('"')).ToArray()
                    : null
            });

    // ====== 块结构（缩进式，无花括号）======

    /// <summary>if {cond}</summary>
    private static readonly Parser<DslStatement> _if =
        Literals.Text("if").AndSkip(_ws)
            .SkipAnd(Expression)
            .Then(cond => (DslStatement)new IfStmt { Condition = cond });

    /// <summary>else if {cond}</summary>
    private static readonly Parser<DslStatement> _elseIf =
        Literals.Text("else").AndSkip(_ws)
            .AndSkip(Literals.Text("if").AndSkip(_ws))
            .SkipAnd(Expression)
            .Then(cond => (DslStatement)new ElseIfStmt { Condition = cond });

    /// <summary>else</summary>
    private static readonly Parser<DslStatement> _else =
        Literals.Text("else").Then((DslStatement)new ElseStmt());

    /// <summary>while {cond}</summary>
    private static readonly Parser<DslStatement> _while =
        Literals.Text("while").AndSkip(_ws)
            .SkipAnd(Expression)
            .Then(cond => (DslStatement)new WhileStmt { Condition = cond });

    // ====== Phase 38: 时间事件与通知 ======

    /// <summary>
    /// time_event day=1 hour=12 minute=30 target="scene" once=true condition="{expr}" desc="描述"
    /// </summary>
    private static readonly Parser<DslStatement> _timeEvent =
        Literals.Text("time_event").AndSkip(_ws)
            .SkipAnd(ZeroOrMany(_propPair))
            .Then(props => ParseTimeEvent(props));

    /// <summary>time_pause</summary>
    private static readonly Parser<DslStatement> _timePause =
        Literals.Text("time_pause").Then((DslStatement)new TimePauseStmt());

    /// <summary>time_resume</summary>
    private static readonly Parser<DslStatement> _timeResume =
        Literals.Text("time_resume").Then((DslStatement)new TimeResumeStmt());

    /// <summary>skip_time N</summary>
    private static readonly Parser<DslStatement> _skipTime =
        Literals.Text("skip_time").AndSkip(_ws)
            .SkipAnd(Number.AndSkip(_ws))
            .Then(minutes => (DslStatement)new SkipTimeStmt { Minutes = (int)minutes });

    /// <summary>
    /// set_time_event "id" HOUR [minute=N] [day=N] [once=true|false] [weekdays="Mon,Tue"] [condition="{expr}"] [desc="描述"]
    /// </summary>
    private static readonly Parser<DslStatement> _setTimeEvent =
        Literals.Text("set_time_event").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(Number.AndSkip(_ws))
            .And(ZeroOrMany(_propPair))
            .Then(t => ParseSetTimeEvent(t.Item1, (int)t.Item2, t.Item3));

    /// <summary>unregister_time_event "id" [permanent|temporary]</summary>
    private static readonly Parser<DslStatement> _unregisterTimeEvent =
        Literals.Text("unregister_time_event").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(
                Literals.Text("permanent").AndSkip(_ws)
                .Or(Literals.Text("temporary").AndSkip(_ws))
                .Optional())
            .Then(t => (DslStatement)new UnregisterTimeEventStmt
            {
                Id = t.Item1,
                Mode = t.Item2.HasValue
                    ? (t.Item2.Value == "permanent" ? UnregisterMode.Permanent : UnregisterMode.Temporary)
                    : UnregisterMode.Normal
            });

    /// <summary>restore_time_event "id"</summary>
    private static readonly Parser<DslStatement> _restoreTimeEvent =
        Literals.Text("restore_time_event").AndSkip(_ws)
            .SkipAnd(QuotedString)
            .Then(id => (DslStatement)new RestoreTimeEventStmt { Id = id });

    /// <summary>notify "text" [type=warning] [duration=5.0]</summary>
    private static readonly Parser<DslStatement> _notify =
        Literals.Text("notify").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(Literals.Text("type=").SkipAnd(Identifier).AndSkip(_ws).Optional())
            .And(Literals.Text("duration=").SkipAnd(Number).AndSkip(_ws).Optional())
            .Then(t => (DslStatement)new NotifyStmt
            {
                Text = t.Item1,
                Type = t.Item2.HasValue ? t.Item2.Value : null,
                Duration = t.Item3.HasValue ? t.Item3.Value : null
            });

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
    private static readonly Parser<DslStatement> _window =
        Literals.Text("window").AndSkip(_ws)
            .SkipAnd(
                Literals.Text("auto").Then("auto")
                .Or(Literals.Text("show").Then("show"))
                .Or(Literals.Text("hide").Then("hide")))
            .Then(mode => (DslStatement)new WindowStmt { Mode = mode });

    /// <summary>block_rollback</summary>
    private static readonly Parser<DslStatement> _blockRollback =
        Literals.Text("block_rollback").Then((DslStatement)new BlockRollbackStmt());

    /// <summary>fix_rollback</summary>
    private static readonly Parser<DslStatement> _fixRollback =
        Literals.Text("fix_rollback").Then((DslStatement)new FixRollbackStmt());

    /// <summary>break — 退出当前循环</summary>
    private static readonly Parser<DslStatement> _break =
        Literals.Text("break").Then((DslStatement)new BreakStmt());

    /// <summary>continue — 跳过当前迭代</summary>
    private static readonly Parser<DslStatement> _continue =
        Literals.Text("continue").Then((DslStatement)new ContinueStmt());

    /// <summary>for "var" in {expr}</summary>
    private static readonly Parser<DslStatement> _for =
        Literals.Text("for").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .AndSkip(Literals.Text("in").AndSkip(_ws))
            .And(Expression)
            .Then(t => (DslStatement)new ForStmt { VarName = t.Item1, SourceExpr = t.Item2 });

    // ====== Phase 44: 叙事增强 ======

    /// <summary>switch {expr}</summary>
    private static readonly Parser<DslStatement> _switch =
        Literals.Text("switch").AndSkip(_ws)
            .SkipAnd(Expression)
            .Then(expr => (DslStatement)new SwitchStmt { Expression = expr });

    /// <summary>case N</summary>
    private static readonly Parser<DslStatement> _case =
        Literals.Text("case").AndSkip(_ws)
            .SkipAnd(RestToEol)
            .Then(val => (DslStatement)new CaseStmt { Value = val.Trim() });

    /// <summary>default</summary>
    private static readonly Parser<DslStatement> _default =
        Literals.Text("default").Then((DslStatement)new DefaultStmt());

    /// <summary>func name(param1, param2)</summary>
    private static readonly Parser<DslStatement> _func =
        Literals.Text("func").AndSkip(_ws)
            .SkipAnd(Identifier.AndSkip(_ws))
            .AndSkip(Literals.Char('('))
            .And(AnyCharBefore(Literals.Char(')'), canBeEmpty: true).Then(s => s.ToString()))
            .AndSkip(Literals.Char(')'))
            .Then(t => (DslStatement)new FuncStmt
            {
                Name = t.Item1,
                Parameters = t.Item2.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(p => p.Trim()).ToList()
            });

    /// <summary>array "key" [item1, item2, ...] [once]</summary>
    private static readonly Parser<DslStatement> _array =
        Literals.Text("array").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .AndSkip(Literals.Char('['))
            .And(AnyCharBefore(Literals.Char(']'), canBeEmpty: true).Then(s => s.ToString()))
            .AndSkip(Literals.Char(']').AndSkip(_ws))
            .And(Literals.Text("once").AndSkip(_ws).Optional())
            .Then(t => (DslStatement)new ArrayStmt
            {
                Key = t.Item1,
                Items = t.Item2.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => s.Trim().Trim('"')).ToList(),
                IsDefine = t.Item3.HasValue
            });

    /// <summary>array_push "key" "value"</summary>
    private static readonly Parser<DslStatement> _arrayPush =
        Literals.Text("array_push").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(RestToEol)
            .Then(t => (DslStatement)new ArrayPushStmt { Key = t.Item1, ValuePart = t.Item2.Trim() });

    /// <summary>array_pop "key"</summary>
    private static readonly Parser<DslStatement> _arrayPop =
        Literals.Text("array_pop").AndSkip(_ws)
            .SkipAnd(QuotedString)
            .Then(key => (DslStatement)new ArrayPopStmt { Key = key });

    /// <summary>foreach "var" in "key"</summary>
    private static readonly Parser<DslStatement> _foreach =
        Literals.Text("foreach").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .AndSkip(Literals.Text("in").AndSkip(_ws))
            .And(QuotedString)
            .Then(t => (DslStatement)new ForeachStmt { VarName = t.Item1, SourceKey = t.Item2 });

    /// <summary>dict "key" {"field":value,...} [once]</summary>
    private static readonly Parser<DslStatement> _dict =
        Literals.Text("dict").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .AndSkip(Literals.Char('{'))
            .And(AnyCharBefore(Literals.Char('}'), canBeEmpty: true).Then(s => s.ToString()))
            .AndSkip(Literals.Char('}').AndSkip(_ws))
            .And(Literals.Text("once").AndSkip(_ws).Optional())
            .Then(t => ParseDictStmt(t.Item1, t.Item2, t.Item3.HasValue));

    /// <summary>dict_set "key" "field" value</summary>
    private static readonly Parser<DslStatement> _dictSet =
        Literals.Text("dict_set").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(QuotedString.AndSkip(_ws))
            .And(RestToEol)
            .Then(t => (DslStatement)new DictSetStmt { Key = t.Item1, Field = t.Item2, ValuePart = t.Item3.Trim() });

    // ====== Phase 45: UI 增强 ======

    /// <summary>popup "name" [width=N] [height=N] [mask=true|false]</summary>
    private static readonly Parser<DslStatement> _popup =
        Literals.Text("popup").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(ZeroOrMany(_propPair))
            .Then(t => ParsePopup(t.Item1, t.Item2));

    /// <summary>zindex N</summary>
    private static readonly Parser<DslStatement> _zindex =
        Literals.Text("zindex").AndSkip(_ws)
            .SkipAnd(Number)
            .Then(z => (DslStatement)new ZindexStmt { ZIndex = (int)z });

    /// <summary>sprite "id" src="path" [x=N] [y=N] [fade=N]</summary>
    private static readonly Parser<DslStatement> _sprite =
        Literals.Text("sprite").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(ZeroOrMany(_propPair))
            .Then(t => ParseSprite(t.Item1, t.Item2));

    /// <summary>sprite_state "id" emotion="smile"</summary>
    private static readonly Parser<DslStatement> _spriteState =
        Literals.Text("sprite_state").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(ZeroOrMany(_propPair))
            .Then(t => (DslStatement)new SpriteStateStmt
            {
                Id = t.Item1,
                Emotion = t.Item2.FirstOrDefault(p => p.key == "emotion").value ?? ""
            });

    /// <summary>sprite_move "id" [x=N] [y=N] [duration=N]</summary>
    private static readonly Parser<DslStatement> _spriteMove =
        Literals.Text("sprite_move").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(ZeroOrMany(_propPair))
            .Then(t => ParseSpriteMove(t.Item1, t.Item2));

    /// <summary>sprite_hide "id" [fade=N]</summary>
    private static readonly Parser<DslStatement> _spriteHide =
        Literals.Text("sprite_hide").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(ZeroOrMany(_propPair))
            .Then(t => (DslStatement)new SpriteHideStmt
            {
                Id = t.Item1,
                Fade = GetPropDouble(t.Item2, "fade")
            });

    /// <summary>bg_switch "path" [transition=fade] [duration=N]</summary>
    private static readonly Parser<DslStatement> _bgSwitch =
        Literals.Text("bg_switch").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(ZeroOrMany(_propPair))
            .Then(t => (DslStatement)new BgSwitchStmt
            {
                Path = t.Item1,
                Transition = t.Item2.FirstOrDefault(p => p.key == "transition").value,
                Duration = GetPropDouble(t.Item2, "duration")
            });

    /// <summary>text_typewriter speed=N</summary>
    private static readonly Parser<DslStatement> _textTypewriter =
        Literals.Text("text_typewriter").AndSkip(_ws)
            .AndSkip(Literals.Text("speed="))
            .SkipAnd(Number)
            .Then(speed => (DslStatement)new TextTypewriterStmt { Speed = speed });

    // ====== Phase 46: Live2D ======

    /// <summary>live2d_char "id" src="path" [height=N] [x=N] [y=N] [fade=N] ...</summary>
    private static readonly Parser<DslStatement> _live2dChar =
        Literals.Text("live2d_char").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(ZeroOrMany(_propPair))
            .Then(t => ParseLive2DChar(t.Item1, t.Item2));

    /// <summary>live2d_show "id"</summary>
    private static readonly Parser<DslStatement> _live2dShow =
        Literals.Text("live2d_show").AndSkip(_ws)
            .SkipAnd(QuotedString)
            .Then(id => (DslStatement)new Live2DShowStmt { Id = id });

    /// <summary>live2d_motion "id" name="motion" [fade=N] [loop=true|false]</summary>
    private static readonly Parser<DslStatement> _live2dMotion =
        Literals.Text("live2d_motion").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(ZeroOrMany(_propPair))
            .Then(t => (DslStatement)new Live2DMotionStmt
            {
                Id = t.Item1,
                Name = t.Item2.FirstOrDefault(p => p.key == "name").value ?? "",
                Fade = GetPropDouble(t.Item2, "fade"),
                Loop = GetPropBool(t.Item2, "loop", true)
            });

    /// <summary>live2d_expr "id" name="expression" [fade=N]</summary>
    private static readonly Parser<DslStatement> _live2dExpr =
        Literals.Text("live2d_expr").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(ZeroOrMany(_propPair))
            .Then(t => (DslStatement)new Live2DExprStmt
            {
                Id = t.Item1,
                Name = t.Item2.FirstOrDefault(p => p.key == "name").value ?? "",
                Fade = GetPropDouble(t.Item2, "fade")
            });

    /// <summary>live2d_param "id" param="BodyAngleX" value=-8 [weight=0.6]</summary>
    private static readonly Parser<DslStatement> _live2dParam =
        Literals.Text("live2d_param").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(ZeroOrMany(_propPair))
            .Then(t => (DslStatement)new Live2DParamStmt
            {
                Id = t.Item1,
                ParamName = t.Item2.FirstOrDefault(p => p.key == "param").value ?? "",
                Value = GetPropDouble(t.Item2, "value") ?? 0,
                Weight = GetPropDouble(t.Item2, "weight") ?? 1.0
            });

    /// <summary>live2d_hide "id" [fade=N]</summary>
    private static readonly Parser<DslStatement> _live2dHide =
        Literals.Text("live2d_hide").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .And(ZeroOrMany(_propPair))
            .Then(t => (DslStatement)new Live2DHideStmt
            {
                Id = t.Item1,
                Fade = GetPropDouble(t.Item2, "fade")
            });

    /// <summary>live2d_pause "id"</summary>
    private static readonly Parser<DslStatement> _live2dPause =
        Literals.Text("live2d_pause").AndSkip(_ws)
            .SkipAnd(QuotedString)
            .Then(id => (DslStatement)new Live2DPauseStmt { Id = id });

    /// <summary>live2d_resume "id"</summary>
    private static readonly Parser<DslStatement> _live2dResume =
        Literals.Text("live2d_resume").AndSkip(_ws)
            .SkipAnd(QuotedString)
            .Then(id => (DslStatement)new Live2DResumeStmt { Id = id });

    // ====== Phase 47: 存档/成就/章节 ======

    /// <summary>auto_save true|false</summary>
    private static readonly Parser<DslStatement> _autoSave =
        Literals.Text("auto_save").AndSkip(_ws)
            .SkipAnd(Literals.Text("true").Or(Literals.Text("false")))
            .Then(enabled => (DslStatement)new AutoSaveStmt { Enabled = enabled == "true" });

    /// <summary>save_delete "slot"</summary>
    private static readonly Parser<DslStatement> _saveDelete =
        Literals.Text("save_delete").AndSkip(_ws)
            .SkipAnd(QuotedString)
            .Then(slot => (DslStatement)new SaveDeleteStmt { SlotId = slot });

    /// <summary>chapter "id" name "章节名" [unlock=true|false]</summary>
    private static readonly Parser<DslStatement> _chapter =
        Literals.Text("chapter").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .AndSkip(Literals.Text("name").AndSkip(_ws))
            .And(QuotedString.AndSkip(_ws))
            .And(Literals.Text("unlock=").SkipAnd(Literals.Text("true").Or(Literals.Text("false"))).AndSkip(_ws).Optional())
            .Then(t => (DslStatement)new ChapterStmt
            {
                Id = t.Item1,
                ChapterName = t.Item2,
                Unlock = !t.Item3.HasValue || t.Item3.Value == "true"
            });

    /// <summary>achievement "id" name "成就名"</summary>
    private static readonly Parser<DslStatement> _achievement =
        Literals.Text("achievement").AndSkip(_ws)
            .SkipAnd(QuotedString.AndSkip(_ws))
            .AndSkip(Literals.Text("name").AndSkip(_ws))
            .And(QuotedString)
            .Then(t => (DslStatement)new AchievementStmt { Id = t.Item1, AchievementName = t.Item2 });

    /// <summary>auto_speed N</summary>
    private static readonly Parser<DslStatement> _autoSpeed =
        Literals.Text("auto_speed").AndSkip(_ws)
            .SkipAnd(Number)
            .Then(speed => (DslStatement)new AutoSpeedStmt { Speed = speed });

    /// <summary>no_skip</summary>
    private static readonly Parser<DslStatement> _noSkip =
        Literals.Text("no_skip").Then((DslStatement)new NoSkipStmt());

    /// <summary>force_skip</summary>
    private static readonly Parser<DslStatement> _forceSkip =
        Literals.Text("force_skip").Then((DslStatement)new ForceSkipStmt());

    // ====== Phase 48: 视频增强 ======

    /// <summary>video_skipable true|false</summary>
    private static readonly Parser<DslStatement> _videoSkipable =
        Literals.Text("video_skipable").AndSkip(_ws)
            .SkipAnd(Literals.Text("true").Or(Literals.Text("false")))
            .Then(enabled => (DslStatement)new VideoSkipableStmt { Enabled = enabled == "true" });

    /// <summary>video_auto_nav "scene"</summary>
    private static readonly Parser<DslStatement> _videoAutoNav =
        Literals.Text("video_auto_nav").AndSkip(_ws)
            .SkipAnd(QuotedString)
            .Then(scene => (DslStatement)new VideoAutoNavStmt { SceneName = scene });

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
    private static readonly Parser<DslStatement>[] _statementParsers =
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
    /// 完整语句解析器（语句 + 尾部空白 + 行尾）——静态一次构建，
    /// 避免原 Pidgin 版在循环内每次新建 parser 包装的分配。
    /// </summary>
    private static readonly Parser<DslStatement>[] _fullStatementParsers =
        _statementParsers.Select(p => p.AndSkip(_ws).Eof()).ToArray();

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

        foreach (var parser in _fullStatementParsers)
        {
            if (parser.TryParse(line, out var stmt) && stmt is not null)
            {
                stmt.LineNumber = lineNumber;
                return stmt;
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

        foreach (var parser in _fullStatementParsers)
        {
            if (parser.TryParse(line, out var parsed) && parsed is not null)
            {
                parsed.LineNumber = lineNumber;
                stmt = parsed;
                error = null;
                return true;
            }
        }

        stmt = null;
        error = $"行 {lineNumber + 1}: 无法解析 DSL 语句 '{line}'";
        return false;
    }
}
