using System;
using Pidgin;
using LingFanEngine.Services.Scripting.Pidgin;

// 测试 TextElement 解析
string[] testLines = new[]
{
    @"text ""灵泛引擎"" at (50%, 15%) size=56 color=""#FFD700"" align=center font=""Microsoft YaHei""",
    @"text ""你好世界"" at (100, 200) size=24",
    @"text ""左边的小路"" at (100, 300) size=18 color=""#FFFFFF""",
};

foreach (var line in testLines)
{
    var r = SceneElementParser.ParseLine(line);
    Console.WriteLine($"Input: {line}");
    Console.WriteLine($"  ParseLine result: {(r != null ? r.ElementType : "null")}");
    if (r != null)
    {
        foreach (var kv in r.Properties)
            Console.WriteLine($"    {kv.Key} = {kv.Value} ({kv.Value?.GetType().Name})");
    }
    Console.WriteLine();
}

// 直接 Pidgin 解析 text 行
var textParser = TextElementParser();
var textLine = @"text ""灵泛引擎"" at (50%, 15%) size=56 color=""#FFD700"" align=center font=""Microsoft YaHei""";
var result = textParser.Parse(textLine);
Console.WriteLine($"Direct Pidgin parse: Success={result.Success}, Value={result.Value?.ElementType}");

static Parser<char, UIElementEntity> TextElementParser()
{
    return from _ in Parser.String("text")
           from __ in Parser.Whitespace.AtLeastOnceString()
           from text in QuotedString()
           from ___ in Parser.Whitespace.AtLeastOnceString()
           from ____ in Parser.String("at")
           from _____ in Parser.Whitespace.SkipMany()
           from leftP in Parser.Char('(')
           from x in CoordValue()
           from comma in Parser.Char(',')
           from y in CoordValue()
           from rightP in Parser.Char(')')
           select new UIElementEntity { ElementType = "text", Properties = new() { ["text"] = text, ["x"] = x, ["y"] = y } };
}

static Parser<char, string> QuotedString()
{
    return from open in Parser.Char('"')
           from content in Parser<char>.Token(c => c != '"').ManyString()
           from close in Parser.Char('"')
           select content;
}

static Parser<char, string> CoordValue()
{
    return from digits in Parser.Digit.AtLeastOnceString()
           from pct in Parser.Try(from c in Parser.Char('%') select "%").Optional()
           select digits + (pct.HasValue ? pct.Value : "");
}

class UIElementEntity
{
    public string ElementType { get; set; } = "";
    public Dictionary<string, object> Properties { get; set; } = new();
}
