using System.Collections.Generic;
using System.Text;

namespace LingFanEngine.SDK.Dsl;

/// <summary>
/// DSL 智能格式化器（Alt+Shift+F）。
/// <para>基于源缩进的块结构检测——DSL 是缩进式语言（无 end 关键字），
/// 源缩进即逻辑结构。格式化器将源缩进归一化为 4 空格单位。</para>
/// <para>同时修正：行尾空白、多余空格压缩、逗号后加空格。</para>
/// <para>注意：不修改 key=value 的 = 两侧空格——DSL 解析器使用 String("key=") 
/// 要求 = 紧贴 key，加空格会导致解析失败。</para>
/// </summary>
public static class DslFormatter
{
    private const int IndentSize = 4;

    /// <summary>格式化 DSL 源码</summary>
    public static string Format(string source)
    {
        if (string.IsNullOrEmpty(source))
            return source;

        var lines = source.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        var result = new List<string>(lines.Length);

        // 缩进栈：记录每一层深度对应的源缩进空格数
        // DSL 是缩进式语言，源缩进即逻辑结构
        var indentStack = new Stack<int>();
        indentStack.Push(0); // 根级别
        var depth = 0;

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
                result.Add(new string(' ', depth * IndentSize) + trimmed);
                continue;
            }

            // 计算源缩进（tab 按 4 空格计算）
            var sourceIndent = CountLeadingSpaces(rawLine);

            // 弹出栈直到找到匹配或更低的缩进级别（块结束）
            while (indentStack.Count > 1 && sourceIndent < indentStack.Peek())
            {
                indentStack.Pop();
                depth--;
            }

            // 如果源缩进大于栈顶 → 进入新的子块
            if (sourceIndent > indentStack.Peek())
            {
                indentStack.Push(sourceIndent);
                depth++;
            }

            // 输出归一化后的行
            result.Add(new string(' ', depth * IndentSize) + NormalizeLine(trimmed));
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

    /// <summary>
    /// 规范化单行内容（不含缩进）。
    /// <para>处理：压缩多余空格、逗号后加空格、删除行尾空白。</para>
    /// <para>不处理：= 两侧空格（DSL 解析器要求 key=value 紧凑写法）。</para>
    /// <para>字符串内的内容原样保留。行尾 // 注释先剥离再处理，避免注释中的引号破坏字符串检测。</para>
    /// </summary>
    private static string NormalizeLine(string line)
    {
        // 先剥离行尾注释（// 在引号外），避免注释中的 " 破坏字符串状态检测
        var commentSuffix = "";
        var codePart = StripInlineCommentPreserving(line, ref commentSuffix);

        var sb = new StringBuilder(codePart.Length + 8);
        var inString = false;

        for (var i = 0; i < codePart.Length; i++)
        {
            var c = codePart[i];

            // 字符串引号——切换字符串状态（支持 \" 转义）
            if (c == '"' && (i == 0 || codePart[i - 1] != '\\'))
            {
                inString = !inString;
                sb.Append(c);
                continue;
            }

            // 字符串内——原样输出
            if (inString)
            {
                sb.Append(c);
                continue;
            }

            // 逗号后确保有空格
            if (c == ',')
            {
                sb.Append(',');
                if (i + 1 < codePart.Length && codePart[i + 1] != ' ' && codePart[i + 1] != '\t')
                    sb.Append(' ');
                continue;
            }

            // 多个连续空格压缩为一个（非字符串内）
            if ((c == ' ' || c == '\t') && sb.Length > 0 && sb[^1] == ' ')
                continue;

            // tab 转空格
            if (c == '\t')
            {
                sb.Append(' ');
                continue;
            }

            sb.Append(c);
        }

        var normalized = sb.ToString().TrimEnd();
        return string.IsNullOrEmpty(commentSuffix) ? normalized : normalized + " " + commentSuffix;
    }

    /// <summary>
    /// 剥离行尾 // 注释，将注释部分存入 commentSuffix。
    /// <para>跟踪引号状态，仅在引号外遇到 // 时截断。</para>
    /// </summary>
    private static string StripInlineCommentPreserving(string line, ref string commentSuffix)
    {
        bool inQuotes = false;
        for (int i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\'))
                inQuotes = !inQuotes;
            else if (!inQuotes && line[i] == '/' && line[i + 1] == '/')
            {
                commentSuffix = line[i..].TrimEnd();
                return line[..i].TrimEnd();
            }
        }
        return line;
    }

    /// <summary>计算行的前导缩进空格数（tab 按 4 空格）</summary>
    private static int CountLeadingSpaces(string line)
    {
        var count = 0;
        foreach (var c in line)
        {
            if (c == ' ')
                count++;
            else if (c == '\t')
                count += IndentSize;
            else
                break;
        }
        return count;
    }
}
