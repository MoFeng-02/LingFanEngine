using System.Collections.Generic;
using LingFanEngine.DslCore;
using LingFanEngine.SDK.Dsl.Lexer;

namespace LingFanEngine.SDK.Dsl.Highlight;

/// <summary>
/// 轻量高亮器——基于手写 Lexer 的 Token 流进行语义着色。
/// <para>单遍 O(n) 扫描，无回溯，适合在 UI 线程每次渲染时调用。</para>
/// </summary>
public static class Highlighter
{
    private static readonly IReadOnlySet<string> s_keywords = DslKeywords.All;

    // 已知枚举值——过渡效果和缓动函数从 DslCore 共享常量自动同步（修复 P0: 硬编码与引擎不同步）
    private static readonly HashSet<string> s_knownEnumValues = new(
        DslTransitionNames.All
            .Concat(DslEasingNames.All)
            .Concat(new[] { "true", "false", "game", "menu", "ui" }));

    private static readonly HashSet<string> s_fileExtensions = new()
    {
        ".story", ".json", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
        ".mp3", ".ogg", ".wav", ".m4a", ".flac",
        ".mp4", ".webm", ".mkv", ".avi", ".mov",
    };

    // UI 元素子类分组——让 text/button/image 等不同类型的 UI 元素着不同色，
    // 解决"UI 元素关键字之间没区分"的视觉问题（展示型=红，容器型=青绿，交互型=橙）。
    private static readonly HashSet<string> s_uiContainer = new()
    {
        "panel", "vbox", "hbox", "container", "scrollview",
    };

    private static readonly HashSet<string> s_uiInteractive = new()
    {
        "button", "input", "checkbox", "slider",
    };

    /// <summary>从源码生成高亮 token 列表（单遍 O(n)，无 Pidgin）</summary>
    public static List<HighlightToken> GetHighlights(string source)
    {
        var highlights = new List<HighlightToken>();
        if (string.IsNullOrEmpty(source)) return highlights;

        var tokens = Lexer.Lexer.Tokenize(source);
        if (tokens.Count == 0) return highlights;

        // 预计算每行偏移量
        var lineOffsets = ComputeLineOffsets(source);
        
        // 记录每行首关键字（用于字符串语义着色）
        var lineKeyword = new Dictionary<int, string>();
        foreach (var t in tokens)
        {
            if (t.Type == TokenType.Newline) continue;
            if (lineKeyword.ContainsKey(t.Line)) continue;
            if (t.Type is TokenType.Keyword or TokenType.Identifier)
                lineKeyword[t.Line] = t.Value;
        }

        // 记录每行 token 列表（用于判断 token 位置）
        var lineTokens = new Dictionary<int, List<Token>>();
        foreach (var t in tokens)
        {
            if (t.Type == TokenType.Newline) continue;
            if (!lineTokens.TryGetValue(t.Line, out var list))
            {
                list = new List<Token>();
                lineTokens[t.Line] = list;
            }
            list.Add(t);
        }

        // 逐 token 分类
        foreach (var t in tokens)
        {
            if (t.Type == TokenType.Newline) continue;

            var start = lineOffsets[t.Line - 1] + (t.Column - 1);
            var length = t.Value.Length;
            var category = Classify(t, lineKeyword, lineTokens);
            highlights.Add(new HighlightToken(start, length, category));
        }

        // {variable} 和内联标记
        AddExpressionHighlights(source, highlights);

        return highlights;
    }

    private static int[] ComputeLineOffsets(string source)
    {
        // 统计行数
        var lineCount = 1;
        foreach (var c in source)
            if (c == '\n') lineCount++;

        var offsets = new int[lineCount];
        offsets[0] = 0;
        var idx = 1;
        for (var i = 0; i < source.Length && idx < lineCount; i++)
        {
            if (source[i] == '\n')
                offsets[idx++] = i + 1;
        }
        return offsets;
    }

    private static HighlightCategory Classify(
        Token token,
        Dictionary<int, string> lineKeyword,
        Dictionary<int, List<Token>> lineTokens)
    {
        switch (token.Type)
        {
            case TokenType.Comment: return HighlightCategory.Comment;
            case TokenType.Number: return HighlightCategory.Number;
            case TokenType.Keyword: return ClassifyKeyword(token, lineTokens);
            case TokenType.Symbol: return HighlightCategory.Symbol;
            
            case TokenType.String:
                // 根据行首关键字判断字符串语义
                if (lineKeyword.TryGetValue(token.Line, out var kw) &&
                    lineTokens.TryGetValue(token.Line, out var toks) &&
                    toks.Count > 0)
                {
                    // 判断是否是行内第一个字符串（紧跟关键字后）
                    var isFirst = toks[0].Type is TokenType.Keyword or TokenType.Identifier &&
                                  ReferenceEquals(toks[0], token) || 
                                  (toks.Count > 1 && ReferenceEquals(toks[1], token) && toks[0].Type is TokenType.Keyword or TokenType.Identifier);

                    // 更简单的判断：看 token 在行内的位置
                    var tokenIdx = -1;
                    for (var i = 0; i < toks.Count; i++)
                    {
                        if (ReferenceEquals(toks[i], token)) { tokenIdx = i; break; }
                    }

                    if (tokenIdx == 1 || (tokenIdx > 0 && toks[tokenIdx - 1].Type is TokenType.Keyword or TokenType.Identifier))
                    {
                        return kw switch
                        {
                            "style" => HighlightCategory.StyleName,
                            "character" => HighlightCategory.CharacterName,
                            "scene" => HighlightCategory.SceneName,
                            "label" => HighlightCategory.Label,
                            "navigate" => HighlightCategory.SceneName,
                            "call_screen" => HighlightCategory.SceneName,
                            "sprite" or "live2d_char" => HighlightCategory.CharacterName,
                            _ => IsPathValue(token.Value) ? HighlightCategory.PathValue : HighlightCategory.String,
                        };
                    }
                }

                if (IsPathValue(token.Value))
                    return HighlightCategory.PathValue;
                return HighlightCategory.String;

            case TokenType.Identifier:
                if (s_keywords.Contains(token.Value))
                    return HighlightCategory.Keyword;

                // 检查 key= 模式（属性名）
                if (lineTokens.TryGetValue(token.Line, out var toks2))
                {
                    var idx = -1;
                    for (var i = 0; i < toks2.Count; i++)
                    {
                        if (ReferenceEquals(toks2[i], token)) { idx = i; break; }
                    }
                    if (idx >= 0 && idx + 1 < toks2.Count && toks2[idx + 1].Value == "=")
                        return HighlightCategory.PropertyName;
                }

                if (s_knownEnumValues.Contains(token.Value))
                    return HighlightCategory.PropertyValue;
                if (token.Value.StartsWith('#') && token.Value.Length >= 4)
                    return HighlightCategory.ColorValue;
                return HighlightCategory.Plain;
        }

        return HighlightCategory.Plain;
    }

    /// <summary>
    /// 细分关键字类别——根据 DslKeywords 子集和上下文位置区分颜色。
    /// <para>语句关键字 → Keyword（蓝色），参数键后跟= → PropertyName（浅蓝），</para>
    /// <para>字面量 → PropertyValue（橙色），UI 元素类型 → Keyword（蓝色）。</para>
    /// </summary>
    private static HighlightCategory ClassifyKeyword(
        Token token,
        Dictionary<int, List<Token>> lineTokens)
    {
        var value = token.Value;

        // 获取 token 在行内的位置索引
        int tokenIdx = -1;
        List<Token>? toks = null;
        if (lineTokens.TryGetValue(token.Line, out var list))
        {
            toks = list;
            for (var i = 0; i < toks.Count; i++)
            {
                if (ReferenceEquals(toks[i], token)) { tokenIdx = i; break; }
            }
        }

        bool isAtLineStart = tokenIdx == 0;
        bool hasNextEquals = tokenIdx >= 0 && toks != null &&
                             tokenIdx + 1 < toks.Count && toks[tokenIdx + 1].Value == "=";

        // 1. 行首语句关键字（say/scene/if/while/set 等）→ 按语义子分组着色（P2: 缓解全蓝视觉疲劳）
        if (isAtLineStart && DslKeywords.Statements.Contains(value))
        {
            if (DslKeywords.ControlFlow.Contains(value)) return HighlightCategory.ControlFlow;   // 控制流 → 紫
            if (DslKeywords.Navigation.Contains(value)) return HighlightCategory.Navigation;      // 导航 → 青绿
            if (DslKeywords.DataOp.Contains(value)) return HighlightCategory.DataOp;              // 数据操作 → 黄
            if (DslKeywords.Media.Contains(value)) return HighlightCategory.Media;                // 媒体 → 橙绿
            return HighlightCategory.Keyword;                                                     // 显示/系统类主语句 → 蓝
        }

        // 2. 后面跟 = 的参数键 / UI 元素属性键 → PropertyName（浅蓝）
        //    如 volume=、duration=、speaker=、width=、height=、x=、y=
        if (hasNextEquals &&
            (DslKeywords.Parameters.Contains(value) || DslKeywords.ElementAttributes.Contains(value)))
            return HighlightCategory.PropertyName;

        // 3. 字面量值（true/false/game/menu/ui）→ PropertyValue（橙色）
        //    如 type=game 中的 game，clickable=true 中的 true
        if (DslKeywords.Literals.Contains(value))
            return HighlightCategory.PropertyValue;

        // 4. UI 元素类型（text/button/image/vbox/hbox 等）→ 按子类着色，互相区分
        if (DslKeywords.UiElementTypes.Contains(value))
        {
            if (s_uiContainer.Contains(value)) return HighlightCategory.UiContainer;     // 容器型 → 青绿
            if (s_uiInteractive.Contains(value)) return HighlightCategory.UiInteractive; // 交互型 → 橙
            return HighlightCategory.Uielement;                                           // 展示型 → 红
        }

        // 5. 其他参数关键字（once/in/clear/exit/hard/skipable/store 等不以 = 结尾的修饰符）→ Keyword（蓝色）
        if (DslKeywords.Parameters.Contains(value))
            return HighlightCategory.Keyword;

        // 6. 其他 UI 元素属性键（不以 = 结尾时）→ Keyword（蓝色）
        if (DslKeywords.ElementAttributes.Contains(value))
            return HighlightCategory.Keyword;

        // 默认 → Keyword
        return HighlightCategory.Keyword;
    }

    private static bool IsPathValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var lower = value.ToLowerInvariant();
        foreach (var ext in s_fileExtensions)
            if (lower.EndsWith(ext)) return true;
        return value.Contains('/') || value.Contains('\\');
    }

    private static void AddExpressionHighlights(string source, List<HighlightToken> highlights)
    {
        var i = 0;
        while (i < source.Length)
        {
            if (source[i] == '{')
            {
                var end = source.IndexOf('}', i + 1);
                if (end > i)
                {
                    var content = source.Substring(i + 1, end - i - 1).Trim();
                    if (IsInlineTag(content))
                        highlights.Add(new HighlightToken(i, end - i + 1, HighlightCategory.InlineTag));
                    else if (content.Length > 0)
                    {
                        if (content.StartsWith("color=") || content.StartsWith("font=") || content.StartsWith("size="))
                            highlights.Add(new HighlightToken(i, end - i + 1, HighlightCategory.InlineTag));
                        else
                            highlights.Add(new HighlightToken(i, end - i + 1, HighlightCategory.Variable));
                    }
                    i = end + 1;
                }
                else i++;
            }
            else i++;
        }
    }

    private static bool IsInlineTag(string content)
    {
        return content is "b" or "/b" or "i" or "/i" or "w" or "fast" or "p" ||
               content.StartsWith("color=") || content.StartsWith("/color") ||
               content.StartsWith("font=") || content.StartsWith("/font") ||
               content.StartsWith("size=") || content.StartsWith("/size");
    }
}
