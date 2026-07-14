using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Avalonia.Media;
using LingFanEngine.DslCore;
using LingFanEngine.SDK.Dsl.Highlight;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Editor;

/// <summary>
/// DSL 代码补全数据源——根据当前光标位置和上下文提供补全建议。
/// </summary>
public class DslCompletionProvider
{
    // 颜色
    private static readonly IBrush s_keywordColor = new SolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush s_paramColor = new SolidColorBrush(Color.Parse("#9CDCFE"));
    private static readonly IBrush s_variableColor = new SolidColorBrush(Color.Parse("#4EC9B0"));
    private static readonly IBrush s_sceneColor = new SolidColorBrush(Color.Parse("#DCDCAA"));

    /// <summary>
    /// 根据上下文获取补全列表
    /// </summary>
    /// <param name="document">文本文档</param>
    /// <param name="offset">光标偏移</param>
    /// <param name="variables">已收集的变量列表</param>
    /// <param name="scenes">已索引的场景名列表</param>
    /// <param name="labels">已索引的标签名列表</param>
    /// <param name="characters">已索引的角色键列表</param>
    public IEnumerable<ICompletionData> GetCompletions(
        TextDocument document,
        int offset,
        List<VariableInfo> variables,
        List<string> scenes,
        List<string> labels,
        List<string> characters)
    {
        // 获取当前行文本和光标在行中的位置
        var line = document.GetLineByOffset(offset);
        var lineText = document.GetText(line);
        var column = offset - line.Offset;

        // 获取光标前的文本（当前正在输入的单词）
        var wordStart = column;
        while (wordStart > 0 && IsWordChar(lineText[wordStart - 1]))
            wordStart--;

        var prefix = lineText.Substring(wordStart, column - wordStart).ToLowerInvariant();
        var beforeWord = wordStart > 0 ? lineText[..wordStart].TrimEnd() : "";

        var results = new List<ICompletionData>();

        // 根据上下文决定补全类型
        var context = GetCompletionContext(lineText, beforeWord, prefix);

        switch (context)
        {
            case CompletionContext.StatementStart:
                // 行首 → 语句关键字 + UI 元素类型
                foreach (var kw in DslKeywords.Statements)
                {
                    if (string.IsNullOrEmpty(prefix) || kw.StartsWith(prefix))
                    {
                        results.Add(new DslCompletionData(kw, kw, s_keywordColor, "语句关键字"));
                    }
                }
                foreach (var el in DslKeywords.UiElementTypes)
                {
                    if (string.IsNullOrEmpty(prefix) || el.StartsWith(prefix))
                    {
                        results.Add(new DslCompletionData(el, el, s_keywordColor, "UI 元素"));
                    }
                }
                break;

            case CompletionContext.ParameterName:
                // key= → 参数名
                foreach (var param in DslKeywords.Parameters)
                {
                    if (string.IsNullOrEmpty(prefix) || param.StartsWith(prefix))
                    {
                        results.Add(new DslCompletionData(param, param + "=", s_paramColor, "参数"));
                    }
                }
                foreach (var attr in DslKeywords.ElementAttributes)
                {
                    if (string.IsNullOrEmpty(prefix) || attr.StartsWith(prefix))
                    {
                        results.Add(new DslCompletionData(attr, attr + "=", s_paramColor, "元素属性"));
                    }
                }
                break;

            case CompletionContext.VariableReference:
                // { → 变量名
                foreach (var v in variables)
                {
                    if (string.IsNullOrEmpty(prefix) || v.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new DslCompletionData(v.Name, v.Name + "}", s_variableColor, $"变量 (行 {v.DefinitionLine})"));
                    }
                }
                break;

            case CompletionContext.SceneName:
                // navigate "/scene " → 场景名
                foreach (var scene in scenes)
                {
                    if (string.IsNullOrEmpty(prefix) || scene.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new DslCompletionData(scene, scene + "\"", s_sceneColor, "场景"));
                    }
                }
                break;

            case CompletionContext.LabelName:
                // jump/call → 标签名
                foreach (var label in labels)
                {
                    if (string.IsNullOrEmpty(prefix) || label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new DslCompletionData(label, label, s_sceneColor, "标签"));
                    }
                }
                break;

            case CompletionContext.SpeakerName:
                // speaker= → 角色名
                foreach (var ch in characters)
                {
                    if (string.IsNullOrEmpty(prefix) || ch.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new DslCompletionData(ch, ch, s_sceneColor, "角色"));
                    }
                }
                break;

            case CompletionContext.General:
            default:
                // 通用补全：关键字 + UI 元素 + 变量
                foreach (var kw in DslKeywords.Statements)
                {
                    if (string.IsNullOrEmpty(prefix) || kw.StartsWith(prefix))
                    {
                        results.Add(new DslCompletionData(kw, kw, s_keywordColor, "语句关键字"));
                    }
                }
                foreach (var el in DslKeywords.UiElementTypes)
                {
                    if (string.IsNullOrEmpty(prefix) || el.StartsWith(prefix))
                    {
                        results.Add(new DslCompletionData(el, el, s_keywordColor, "UI 元素"));
                    }
                }
                foreach (var v in variables)
                {
                    if (string.IsNullOrEmpty(prefix) || v.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new DslCompletionData(v.Name, v.Name, s_variableColor, $"变量 (行 {v.DefinitionLine})"));
                    }
                }
                break;
        }

        return results;
    }

    /// <summary>判断当前补全上下文</summary>
    private static CompletionContext GetCompletionContext(string lineText, string beforeWord, string prefix)
    {
        var trimmed = lineText.TrimStart();

        // 如果是行首（beforeWord 为空），补全语句关键字
        if (string.IsNullOrEmpty(beforeWord))
            return CompletionContext.StatementStart;

        // 检查特殊前缀
        var lowerBefore = beforeWord.ToLowerInvariant();

        // navigate "  → 场景名
        if (lowerBefore.EndsWith("navigate \"") || lowerBefore.EndsWith("scene \""))
            return CompletionContext.SceneName;

        // jump / call → 标签名
        if (lowerBefore.EndsWith("jump") || lowerBefore.EndsWith("call"))
            return CompletionContext.LabelName;

        // speaker=" → 角色名
        if (lowerBefore.EndsWith("speaker=\"") || lowerBefore.EndsWith("by=\""))
            return CompletionContext.SpeakerName;

        // { → 变量引用
        if (beforeWord.EndsWith("{"))
            return CompletionContext.VariableReference;

        // 如果前一个非空白字符是空格且不是在值位置 → 参数名
        if (HasParameterContext(beforeWord))
            return CompletionContext.ParameterName;

        return CompletionContext.General;
    }

    /// <summary>检查是否在参数位置（前面有语句关键字 + 空格）</summary>
    private static bool HasParameterContext(string beforeWord)
    {
        var parts = beforeWord.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        var firstWord = parts[0].ToLowerInvariant();
        return DslKeywords.Statements.Contains(firstWord) && parts.Length >= 1;
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '-';

    private enum CompletionContext
    {
        StatementStart,
        ParameterName,
        VariableReference,
        SceneName,
        LabelName,
        SpeakerName,
        General,
    }
}

/// <summary>DSL 补全数据项</summary>
public class DslCompletionData : ICompletionData
{
    public DslCompletionData(string text, string completionText, IBrush color, string description)
    {
        Text = text;
        CompletionText = completionText;
        Color = color;
        DescriptionText = description;
    }

    public IBrush Color { get; }
    public string CompletionText { get; }
    public string DescriptionText { get; }

    // ICompletionData
    public object Content => Text;
    public object Description => DescriptionText;
    public Avalonia.Media.IImage? Image => null;
    public double Priority { get; set; }
    public string Text { get; }

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs e)
    {
        textArea.Document.Replace(completionSegment, CompletionText);
    }
}
