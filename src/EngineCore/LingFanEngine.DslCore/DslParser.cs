using System.Globalization;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Entities.UIs;
using Pidgin;
using static Pidgin.Parser;
using static Pidgin.Parser<char>;

namespace LingFanEngine.DslCore;

/// <summary>
/// 基于 Pidgin 的 DSL 场景元素解析器
/// <para>替代正则表达式，支持：属性任意顺序、类型安全、可组合扩展。</para>
/// <para>语法：elementType "content" key=value key=value ...</para>
/// </summary>
public static class DslParser
{
    // ========== 基础组合子 ==========

    /// <summary>跳过空白（空格/制表符）</summary>
    private static readonly Parser<char, Unit> Ws = SkipWhitespaces;

    /// <summary>带前导空白的 token</summary>
    private static Parser<char, T> Spaced<T>(Parser<char, T> p) => Ws.Then(p);

    // ========== 值解析器 ==========

    /// <summary>引号字符串："..."（内容不含引号）</summary>
    private static readonly Parser<char, string> QuotedString =
        Char('"')
            .Then(AnyCharExcept('"').ManyString())
            .Before(Char('"'));

    /// <summary>标识符：字母开头，后跟字母/数字/下划线/连字符</summary>
    private static readonly Parser<char, string> Identifier =
        from first in Letter
        from rest in Token(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ManyString()
        select first + rest;

    /// <summary>数字或百分比值：100, 50%, 12.5%</summary>
    private static readonly Parser<char, object> NumberOrPercent =
        from n in Real
        from pct in Char('%').Optional()
        select pct.HasValue ? (object)string.Create(CultureInfo.InvariantCulture, $"{n}%") : (object)n;

    /// <summary>布尔值：true / false</summary>
    private static readonly Parser<char, object> BoolValue =
        String("true").ThenReturn((object)true)
            .Or(String("false").ThenReturn((object)false));

    /// <summary>十六进制颜色：#RRGGBB / #AARRGGBB</summary>
    private static readonly Parser<char, object> ColorHex =
        Char('#')
            .Then(Token(c => "0123456789ABCDEFabcdef".Contains(c)).AtLeastOnceString())
            .Select(hex => (object)("#" + hex));

    /// <summary>
    /// 通用值：引号字符串 | 布尔 | 颜色 | 数字(%) | 标识符
    /// </summary>
    private static readonly Parser<char, object> Value =
        QuotedString.Select(s => (object)s)
            .Or(Try(BoolValue))
            .Or(Try(ColorHex))
            .Or(Try(NumberOrPercent))
            .Or(Identifier.Select(s => (object)s))
            .Labelled("value");

    // ========== 属性解析器 ==========

    /// <summary>属性条目：key + value</summary>
    private sealed record PropEntry(string Key, object Value);

    /// <summary>key = value（通用键值对，唯一属性形式）</summary>
    private static readonly Parser<char, PropEntry> Property =
        from _ in Ws
        from key in Identifier
        from eq in Spaced(Char('='))
        from val in Spaced(Value)
        select new PropEntry(key, val);

    // ========== 元素解析器 ==========

    /// <summary>
    /// 完整元素解析器：type "content" key=value key=value ...
    /// </summary>
    private static readonly Parser<char, UIElementEntity> Element =
        from type in Identifier
        from content in Spaced(QuotedString)
        from props in Property.Many()
        from _ in Ws
        from end in End
        select BuildEntity(type, content, props);

    /// <summary>
    /// 解析单行 DSL 元素，返回 UIElementEntity
    /// </summary>
    public static UIElementEntity? ParseElement(string line)
    {
        var result = Element.Parse(line.Trim());
        if (!result.Success)
        {
            System.Diagnostics.Debug.WriteLine($"[DslParser] 解析失败: {line} → {result.Error}");
            return null;
        }
        return result.Value;
    }

    // ========== 场景头解析器 ==========

    /// <summary>
    /// scene 行属性解析结果
    /// </summary>
    public sealed record SceneHeader(string SceneName, string LayoutMode, SceneType SceneType);

    /// <summary>属性键值对（scene 行上的 key=value）</summary>
    private sealed record SceneProp(string Key, string Value);

    /// <summary>非引号属性值：非空白字符序列</summary>
    private static readonly Parser<char, string> BareValue =
        Token(c => !char.IsWhiteSpace(c) && c != '"').AtLeastOnceString();

    /// <summary>scene 行属性：key=value（值可为引号字符串或裸值）</summary>
    private static readonly Parser<char, SceneProp> SceneKeyValue =
        from _ in Ws
        from key in Identifier
        from eq in Spaced(Char('='))
        from val in Spaced(Try(QuotedString).Or(BareValue))
        select new SceneProp(key, val);

    /// <summary>
    /// 完整 scene 行解析器：scene "name" [layout=canvas] [type=menu] ...
    /// </summary>
    private static readonly Parser<char, SceneHeader> SceneHeaderParser =
        from _ in String("scene")
        from name in Spaced(QuotedString)
        from props in SceneKeyValue.Many()
        from tail in Ws
        from end in End
        select BuildSceneHeader(name, props);

    /// <summary>
    /// 解析 scene 行，返回场景名、布局模式、场景类型
    /// </summary>
    public static SceneHeader? ParseSceneHeader(string line)
    {
        var result = SceneHeaderParser.Parse(line.Trim());
        if (!result.Success)
        {
            System.Diagnostics.Debug.WriteLine($"[DslParser] scene 行解析失败: {line} → {result.Error}");
            return null;
        }
        return result.Value;
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

    /// <summary>
    /// define "key" value once
    /// </summary>
    private static readonly Parser<char, DefineEntry> DefineLineParser =
        from _ in String("define")
        from key in Spaced(QuotedString)
        from rest in Spaced(AnyCharExcept('\n', '\r').ManyString())
        from tail in Ws
        from end in End
        select StripOnceSuffix(key, rest);

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
        var result = DefineLineParser.Parse(line.Trim());
        if (!result.Success)
            return null;
        return result.Value;
    }

    // ========== label 行解析器 ==========

    /// <summary>
    /// label xxx: — 全局标签定义
    /// </summary>
    private static readonly Parser<char, string> LabelLineParser =
        from _ in String("label")
        from name in Spaced(Identifier)
        from colon in Spaced(Char(':'))
        from tail in Ws
        from end in End
        select name;

    /// <summary>
    /// 解析 label 行
    /// </summary>
    public static string? ParseLabelLine(string line)
    {
        var result = LabelLineParser.Parse(line.Trim());
        if (!result.Success)
            return null;
        return result.Value;
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
