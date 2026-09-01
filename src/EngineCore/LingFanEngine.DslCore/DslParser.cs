using System.Globalization;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Entities.UIs;
using Parlot;
using Parlot.Fluent;
using static Parlot.Fluent.Parsers;

namespace LingFanEngine.DslCore;

/// <summary>
/// 基于 Parlot 的 DSL 场景元素解析器
/// <para>替代正则表达式，支持：属性任意顺序、类型安全、可组合扩展。</para>
/// <para>语法：elementType "content" key=value key=value ...</para>
/// <para>2026-09：由 Pidgin（本地 fork DLL）迁移至 Parlot 1.5.8（NuGet），语义同构——
/// 转义序列（未知转义保留两字符原样）、颜色长度校验、End 语义全数保留。</para>
/// </summary>
public static class DslParser
{
    // ========== 基础组合子 ==========

    /// <summary>
    /// 跳过空白（贪婪，零或多——与 Pidgin SkipWhitespaces 语义严格对齐）。
    /// <para>注意：不能直接用 Literals.WhiteSpace()——它零匹配时返回失败，
    /// 会导致行尾 AndSkip(Ws) 在 EOF 必然失败（v1.5.8 WhiteSpaceLiteral 源码实证）。
    /// ZeroOrOne 包裹后：内层贪婪吃光连续空白、零个也成功。</para>
    /// </summary>
    private static readonly Parser<TextSpan> Ws =
        ZeroOrOne(Literals.WhiteSpace(includeNewLines: true));

    /// <summary>带前导空白的 token</summary>
    private static Parser<T> Spaced<T>(Parser<T> p) => Ws.SkipAnd(p);

    // ========== 值解析器 ==========

    /// <summary>转义序列：\" \\ \n \t \r；未知转义保留两字符原样（如 Windows 路径 "C:\dir"），与 DslStatementParser/ExpressionParser 语义一致</summary>
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

    /// <summary>引号字符串："..."（支持 \" 与 \\ 转义；未知转义保留两字符原样，存量零破坏）</summary>
    private static readonly Parser<string> QuotedString =
        Literals.Char('"')
            .SkipAnd(ZeroOrMany(StringSegment).Then(ss => string.Concat(ss)))
            .AndSkip(Literals.Char('"'));

    /// <summary>标识符：字母开头，后跟字母/数字/下划线/连字符</summary>
    private static readonly Parser<string> Identifier =
        Capture(
            Literals.Pattern(c => char.IsLetter(c) || c == '_', 1, 1)
                .AndSkip(ZeroOrMany(Literals.Pattern(c => char.IsLetterOrDigit(c) || c == '_' || c == '-')))
        ).Then(s => s.ToString());

    /// <summary>数字（整数部分必须、小数部分可选——与 Pidgin Real 同构）</summary>
    private static readonly Parser<double> Number =
        Capture(
            Literals.Pattern(char.IsDigit)
                .AndSkip(ZeroOrOne(
                    Literals.Char('.').AndSkip(Literals.Pattern(char.IsDigit))
                ))
        ).Then(s => double.Parse(s.ToString(), CultureInfo.InvariantCulture));

    /// <summary>数字或百分比值：100, 50%, 12.5%</summary>
    private static readonly Parser<object> NumberOrPercent =
        Number
            .And(Literals.Char('%').Optional())
            .Then(t => t.Item2.HasValue
                ? (object)string.Create(CultureInfo.InvariantCulture, $"{t.Item1}%")
                : (object)t.Item1);

    /// <summary>布尔值：true / false</summary>
    private static readonly Parser<object> BoolValue =
        Literals.Text("true").Then(_ => (object)true)
            .Or(Literals.Text("false").Then(_ => (object)false));

    /// <summary>十六进制颜色：#RGB / #ARGB / #RRGGBB / #AARRGGBB（3/4/6/8 位）</summary>
    private static readonly Parser<object> ColorHex =
        Literals.Char('#')
            .SkipAnd(Literals.Pattern(Uri.IsHexDigit)
                .When((ctx, span) => span.Length is 3 or 4 or 6 or 8)
                .Then(hex => (object)("#" + hex.ToString())))
            .Named("hex color (3/4/6/8 digits)");

    /// <summary>
    /// 通用值：引号字符串 | 布尔 | 颜色 | 数字(%) | 标识符
    /// <para>分支顺序与 Pidgin 原实现一致；Parlot 组合子失败自动回溯游标，无需 Try 包裹。</para>
    /// </summary>
    private static readonly Parser<object> Value =
        QuotedString.Then(s => (object)s)
            .Or(BoolValue)
            .Or(ColorHex)
            .Or(NumberOrPercent)
            .Or(Identifier.Then(s => (object)s))
            .Named("value");

    // ========== 属性解析器 ==========

    /// <summary>属性条目：key + value</summary>
    private sealed record PropEntry(string Key, object Value);

    /// <summary>key = value（通用键值对，唯一属性形式）</summary>
    private static readonly Parser<PropEntry> Property =
        Ws.SkipAnd(Identifier)
            .AndSkip(Ws).AndSkip(Literals.Char('='))
            .And(Ws.SkipAnd(Value))
            .Then(t => new PropEntry(t.Item1, t.Item2));

    // ========== 元素解析器 ==========

    /// <summary>
    /// 完整元素解析器：type "content" key=value key=value ...
    /// </summary>
    private static readonly Parser<UIElementEntity> Element =
        Identifier
            .And(Ws.SkipAnd(QuotedString))
            .And(ZeroOrMany(Property))
            .AndSkip(Ws)
            .Eof()
            .Then(t => BuildEntity(t.Item1, t.Item2, t.Item3));

    /// <summary>
    /// 解析单行 DSL 元素，返回 UIElementEntity
    /// </summary>
    public static UIElementEntity? ParseElement(string line)
    {
        if (!Element.TryParse(line.Trim(), out var entity, out var error))
        {
            System.Diagnostics.Debug.WriteLine($"[DslParser] 解析失败: {line} → {error?.Message}");
            return null;
        }
        return entity;
    }

    // ========== 场景头解析器 ==========

    /// <summary>
    /// scene 行属性解析结果
    /// </summary>
    public sealed record SceneHeader(string SceneName, string LayoutMode, SceneType SceneType);

    /// <summary>属性键值对（scene 行上的 key=value）</summary>
    private sealed record SceneProp(string Key, string Value);

    /// <summary>非引号属性值：非空白字符序列</summary>
    private static readonly Parser<string> BareValue =
        Literals.Pattern(c => !char.IsWhiteSpace(c) && c != '"').Then(s => s.ToString());

    /// <summary>scene 行属性：key=value（值可为引号字符串或裸值）</summary>
    private static readonly Parser<SceneProp> SceneKeyValue =
        Ws.SkipAnd(Identifier)
            .AndSkip(Ws).AndSkip(Literals.Char('='))
            .And(Ws.SkipAnd(QuotedString.Or(BareValue)))
            .Then(t => new SceneProp(t.Item1, t.Item2));

    /// <summary>
    /// 完整 scene 行解析器：scene "name" [layout=canvas] [type=menu] ...
    /// </summary>
    private static readonly Parser<SceneHeader> SceneHeaderParser =
        Literals.Text("scene")
            .And(Ws.SkipAnd(QuotedString))
            .And(ZeroOrMany(SceneKeyValue))
            .AndSkip(Ws)
            .Eof()
            .Then(t => BuildSceneHeader(t.Item2, t.Item3));

    /// <summary>
    /// 解析 scene 行，返回场景名、布局模式、场景类型
    /// </summary>
    public static SceneHeader? ParseSceneHeader(string line)
    {
        if (!SceneHeaderParser.TryParse(line.Trim(), out var header, out var error))
        {
            System.Diagnostics.Debug.WriteLine($"[DslParser] scene 行解析失败: {line} → {error?.Message}");
            return null;
        }
        return header;
    }

    private static SceneHeader BuildSceneHeader(string name, IEnumerable<SceneProp> props)
    {
        var layout = "grid";
        var type = SceneType.Game;
        foreach (var (key, val) in props)
        {
            if (key == "layout")
                layout = val;
            else if (key == "type")
                type = val.ToLowerInvariant() switch
                {
                    "menu" => SceneType.Menu,
                    "ui" => SceneType.UI,
                    _ => SceneType.Game
                };
        }
        return new SceneHeader(name, layout, type);
    }

    // ========== define 行解析器 ==========

    /// <summary>
    /// define 行解析结果
    /// </summary>
    public sealed record DefineEntry(string Key, string RawValue);

    /// <summary>行尾文本：任意非换行字符（可为空——对应 Pidgin AnyCharExcept('\n','\r').ManyString()）</summary>
    private static readonly Parser<string> RestToEol =
        Parsers.AnyCharBefore(Literals.Char('\n').Or(Literals.Char('\r')), canBeEmpty: true)
            .Then(s => s.ToString());

    /// <summary>
    /// define "key" value once
    /// </summary>
    private static readonly Parser<DefineEntry> DefineLineParser =
        Literals.Text("define")
            .SkipAnd(Ws.SkipAnd(QuotedString))
            .And(RestToEol)
            .AndSkip(Ws)
            .Eof()
            .Then(t => StripOnceSuffix(t.Item1, t.Item2));

    /// <summary>
    /// 剥离 define 行尾的 " once" 后缀（不区分大小写）
    /// </summary>
    private static DefineEntry StripOnceSuffix(string key, string rest)
    {
        var trimmed = rest.Trim();
        const string suffix = " once";
        if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^suffix.Length].Trim();
        return new DefineEntry(key, trimmed);
    }

    /// <summary>
    /// 解析 define 行
    /// </summary>
    public static DefineEntry? ParseDefineLine(string line)
    {
        if (!DefineLineParser.TryParse(line.Trim(), out var entry, out _))
            return null;
        return entry;
    }

    // ========== label 行解析器 ==========

    /// <summary>
    /// label xxx: — 全局标签定义
    /// </summary>
    private static readonly Parser<string> LabelLineParser =
        Literals.Text("label")
            .SkipAnd(Ws.SkipAnd(Identifier))
            .AndSkip(Ws).AndSkip(Literals.Char(':'))
            .AndSkip(Ws)
            .Eof();

    /// <summary>
    /// 解析 label 行
    /// </summary>
    public static string? ParseLabelLine(string line)
    {
        if (!LabelLineParser.TryParse(line.Trim(), out var name, out _))
            return null;
        return name;
    }

    // ========== 实体构建 ==========

    /// <summary>
    /// 将解析结果构建为 UIElementEntity
    /// </summary>
    private static UIElementEntity BuildEntity(
        string type, string content,
        IEnumerable<PropEntry> props)
    {
        var propDict = new Dictionary<string, object>();

        // 内容属性：image/background → source，其他 → text
        if (type is "image" or "background" or "portrait")
            propDict["source"] = content;
        else
            propDict["text"] = content;

        // 收集所有 key=value 属性
        foreach (var (key, value) in props)
        {
            // DSL 2.0: style=name 作为 class=name 的别名
            if (key == "style")
                propDict["class"] = value;
            else
                propDict[key] = value;
        }

        // 安全网：align → halign（如果未显式设 halign）
        if (propDict.TryGetValue("align", out var alignVal) && !propDict.ContainsKey("halign"))
            propDict["halign"] = alignVal;

        // 向后兼容：text 元素的 size=N → fontSize=N（仅当未设 width/height 时）
        if (type is "text" or "dialog" or "narrator" or "speaker"
            && propDict.TryGetValue("size", out var sizeVal)
            && !propDict.ContainsKey("width") && !propDict.ContainsKey("height")
            && !propDict.ContainsKey("fontSize"))
        {
            propDict["fontSize"] = sizeVal;
        }

        var entity = new UIElementEntity
        {
            ElementType = type,
            Properties = propDict,
            Order = 0
        };

        // 按钮命令：cmd 优先，nav 兜底
        if (type is "button" or "choice")
        {
            if (propDict.TryGetValue("cmd", out var cmd) && cmd != null)
                entity.Command = cmd.ToString();
            else if (propDict.TryGetValue("nav", out var nav) && nav != null)
                entity.Command = nav.ToString();

            if (propDict.TryGetValue("value", out var val) && val != null)
                entity.CommandValue = val.ToString();
        }

        return entity;
    }
}
