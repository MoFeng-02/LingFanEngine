using System.Collections.Generic;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;

namespace LingFanEngine.SDK.Editor;

/// <summary>
/// DSL 代码折叠策略（P2-1）
/// <para>基于缩进层级生成折叠区段：scene/if/while/for/func/switch/foreach 块。</para>
/// </summary>
public class DslFoldingStrategy
{
    // 块开头关键字
    private static readonly HashSet<string> s_blockStarters = new()
    {
        "scene", "if", "while", "for", "func", "switch", "foreach",
    };

    /// <summary>生成折叠区段</summary>
    public IEnumerable<NewFolding> CreateNewFoldings(TextDocument document, out int firstErrorOffset)
    {
        firstErrorOffset = -1;
        var foldings = new List<NewFolding>();
        var lines = document.Text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

        // 栈追踪块开头
        var stack = new Stack<(int StartLine, int Indent, string Keyword)>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var indent = CountIndent(line);
            var firstWord = GetFirstWord(trimmed);

            // 检测块开头
            if (s_blockStarters.Contains(firstWord))
            {
                stack.Push((i, indent, firstWord));
            }
            else if (firstWord == "else" || firstWord == "case" || firstWord == "default")
            {
                // 这些不是新块——它们属于 if/switch 块
            }
            else
            {
                // 普通语句：关闭缩进更深的块
                while (stack.Count > 0 && indent <= stack.Peek().Indent)
                {
                    var (startLine, _, _) = stack.Pop();
                    if (i - 1 > startLine) // 至少 2 行才折叠
                    {
                        var startOffset = document.GetLineByNumber(startLine + 1).Offset;
                        var endOffset = document.GetLineByNumber(i).Offset;
                        foldings.Add(new NewFolding(startOffset, endOffset));
                    }
                }
            }
        }

        // 关闭剩余的块
        while (stack.Count > 0)
        {
            var (startLine, _, _) = stack.Pop();
            if (lines.Length - 1 > startLine)
            {
                var startOffset = document.GetLineByNumber(startLine + 1).Offset;
                var endOffset = document.TextLength;
                foldings.Add(new NewFolding(startOffset, endOffset));
            }
        }

        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;
    }

    private static int CountIndent(string line)
    {
        var count = 0;
        foreach (var c in line)
        {
            if (c == ' ') count++;
            else if (c == '\t') count += 4;
            else break;
        }
        return count;
    }

    private static string GetFirstWord(string trimmedLine)
    {
        var spaceIdx = trimmedLine.IndexOf(' ');
        if (spaceIdx < 0)
            return trimmedLine;
        return trimmedLine[..spaceIdx];
    }
}
