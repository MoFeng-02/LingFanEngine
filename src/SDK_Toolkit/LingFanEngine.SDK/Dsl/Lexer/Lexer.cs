using System;
using System.Collections.Generic;
using LingFanEngine.DslCore;
using Pidgin;
using static Pidgin.Parser;
using static Pidgin.Parser<char>;

namespace LingFanEngine.SDK.Dsl.Lexer;

/// <summary>
/// 基于 Pidgin 的 DSL 词法分析器
/// <para>将 .story 源码字符串转换为 Token 流。</para>
/// <para>与引擎核心 DslStatementParser 保持 Pidgin 技术栈一致。</para>
/// </summary>
public static class Lexer
{
    // ====== 基础 parser 组合子 ======

    private static readonly Parser<char, Unit> Whitespace = SkipWhitespaces;

    /// <summary>行注释 #...</summary>
    private static readonly Parser<char, string> Comment =
        Char('#').Then(AnyCharExcept('\n', '\r').ManyString())
            .Select(s => "#" + s)
            .Labelled("comment");

    /// <summary>双引号字符串</summary>
    private static readonly Parser<char, string> QuotedString =
        Char('"').Then(AnyCharExcept('"').ManyString()).Before(Char('"'))
            .Labelled("quoted string");

    /// <summary>数字（整数或浮点数）</summary>
    private static readonly Parser<char, string> Number =
        (from sign in Char('-').Optional()
         from intPart in Digit.AtLeastOnceString()
         from fracPart in Try(Char('.').Then(Digit.AtLeastOnceString())).Optional()
         select (sign.HasValue ? "-" : "") + intPart +
                (fracPart.HasValue ? "." + fracPart.Value : ""))
        .Labelled("number");

    /// <summary>标识符/关键字</summary>
    private static readonly Parser<char, string> Identifier =
        Token(c => char.IsLetter(c) || c == '_').AtLeastOnceString()
            .Labelled("identifier");

    /// <summary>箭头 -></summary>
    private static readonly Parser<char, string> Arrow =
        String("->").Labelled("arrow");

    /// <summary>单字符符号</summary>
    private static readonly Parser<char, string> Symbol =
        OneOf(
            Char('=').Select(c => c.ToString()),
            Char(':').Select(c => c.ToString()),
            Char(',').Select(c => c.ToString()),
            Char('(').Select(c => c.ToString()),
            Char(')').Select(c => c.ToString()),
            Char('{').Select(c => c.ToString()),
            Char('}').Select(c => c.ToString()),
            Char('#').Select(c => c.ToString())
        ).Labelled("symbol");

    // ====== DSL 关键字集合（引用 DslCore 统一源） ======

    private static readonly IReadOnlySet<string> Keywords = DslKeywords.All;

    // ====== 单个 Token 解析器 ======

    /// <summary>解析一个 token（带位置追踪）</summary>
    private static Parser<char, Token> TokenParser =>
        Comment.Select(v => new Token(TokenType.Comment, v, 0, 0))
            .Or(QuotedString.Select(v => new Token(TokenType.String, v, 0, 0)))
            .Or(Number.Select(v => new Token(TokenType.Number, v, 0, 0)))
            .Or(Arrow.Select(v => new Token(TokenType.Symbol, v, 0, 0)))
            .Or(Identifier.Select(v =>
                Keywords.Contains(v)
                    ? new Token(TokenType.Keyword, v, 0, 0)
                    : new Token(TokenType.Identifier, v, 0, 0)))
            .Or(Symbol.Select(v => new Token(TokenType.Symbol, v, 0, 0)));

    /// <summary>
    /// 将 .story 源码词法分析为 Token 列表
    /// </summary>
    public static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        var lines = source.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

        for (var lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            var line = lines[lineIdx];
            var col = 0;

            while (col < line.Length)
            {
                // 跳过空白
                while (col < line.Length && char.IsWhiteSpace(line[col]))
                    col++;

                if (col >= line.Length)
                    break;

                var remaining = line[col..];
                var result = TokenParser.Parse(remaining);

                if (result.Success)
                {
                    var token = result.Value with { Line = lineIdx + 1, Column = col + 1 };
                    tokens.Add(token);
                    col += result.Value.Value.Length;
                }
                else
                {
                    // 未知字符，跳过
                    col++;
                }
            }

            // 添加换行 token
            tokens.Add(new Token(TokenType.Newline, "\\n", lineIdx + 1, line.Length + 1));
        }

        return tokens;
    }
}
