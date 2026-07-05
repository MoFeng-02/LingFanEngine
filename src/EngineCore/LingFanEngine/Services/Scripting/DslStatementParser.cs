using System.Globalization;
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

    /// <summary>标识符 \w+</summary>
    private static readonly Parser<char, string> Identifier =
        Token(char.IsLetterOrDigit).AtLeastOnceString()
            .Labelled("identifier");

    /// <summary>数字（整数或浮点数）</summary>
    private static readonly Parser<char, double> Number =
        (from intPart in Digit.AtLeastOnceString()
         from fracPart in Try(Char('.').Then(Digit.AtLeastOnceString())).Optional()
         select double.Parse(intPart + fracPart.GetValueOrDefault(""), CultureInfo.InvariantCulture))
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

    /// <summary>say "text" [by "speaker"]</summary>
    private static readonly Parser<char, DslStatement> _say =
        from _1 in String("say").Before(_ws)
        from text in QuotedString.Before(_ws)
        from speaker in Try(String("by").Before(_ws).Then(QuotedString)).Optional()
        select (DslStatement)new SayStmt
        {
            Text = text,
            Speaker = speaker.HasValue ? speaker.Value : null
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

    /// <summary>set "key" value</summary>
    private static readonly Parser<char, DslStatement> _set =
        from _1 in String("set").Before(_ws)
        from key in QuotedString.Before(_ws)
        from value in Tok(AnyCharExcept('\n', '\r')).ManyString()
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
        from volume in Try(String("volume=").Then(Number)).Optional()
        select (DslStatement)new BgmStmt
        {
            Path = path,
            Volume = volume.HasValue ? (float)volume.Value : null
        };

    /// <summary>wait N</summary>
    private static readonly Parser<char, DslStatement> _wait =
        from _1 in String("wait").Before(_ws)
        from seconds in Number
        select (DslStatement)new WaitStmt { Seconds = seconds };

    /// <summary>transition "type" [duration=N] [easing=xxx]</summary>
    private static readonly Parser<char, DslStatement> _transition =
        from _1 in String("transition").Before(_ws)
        from type in QuotedString.Before(_ws)
        from duration in Try(String("duration=").Then(Number)).Optional()
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

    /// <summary>show "target" [at (x, y)]</summary>
    private static readonly Parser<char, DslStatement> _show =
        from _1 in String("show").Before(_ws)
        from target in QuotedString.Before(_ws)
        from pos in Try(_position).Optional()
        select (DslStatement)new ShowStmt
        {
            Target = target,
            X = pos.HasValue ? pos.Value.x : null,
            Y = pos.HasValue ? pos.Value.y : null
        };

    /// <summary>hide "target"</summary>
    private static readonly Parser<char, DslStatement> _hide =
        from _1 in String("hide").Before(_ws)
        from target in QuotedString
        select (DslStatement)new HideStmt { Target = target };

    /// <summary>animate "target" property value [duration=N] [easing=xxx]</summary>
    private static readonly Parser<char, DslStatement> _animate =
        from _1 in String("animate").Before(_ws)
        from target in QuotedString.Before(_ws)
        from prop in Identifier.Before(_ws)
        from val in Number.Before(_ws)
        from duration in Try(String("duration=").Then(Number)).Optional()
        from easing in Try(String("easing=").Then(Identifier)).Optional()
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

    // ====== 块结构 ======

    /// <summary>if {cond} {</summary>
    private static readonly Parser<char, DslStatement> _if =
        from _1 in String("if").Before(_ws)
        from cond in Expression.Before(_ws)
        from _2 in Char('{')
        select (DslStatement)new IfStmt { Condition = cond };

    /// <summary>} else if {cond} {</summary>
    private static readonly Parser<char, DslStatement> _elseIf =
        from _1 in Char('}').Before(_ws)
        from _2 in String("else").Before(_ws)
        from _3 in String("if").Before(_ws)
        from cond in Expression.Before(_ws)
        from _4 in Char('{')
        select (DslStatement)new ElseIfStmt { Condition = cond };

    /// <summary>} else {</summary>
    private static readonly Parser<char, DslStatement> _else =
        from _1 in Char('}').Before(_ws)
        from _2 in String("else").Before(_ws)
        from _3 in Char('{')
        select (DslStatement)new ElseStmt();

    /// <summary>}</summary>
    private static readonly Parser<char, DslStatement> _endBlock =
        Char('}').ThenReturn((DslStatement)new EndBlockStmt());

    /// <summary>while {cond} {</summary>
    private static readonly Parser<char, DslStatement> _while =
        from _1 in String("while").Before(_ws)
        from cond in Expression.Before(_ws)
        from _2 in Char('{')
        select (DslStatement)new WhileStmt { Condition = cond };

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
        // 块结构（以 } 开头或包含 { 结尾）
        _elseIf,
        _else,
        _if,
        _while,
        _endBlock,
        // 多字符关键字（长的在前）
        _navigate,
        _transition,
        _background,
        _forward,
        _return,
        _animate,
        // 单关键字
        _define,
        _scene,
        _input,
        _menu,
        _label,
        _jump,
        _call,
        _say,
        _set,
        _let,
        _bgm,
        _wait,
        _show,
        _hide,
        _back,
        _end,
        _saveLoad,
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
