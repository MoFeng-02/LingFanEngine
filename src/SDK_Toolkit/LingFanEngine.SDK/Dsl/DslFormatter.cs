using System.Collections.Generic;
using System.Text;

namespace LingFanEngine.SDK.Dsl;

/// <summary>
/// DSL 智能格式化器（Ctrl+K+D）
/// <para>基于块栈的正确缩进算法——scene/if/while/for/func/switch/menu 增加缩进，
/// 同级关键字 else/case/default 回退一级后同级，其他行按栈深度缩进。</para>
/// <para>同时修正：行尾空白、key=value 两侧空格、运算符间距。</para>
/// </summary>
public static class DslFormatter
{
    private const string IndentUnit = "    ";

    // 块开头关键字——下一行增加缩进
    private static readonly HashSet<string> s_blockStarters = new()
    {
        "scene", "if", "while", "for", "func", "switch", "foreach",
    };

    // 同级关键字——回退一级后输出（不改变栈深度）
    private static readonly HashSet<string> s_dedentKeywords = new()
    {
        "else", "case", "default",
    };

    /// <summary>格式化 DSL 源码（Ctrl+K+D）</summary>
    public static string Format(string source)
    {
        if (string.IsNullOrEmpty(source))
            return source;

        var lines = source.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        var result = new List<string>(lines.Length);
        var indentStack = new Stack<int>();
        indentStack.Push(0); // 根级别

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();

            // 空行——保留但不加缩进
            if (string.IsNullOrEmpty(trimmed))
            {
                result.Add("");
                continue;
            }

            // 注释行——保持当前缩进
            if (DslCommentHelper.IsCommentLine(trimmed))
            {
                result.Add(GetIndent(indentStack) + trimmed);
                continue;
            }

            var firstWord = GetFirstWord(trimmed);

            // 同级关键字（else/case/default）：回退一级输出
            if (s_dedentKeywords.Contains(firstWord))
            {
                var dedentLevel = System.Math.Max(0, indentStack.Peek() - 1);
                result.Add(new string(' ', dedentLevel * 4) + NormalizeLine(trimmed));

                // else if 不改变栈——和 if 同级
                // case/default 不改变栈——和 switch 同级
                continue;
            }

            // 正常行——按当前栈深度缩进
            result.Add(GetIndent(indentStack) + NormalizeLine(trimmed));

            // 块开头关键字——压栈
            if (s_blockStarters.Contains(firstWord))
            {
                indentStack.Push(indentStack.Peek() + 1);
            }
            // menu 块——花括号闭合时需要在同一行处理
            else if (firstWord == "menu" && trimmed.EndsWith("{"))
            {
                indentStack.Push(indentStack.Peek() + 1);
            }
        }

        // 构建结果
        var sb = new StringBuilder();
        for (var i = 0; i < result.Count; i++)
        {
            sb.Append(result[i]);
            if (i < result.Count - 1)
                sb.Append('\n');
        }
        sb.Append('\n');
        return sb.ToString();
    }

    /// <summary>规范化单行——key=value 两侧加空格、逗号后加空格、删除行尾空白</summary>
    private static string NormalizeLine(string line)
    {
        // 不处理字符串内容内的空格
        var sb = new StringBuilder(line.Length + 8);
        var inString = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            // 字符串内——原样输出
            if (c == '"')
            {
                inString = !inString;
                sb.Append(c);
                continue;
            }

            if (inString)
            {
                sb.Append(c);
                continue;
            }

            // key=value → key = value（确保两侧有空格）
            if (c == '=' && i > 0 && line[i - 1] != '=' && i + 1 < line.Length && line[i + 1] != '=')
            {
                // 前面没空格则补
                if (sb.Length > 0 && sb[^1] != ' ')
                    sb.Append(' ');
                sb.Append("= ");
                // 跳过后面已有的空格
                while (i + 1 < line.Length && line[i + 1] == ' ')
                    i++;
                continue;
            }

            // 逗号后加空格
            if (c == ',')
            {
                sb.Append(',');
                if (i + 1 < line.Length && line[i + 1] != ' ' && line[i + 1] != '\n')
                    sb.Append(' ');
                continue;
            }

            // 多个连续空格压缩为一个（非字符串内）
            if (c == ' ' && sb.Length > 0 && sb[^1] == ' ')
                continue;

            sb.Append(c);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>根据缩进栈生成缩进字符串</summary>
    private static string GetIndent(Stack<int> stack)
    {
        return new string(' ', stack.Peek() * 4);
    }

    private static string GetFirstWord(string trimmedLine)
    {
        var spaceIdx = trimmedLine.IndexOf(' ');
        if (spaceIdx < 0)
            return trimmedLine;
        return trimmedLine[..spaceIdx];
    }
}
