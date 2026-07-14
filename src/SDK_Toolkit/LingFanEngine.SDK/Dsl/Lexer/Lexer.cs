using System;
using System.Collections.Generic;
using LingFanEngine.DslCore;

namespace LingFanEngine.SDK.Dsl.Lexer;

/// <summary>
/// 手写 DSL 词法分析器（零依赖，纯字符扫描）。
/// <para>替代 Pidgin 版本——每次按键在 UI 线程上运行，必须极快。</para>
/// <para>逐行扫描，O(n) 单遍，无回溯无分配。</para>
/// </summary>
public static class Lexer
{
    private static readonly IReadOnlySet<string> Keywords = DslKeywords.All;

    /// <summary>
    /// 将 .story 源码词法分析为 Token 列表。
    /// 逐行扫描，每行内单遍前进，不回溯。
    /// </summary>
    public static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        if (string.IsNullOrEmpty(source)) return tokens;

        var lineIdx = 0;
        var globalOffset = 0;
        var lineStart = 0;

        for (var i = 0; i <= source.Length; i++)
        {
            if (i == source.Length || source[i] == '\n')
            {
                // 处理一行
                TokenizeLine(source, lineStart, i, lineIdx, globalOffset, tokens);
                
                // 添加换行 token
                tokens.Add(new Token(TokenType.Newline, "\\n", lineIdx + 1, i - lineStart + 1));
                
                lineIdx++;
                globalOffset += (i - lineStart) + 1;
                lineStart = i + 1;
            }
        }

        return tokens;
    }

    /// <summary>扫描单行，生成 token</summary>
    private static void TokenizeLine(string source, int lineStart, int lineEnd, int lineIdx, int globalOffset, List<Token> tokens)
    {
        var col = lineStart;

        while (col < lineEnd)
        {
            // 跳过空白
            while (col < lineEnd && char.IsWhiteSpace(source[col]))
                col++;
            if (col >= lineEnd) break;

            var ch = source[col];

            // 注释 # 或 //
            if (ch == '#' || (ch == '/' && col + 1 < lineEnd && source[col + 1] == '/'))
            {
                var start = col;
                while (col < lineEnd) col++;
                var text = source[start..col];
                tokens.Add(new Token(TokenType.Comment, text, lineIdx + 1, start - lineStart + 1));
                continue;
            }

            // 双引号字符串（含未闭合）
            if (ch == '"')
            {
                var start = col;
                col++; // 跳过开头 "
                while (col < lineEnd && source[col] != '"')
                    col++;
                // 取引号内内容（如果未闭合则取到行尾）
                var content = source[(start + 1)..col];
                if (col < lineEnd && source[col] == '"')
                    col++; // 跳过结尾 "
                tokens.Add(new Token(TokenType.String, content, lineIdx + 1, start - lineStart + 1));
                continue;
            }

            // 数字
            if (char.IsDigit(ch) || (ch == '-' && col + 1 < lineEnd && char.IsDigit(source[col + 1])))
            {
                var start = col;
                if (ch == '-') col++;
                while (col < lineEnd && (char.IsDigit(source[col]) || source[col] == '.'))
                    col++;
                var text = source[start..col];
                tokens.Add(new Token(TokenType.Number, text, lineIdx + 1, start - lineStart + 1));
                continue;
            }

            // 箭头 ->
            if (ch == '-' && col + 1 < lineEnd && source[col + 1] == '>')
            {
                col += 2;
                tokens.Add(new Token(TokenType.Symbol, "->", lineIdx + 1, col - 2 - lineStart + 1));
                continue;
            }

            // 标识符/关键字
            if (char.IsLetter(ch) || ch == '_')
            {
                var start = col;
                while (col < lineEnd && (char.IsLetterOrDigit(source[col]) || source[col] == '_' || source[col] == '-'))
                    col++;
                var text = source[start..col];
                var type = Keywords.Contains(text) ? TokenType.Keyword : TokenType.Identifier;
                tokens.Add(new Token(type, text, lineIdx + 1, start - lineStart + 1));
                continue;
            }

            // 单字符符号
            if (ch is '=' or ':' or ',' or '(' or ')' or '{' or '}')
            {
                col++;
                tokens.Add(new Token(TokenType.Symbol, ch.ToString(), lineIdx + 1, col - 1 - lineStart + 1));
                continue;
            }

            // 跳过未知字符
            col++;
        }
    }
}
