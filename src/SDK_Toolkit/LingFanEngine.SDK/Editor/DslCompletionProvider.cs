using System;
using System.Collections.Generic;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Avalonia.Media;
using LingFanEngine.DslCore;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Editor;

/// <summary>
/// DSL 代码补全数据源——根据当前光标位置和上下文提供补全建议。
/// <para>支持上下文感知：行首关键字、参数名、值枚举、变量引用、场景/标签/角色名。</para>
/// <para>字符串内安全防护：检测引号状态，避免在对话文本中弹出无关补全。</para>
/// </summary>
public class DslCompletionProvider
{
    // 颜色
    private static readonly IBrush s_keywordColor = new SolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush s_paramColor = new SolidColorBrush(Color.Parse("#9CDCFE"));
    private static readonly IBrush s_variableColor = new SolidColorBrush(Color.Parse("#4EC9B0"));
    private static readonly IBrush s_sceneColor = new SolidColorBrush(Color.Parse("#DCDCAA"));
    private static readonly IBrush s_valueColor = new SolidColorBrush(Color.Parse("#CE9178"));

    // 布尔值
    private static readonly string[] s_booleanValues = { "true", "false" };

    // 场景类型枚举
    private static readonly string[] s_sceneTypes = { "game", "menu", "ui" };

    // 过渡效果——从 DslTransitionNames 共享常量自动同步（修复 P0: 硬编码与引擎不同步）
    private static readonly IReadOnlySet<string> s_transitions = DslTransitionNames.All;

    // 缓动函数——从 DslEasingNames 共享常量自动同步（修复 P0: 硬编码与引擎不同步）
    private static readonly IReadOnlySet<string> s_easings = DslEasingNames.All;

    // 布尔参数集合——key=true|false 形式，true 和 false 都合法
    private static readonly HashSet<string> s_booleanParams = new()
    {
        "loop", "autoplay", "skipable", "screenshot", "mask", "unlock",
    };

    // 仅 true 参数集合——解析器使用 String("key=true")，只接受 true
    private static readonly HashSet<string> s_trueOnlyParams = new()
    {
        "clickable", "noskip", "instant", "typewriter",
    };

    // 仅 true 值列表
    private static readonly string[] s_trueOnlyValues = { "true" };

    /// <summary>
    /// 根据上下文获取补全列表
    /// </summary>
    public IEnumerable<ICompletionData> GetCompletions(
        TextDocument document,
        int offset,
        List<VariableInfo> variables,
        List<string> scenes,
        List<string> labels,
        List<string> characters,
        List<string>? crossFileVariables = null)
    {
        // 获取当前行文本和光标在行中的位置
        var line = document.GetLineByOffset(offset);
        var lineText = document.GetText(line);
        var column = offset - line.Offset;

        // 获取光标前的文本（当前正在输入的单词）
        var wordStart = column;
        while (wordStart > 0 && IsWordChar(lineText[wordStart - 1]))
            wordStart--;

        var prefix = lineText.Substring(wordStart, column - wordStart);
        var beforeWord = wordStart > 0 ? lineText[..wordStart].TrimEnd() : "";

        var results = new List<ICompletionData>();

        // 根据上下文决定补全类型
        var context = GetCompletionContext(lineText, column, beforeWord, prefix);

        switch (context)
        {
            case CompletionContext.StatementStart:
                // 行首 → 语句关键字 + UI 元素类型
                AddKeywordCompletions(results, prefix, DslKeywords.Statements, "语句关键字", s_keywordColor);
                AddKeywordCompletions(results, prefix, DslKeywords.UiElementTypes, "UI 元素", s_keywordColor);
                break;

            case CompletionContext.ParameterName:
                // key= → 参数名
                AddKeywordCompletions(results, prefix, DslKeywords.Parameters, "参数", s_paramColor, appendEquals: true);
                AddKeywordCompletions(results, prefix, DslKeywords.ElementAttributes, "元素属性", s_paramColor, appendEquals: true);
                break;

            case CompletionContext.VariableReference:
                // { → 变量名（当前文件变量 + 跨场景变量）
                var addedV = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var v in variables)
                {
                    if (Matches(prefix, v.Name))
                    {
                        results.Add(new DslCompletionData(v.Name, v.Name + "}", s_variableColor, $"变量 (行 {v.DefinitionLine})"));
                        addedV.Add(v.Name);
                    }
                }
                if (crossFileVariables != null)
                {
                    foreach (var name in crossFileVariables)
                    {
                        if (addedV.Contains(name)) continue;
                        if (Matches(prefix, name))
                            results.Add(new DslCompletionData(name, name + "}", s_variableColor, "变量 (跨场景)"));
                    }
                }
                break;

            case CompletionContext.SceneName:
                // navigate "/scene " → 场景名
                foreach (var scene in scenes)
                {
                    if (Matches(prefix, scene))
                        results.Add(new DslCompletionData(scene, scene + "\"", s_sceneColor, "场景"));
                }
                break;

            case CompletionContext.LabelName:
                // jump/call → 标签名
                foreach (var label in labels)
                {
                    if (Matches(prefix, label))
                        results.Add(new DslCompletionData(label, label, s_sceneColor, "标签"));
                }
                break;

            case CompletionContext.SpeakerName:
                // speaker= → 角色名
                foreach (var ch in characters)
                {
                    if (Matches(prefix, ch))
                        results.Add(new DslCompletionData(ch, ch, s_sceneColor, "角色"));
                }
                break;

            case CompletionContext.EnumValue:
                // type= → 场景类型枚举
                AddValueCompletions(results, prefix, s_sceneTypes, "场景类型");
                break;

            case CompletionContext.BooleanValue:
                // loop=/autoplay=/etc. → true/false
                AddValueCompletions(results, prefix, s_booleanValues, "布尔值");
                break;

            case CompletionContext.TrueOnlyValue:
                // clickable=/noskip=/etc. → true（解析器只接受 true）
                AddValueCompletions(results, prefix, s_trueOnlyValues, "布尔值");
                break;

            case CompletionContext.TransitionValue:
                // with=/transition= → 过渡效果
                AddValueCompletions(results, prefix, s_transitions, "过渡效果");
                break;

            case CompletionContext.EasingValue:
                // easing= → 缓动函数
                AddValueCompletions(results, prefix, s_easings, "缓动函数");
                break;

            case CompletionContext.None:
                // 无补全（如字符串内的对话文本）
                break;

            case CompletionContext.General:
            default:
                // 通用补全：关键字 + UI 元素 + 变量（当前文件 + 跨场景）
                AddKeywordCompletions(results, prefix, DslKeywords.Statements, "语句关键字", s_keywordColor);
                AddKeywordCompletions(results, prefix, DslKeywords.UiElementTypes, "UI 元素", s_keywordColor);
                var addedG = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var v in variables)
                {
                    if (Matches(prefix, v.Name))
                    {
                        results.Add(new DslCompletionData(v.Name, v.Name, s_variableColor, $"变量 (行 {v.DefinitionLine})"));
                        addedG.Add(v.Name);
                    }
                }
                if (crossFileVariables != null)
                {
                    foreach (var name in crossFileVariables)
                    {
                        if (addedG.Contains(name)) continue;
                        if (Matches(prefix, name))
                            results.Add(new DslCompletionData(name, name, s_variableColor, "变量 (跨场景)"));
                    }
                }
                break;
        }

        return results;
    }

    // ====== 补全辅助方法 ======

    private static void AddKeywordCompletions(
        List<ICompletionData> results, string prefix,
        IReadOnlySet<string> keywords, string description,
        IBrush color,
        bool appendEquals = false)
    {
        foreach (var kw in keywords)
        {
            if (Matches(prefix, kw))
            {
                var text = appendEquals ? kw + "=" : kw;
                results.Add(new DslCompletionData(kw, text, color, description));
            }
        }
    }

    private static void AddValueCompletions(
        List<ICompletionData> results, string prefix,
        IEnumerable<string> values, string description)
    {
        foreach (var val in values)
        {
            if (Matches(prefix, val))
                results.Add(new DslCompletionData(val, val, s_valueColor, description));
        }
    }

    private static bool Matches(string prefix, string text)
    {
        if (string.IsNullOrEmpty(prefix))
            return true;
        return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    // ====== 上下文检测 ======

    /// <summary>判断当前补全上下文</summary>
    private static CompletionContext GetCompletionContext(
        string lineText, int column, string beforeWord, string prefix)
    {
        // 1. 检查是否在字符串内
        if (IsInsideString(lineText, column))
        {
            var beforeString = GetTextBeforeString(lineText, column);
            var lowerStr = beforeString.ToLowerInvariant();

            // navigate "  → 场景名
            if (IsLastWord(lowerStr, "navigate") || IsLastWord(lowerStr, "scene"))
                return CompletionContext.SceneName;

            // jump / call → 标签名
            if (IsLastWord(lowerStr, "jump") || IsLastWord(lowerStr, "call"))
                return CompletionContext.LabelName;

            // speaker=" → 角色名（key=value 语法）
            if (lowerStr.EndsWith("speaker="))
                return CompletionContext.SpeakerName;

            // by "speaker" → 角色名（空格分隔语法）
            if (IsLastWord(lowerStr, "by"))
                return CompletionContext.SpeakerName;

            // with "transition" → 过渡效果（空格分隔语法）
            if (IsLastWord(lowerStr, "with"))
                return CompletionContext.TransitionValue;

            // key=" → 值补全（引号值）
            if (lowerStr.EndsWith("="))
            {
                var paramName = ExtractParamName(lowerStr[..^1]);
                var valueType = GetValueTypeForParam(paramName);
                if (valueType != CompletionContext.None)
                    return valueType;
            }

            // 其他字符串内（对话文本、文件路径等）→ 无补全
            return CompletionContext.None;
        }

        // 2. 检查是否在值位置（after key=）
        if (TryGetValueContext(beforeWord, out var valueContext))
            return valueContext;

        // 3. 行首 → 语句关键字
        if (string.IsNullOrEmpty(beforeWord))
            return CompletionContext.StatementStart;

        // 4. 检查特殊前缀
        var lowerBefore = beforeWord.ToLowerInvariant();

        // jump / call → 标签名（无引号）
        if (IsLastWord(lowerBefore, "jump") || IsLastWord(lowerBefore, "call"))
            return CompletionContext.LabelName;

        // with "transition" → 过渡效果（空格分隔，非字符串内）
        if (IsLastWord(lowerBefore, "with"))
            return CompletionContext.TransitionValue;

        // { → 变量引用
        if (beforeWord.EndsWith("{"))
            return CompletionContext.VariableReference;

        // 5. 参数位置（前面有语句关键字 + 空格）
        if (HasParameterContext(beforeWord))
            return CompletionContext.ParameterName;

        return CompletionContext.General;
    }

    /// <summary>尝试检测值上下文（after key=）</summary>
    private static bool TryGetValueContext(string beforeWord, out CompletionContext context)
    {
        context = CompletionContext.None;

        // beforeWord 以 = 结尾 → 无引号值
        if (beforeWord.EndsWith("="))
        {
            var paramName = ExtractParamName(beforeWord[..^1]);
            context = GetValueTypeForParam(paramName);
            return true;
        }

        // beforeWord 以 =" 结尾 → 引号值（但光标在引号外，已由 IsInsideString 处理）
        // 这里处理的是 =" 后光标已在引号内的场景，由步骤 1 处理

        return false;
    }

    /// <summary>从 beforeWord 中提取参数名</summary>
    private static string ExtractParamName(string text)
    {
        text = text.TrimEnd();
        var lastSpace = text.LastIndexOf(' ');
        return lastSpace >= 0 ? text[(lastSpace + 1)..] : text;
    }

    /// <summary>根据参数名推断值类型</summary>
    private static CompletionContext GetValueTypeForParam(string paramName)
    {
        var lower = paramName.ToLowerInvariant();
        if (lower == "type")
            return CompletionContext.EnumValue;
        if (lower == "transition")
            return CompletionContext.TransitionValue;
        if (lower == "easing")
            return CompletionContext.EasingValue;
        if (s_trueOnlyParams.Contains(lower))
            return CompletionContext.TrueOnlyValue;
        if (s_booleanParams.Contains(lower))
            return CompletionContext.BooleanValue;
        return CompletionContext.None;
    }

    /// <summary>检查 text 的最后一个单词是否为 word</summary>
    private static bool IsLastWord(string text, string word)
    {
        if (text.Length < word.Length)
            return false;
        if (text.Length == word.Length)
            return text == word;
        // 前面必须是空格
        return text.EndsWith(word) && text[text.Length - word.Length - 1] == ' ';
    }

    /// <summary>检查是否在参数位置（前面有语句关键字 + 空格）</summary>
    private static bool HasParameterContext(string beforeWord)
    {
        var parts = beforeWord.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        var firstWord = parts[0].ToLowerInvariant();
        return DslKeywords.Statements.Contains(firstWord);
    }

    // ====== 字符串检测 ======

    /// <summary>检测光标是否在未闭合的字符串内</summary>
    private static bool IsInsideString(string lineText, int column)
    {
        bool inString = false;
        for (int i = 0; i < column && i < lineText.Length; i++)
        {
            if (lineText[i] == '"' && (i == 0 || lineText[i - 1] != '\\'))
                inString = !inString;
        }
        return inString;
    }

    /// <summary>获取字符串开始之前的文本</summary>
    private static string GetTextBeforeString(string lineText, int column)
    {
        bool inString = false;
        int stringStart = -1;
        for (int i = 0; i < column && i < lineText.Length; i++)
        {
            if (lineText[i] == '"' && (i == 0 || lineText[i - 1] != '\\'))
            {
                if (!inString)
                {
                    inString = true;
                    stringStart = i;
                }
                else
                {
                    inString = false;
                }
            }
        }
        return inString && stringStart >= 0 ? lineText[..stringStart].TrimEnd() : "";
    }

    // ====== 字符与枚举定义 ======

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
        EnumValue,
        BooleanValue,
        TrueOnlyValue,
        TransitionValue,
        EasingValue,
        None,
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
