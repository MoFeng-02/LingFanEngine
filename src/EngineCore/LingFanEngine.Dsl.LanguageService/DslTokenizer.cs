using System.Collections.Generic;
using LingFanEngine.DslCore;

namespace LingFanEngine.Dsl.LanguageService;

/// <summary>
/// DSL 词法分析器——消费 <see cref="DslKeywords.All"/> 单一真相源做关键字判定。
/// <para>设计目标（对标规划 §3.1/§3.3）：</para>
/// <list type="bullet">
///   <item><description>单遍、<see cref="ReadOnlySpan{T}"/> 字符扫描，无回溯。</description></item>
///   <item><description>产出共享的 <see cref="DslToken"/>（绝对偏移 + 长度 + 词法类型），文本按需切片，零分配友好。</description></item>
///   <item><description>行级 token 化（DSL 以行为语句单位），便于 <see cref="DslDocument"/> 做脏行增量重算。</description></item>
///   <item><description>NativeAOT 友好：无反射 / 无动态代码生成。</description></item>
/// </list>
/// <para>替代 SDK 当前的手写 <c>LingFanEngine.SDK.Dsl.Lexer.Lexer</c>（M3 移除）。</para>
/// </summary>
public static class DslTokenizer
{
    private static readonly IReadOnlySet<string> s_keywords = DslKeywords.All;

    /// <summary>将整段源码词法分析为扁平 token 数组（绝对偏移）。</summary>
    public static DslToken[] Tokenize(ReadOnlySpan<char> source)
    {
        var tokens = new List<DslToken>(source.Length / 8 + 16);
        var n = source.Length;
        var i = 0;
        while (i < n)
        {
            var lineEnd = i;
            while (lineEnd < n && source[lineEnd] != '\n')
                lineEnd++;
            TokenizeLineInto(source.Slice(i, lineEnd - i), i, tokens);
            i = lineEnd + 1; // 跳过 '\n'（若存在）
        }
        return tokens.ToArray();
    }

    /// <summary>词法分析单行（<paramref name="lineStartOffset"/> 为该行首字符在全文中的绝对偏移）。</summary>
    public static DslToken[] TokenizeLine(ReadOnlySpan<char> line, int lineStartOffset)
    {
        var tokens = new List<DslToken>(8);
        TokenizeLineInto(line, lineStartOffset, tokens);
        return tokens.ToArray();
    }

    /// <summary>词法分析给定文本片段（<paramref name="text"/> 为待扫描文本，<paramref name="baseOffset"/>
    /// 为 <paramref name="text"/>[0] 在全文中的绝对偏移）。内部一律用相对索引 <c>text[col]</c>（恒在界内），
    /// 产出的 token 偏移为 <c>baseOffset + col</c>，从而切片 / 全文两种调用方式均安全。</summary>
    private static void TokenizeLineInto(ReadOnlySpan<char> text, int baseOffset, List<DslToken> tokens)
    {
        var n = text.Length;
        var col = 0;
        while (col < n)
        {
            var ch = text[col];

            // 空白：不产出 token（高亮无需空白）
            if (ch is ' ' or '\t' or '\r')
            {
                col++;
                continue;
            }

            // 注释 # 或 //
            if (ch == '#' || (ch == '/' && col + 1 < n && text[col + 1] == '/'))
            {
                var s = col;
                while (col < n) col++;
                tokens.Add(new DslToken(baseOffset + s, col - s, DslTokenKind.Comment));
                continue;
            }

            // 双引号字符串（含未闭合；跳过 \" 转义——与 DslCore 解析器行为一致）
            if (ch == '"')
            {
                var s = col;
                col++;
                while (col < n)
                {
                    if (text[col] == '\\' && col + 1 < n) { col += 2; continue; }
                    if (text[col] == '"') break;
                    col++;
                }
                if (col < n && text[col] == '"') col++; // 含结尾引号
                tokens.Add(new DslToken(baseOffset + s, col - s, DslTokenKind.String));
                continue;
            }

            // 数字（含前导负号）
            if (char.IsDigit(ch) || (ch == '-' && col + 1 < n && char.IsDigit(text[col + 1])))
            {
                var s = col;
                if (ch == '-') col++;
                while (col < n && (char.IsDigit(text[col]) || text[col] == '.')) col++;
                tokens.Add(new DslToken(baseOffset + s, col - s, DslTokenKind.Number));
                continue;
            }

            // 箭头 ->
            if (ch == '-' && col + 1 < n && text[col + 1] == '>')
            {
                tokens.Add(new DslToken(baseOffset + col, 2, DslTokenKind.Symbol));
                col += 2;
                continue;
            }

            // 标识符 / 关键字
            if (char.IsLetter(ch) || ch == '_')
            {
                var s = col;
                while (col < n && (char.IsLetterOrDigit(text[col]) || text[col] == '_' || text[col] == '-')) col++;
                var word = text.Slice(s, col - s).ToString();
                var kind = s_keywords.Contains(word) ? DslTokenKind.Keyword : DslTokenKind.Identifier;
                tokens.Add(new DslToken(baseOffset + s, col - s, kind));
                continue;
            }

            // 单字符符号（= : -> 已在上方处理；此处覆盖其余）
            if (ch is '=' or ':' or ',' or '(' or ')' or '{' or '}' or '.' or '+' or '*' or '/' or '%' or '>' or '<' or '!' or '?' or '&' or '|')
            {
                tokens.Add(new DslToken(baseOffset + col, 1, DslTokenKind.Symbol));
                col++;
                continue;
            }

            // 其余未知字符：单字符跳过（保持覆盖，不丢偏移）
            col++;
        }
    }
}
