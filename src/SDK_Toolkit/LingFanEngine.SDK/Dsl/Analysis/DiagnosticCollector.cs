using System.Collections.Generic;
using LingFanEngine.DslCore;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Dsl.Analysis;

/// <summary>
/// 错误/警告收集器——基于引擎核心的缩进式块结构
/// <para>引擎核心使用缩进确定 if/while/for 块边界（无 end 关键字）。
/// 此收集器通过追踪缩进层级来验证块结构完整性。</para>
/// </summary>
public static class DiagnosticCollector
{
    /// <summary>收集解析过程中的诊断信息</summary>
    public static (List<DslDiagnostic> Errors, List<DslDiagnostic> Warnings) Collect(
        List<DslStatement> statements,
        List<string> rawLines)
    {
        var errors = new List<DslDiagnostic>();
        var warnings = new List<DslDiagnostic>();

        // 检查无法解析的行（ParseLine 返回 null 的非空非注释行）
        var parsedLineNumbers = new HashSet<int>();
        foreach (var stmt in statements)
            parsedLineNumbers.Add(stmt.LineNumber);

        for (var i = 0; i < rawLines.Count; i++)
        {
            var line = rawLines[i].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            // 如果该行没有被解析（不在 statements 中），报错
            if (!parsedLineNumbers.Contains(i))
            {
                errors.Add(new DslDiagnostic(i + 1, 1, $"无法解析的语句: {line}", line));
            }
        }

        // 检查缩进式块结构配对
        CheckBlockStructure(rawLines, errors);

        return (errors, warnings);
    }

    /// <summary>
    /// 使用缩进层级检查 if/else/while/for 块结构。
    /// <para>规则：块开头语句（if/while/for）的下一行缩进必须更深。
    /// else/else if 的缩进应与对应的 if 相同。
    /// 块结束时缩进回到块开头的层级。</para>
    /// </summary>
    private static void CheckBlockStructure(List<string> rawLines, List<DslDiagnostic> errors)
    {
        // 用栈追踪块结构
        var blockStack = new Stack<(string Type, int Indent, int Line)>();

        for (var i = 0; i < rawLines.Count; i++)
        {
            var rawLine = rawLines[i];
            var trimmed = rawLine.Trim();

            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var indent = CountIndent(rawLine);

            // 检测块开头
            var firstWord = GetFirstWord(trimmed);

            if (firstWord == "if" || firstWord == "while" || firstWord == "for")
            {
                blockStack.Push((firstWord, indent, i + 1));
            }
            else if (firstWord == "else")
            {
                // else 或 else if
                if (blockStack.Count == 0 || blockStack.Peek().Type != "if")
                {
                    errors.Add(new DslDiagnostic(i + 1, 1,
                        "else 没有对应的 if", trimmed));
                }
            }
            else
            {
                // 普通语句：检查是否退出了块
                while (blockStack.Count > 0 && indent <= blockStack.Peek().Indent)
                {
                    blockStack.Pop();
                }
            }
        }

        // 剩余未关闭的块
        foreach (var (type, _, line) in blockStack)
        {
            errors.Add(new DslDiagnostic(line, 1,
                $"{type} 块可能未正确关闭（缩进式块结构，检查缩进）", null));
        }
    }

    /// <summary>计算行的缩进空格数（tab 算 4 空格）</summary>
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

    /// <summary>获取行的第一个单词</summary>
    private static string GetFirstWord(string trimmedLine)
    {
        var spaceIdx = trimmedLine.IndexOf(' ');
        if (spaceIdx < 0)
            return trimmedLine;
        return trimmedLine[..spaceIdx];
    }
}
