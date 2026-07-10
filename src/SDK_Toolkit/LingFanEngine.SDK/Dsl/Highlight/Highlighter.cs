using LingFanEngine.DslCore;
using LingFanEngine.SDK.Dsl.Lexer;

namespace LingFanEngine.SDK.Dsl.Highlight;

/// <summary>
/// 基于 Lexer Token 流的高亮器
/// </summary>
public static class Highlighter
{
    private static readonly IReadOnlySet<string> s_keywords = DslKeywords.All;

    /// <summary>从源码生成高亮 token 列表</summary>
    public static List<HighlightToken> GetHighlights(string source)
    {
        var tokens = Lexer.Lexer.Tokenize(source);
        var highlights = new List<HighlightToken>();

        // 追踪文本中的偏移量
        var lines = source.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        var lineOffsets = new int[lines.Length + 1];
        var offset = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            lineOffsets[i] = offset;
            offset += lines[i].Length + 1; // +1 for newline
        }
        lineOffsets[lines.Length] = offset;

        foreach (var token in tokens)
        {
            if (token.Type == TokenType.Newline)
                continue;

            var start = lineOffsets[token.Line - 1] + (token.Column - 1);
            var length = token.Value.Length;

            var category = token.Type switch
            {
                TokenType.Keyword => HighlightCategory.Keyword,
                TokenType.String => HighlightCategory.String,
                TokenType.Comment => HighlightCategory.Comment,
                TokenType.Number => HighlightCategory.Number,
                TokenType.Symbol => HighlightCategory.Symbol,
                TokenType.Identifier => s_keywords.Contains(token.Value)
                    ? HighlightCategory.Keyword
                    : HighlightCategory.Plain,
                _ => HighlightCategory.Plain,
            };

            highlights.Add(new HighlightToken(start, length, category));
        }

        // 额外检测 {variable} 表达式
        AddVariableHighlights(source, highlights);

        return highlights;
    }

    /// <summary>检测 {expression} 中的变量并添加高亮</summary>
    private static void AddVariableHighlights(string source, List<HighlightToken> highlights)
    {
        var i = 0;
        while (i < source.Length)
        {
            if (source[i] == '{')
            {
                var end = source.IndexOf('}', i + 1);
                if (end > i)
                {
                    // 检查不是内联标记标签
                    var content = source.Substring(i + 1, end - i - 1).Trim();
                    if (content.Length > 0 && !IsInlineTag(content))
                    {
                        highlights.Add(new HighlightToken(i, end - i + 1, HighlightCategory.Variable));
                    }
                    i = end + 1;
                }
                else
                {
                    i++;
                }
            }
            else
            {
                i++;
            }
        }
    }

    /// <summary>检查是否为内联标记标签（b/i/color/font/size/w/fast/p）</summary>
    private static bool IsInlineTag(string content)
    {
        var tags = new[] { "b", "/b", "i", "/i", "w", "fast", "p" };
        if (tags.Contains(content))
            return true;

        // color=#xxx, font=xxx, size=N
        if (content.StartsWith("color=") || content.StartsWith("/color") ||
            content.StartsWith("font=") || content.StartsWith("/font") ||
            content.StartsWith("size=") || content.StartsWith("/size"))
            return true;

        return false;
    }
}
