using LingFanEngine.DslCore;
using LingFanEngine.SDK.Editor;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Dsl.Analysis;

/// <summary>
/// 错误/警告收集器——基于引擎核心的缩进式块结构
/// <para>P1-1: 新增未定义引用检测——未定义的变量/场景/标签/角色/样式引用。</para>
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

        // 检查无法解析的行
        var parsedLineNumbers = new HashSet<int>();
        foreach (var stmt in statements)
            parsedLineNumbers.Add(stmt.LineNumber);

        for (var i = 0; i < rawLines.Count; i++)
        {
            var line = DslCommentHelper.CleanLine(rawLines[i]);
            if (string.IsNullOrEmpty(line))
                continue;

            if (!parsedLineNumbers.Contains(i))
            {
                errors.Add(new DslDiagnostic(i + 1, 1, $"无法解析的语句: {line}", line));
            }
        }

        // 检查缩进式块结构配对
        CheckBlockStructure(rawLines, errors);

        return (errors, warnings);
    }

    /// <summary>收集解析过程中的诊断信息（含跨文件引用检测，P1-1）</summary>
    public static (List<DslDiagnostic> Errors, List<DslDiagnostic> Warnings, List<DslDiagnostic> Infos) CollectWithReferences(
        List<DslStatement> statements,
        List<string> rawLines,
        DslDefinitionIndexer? indexer)
    {
        var (errors, warnings) = Collect(statements, rawLines);
        var infos = new List<DslDiagnostic>();

        if (indexer != null)
        {
            CheckUndefinedReferences(statements, rawLines, indexer, warnings, infos);
        }

        return (errors, warnings, infos);
    }

    /// <summary>检查未定义引用（P1-1）</summary>
    private static void CheckUndefinedReferences(
        List<DslStatement> statements,
        List<string> rawLines,
        DslDefinitionIndexer indexer,
        List<DslDiagnostic> warnings,
        List<DslDiagnostic> infos)
    {
        // 收集当前文件的局部变量定义（变量名 → 定义行号，用于未使用变量波浪线定位）
        var localVars = new Dictionary<string, int>();
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case SetStmt set: localVars[set.Key] = set.LineNumber + 1; break;
                case DefineStmt def: localVars[def.Key] = def.LineNumber + 1; break;
                case LetStmt let: localVars[let.Key] = let.LineNumber + 1; break;
                case ForStmt forStmt: localVars[forStmt.VarName] = forStmt.LineNumber + 1; break;
                case FuncStmt func:
                    foreach (var p in func.Parameters) localVars[p] = func.LineNumber + 1;
                    break;
            }
        }

        // 跨文件变量定义
        var allVars = new HashSet<string>(localVars.Keys);
        foreach (var v in indexer.VariableNames)
            allVars.Add(v);

        // 已知场景/标签/角色/样式
        var knownScenes = new HashSet<string>(indexer.SceneNames);
        var knownLabels = new HashSet<string>(indexer.LabelNames);
        var knownCharacters = new HashSet<string>(indexer.CharacterKeys);
        var knownStyles = new HashSet<string>(indexer.StyleNames);

        foreach (var stmt in statements)
        {
            var lineNum = stmt.LineNumber + 1;
            var lineText = stmt.LineNumber >= 0 && stmt.LineNumber < rawLines.Count
                ? rawLines[stmt.LineNumber].Trim() : "";

            switch (stmt)
            {
                // 未定义场景引用
                case NavigateStmt nav when !knownScenes.Contains(nav.Path):
                    warnings.Add(new DslDiagnostic(lineNum, 1,
                        $"未定义的场景引用: {nav.Path}", lineText, DiagnosticSeverity.Warning));
                    break;

                case SceneStmt scene when !knownScenes.Contains(scene.SceneName):
                    // scene 定义自身——不检查，因为可能正在输入
                    break;

                case CallScreenStmt cs when !knownScenes.Contains(cs.SceneName):
                    warnings.Add(new DslDiagnostic(lineNum, 1,
                        $"未定义的场景引用: {cs.SceneName}", lineText, DiagnosticSeverity.Warning));
                    break;

                // 未定义标签引用
                case JumpStmt jump when !knownLabels.Contains(jump.TargetLabel):
                    warnings.Add(new DslDiagnostic(lineNum, 1,
                        $"未定义的标签引用: {jump.TargetLabel}", lineText, DiagnosticSeverity.Warning));
                    break;

                case CallStmt call when !knownLabels.Contains(call.TargetLabel):
                    warnings.Add(new DslDiagnostic(lineNum, 1,
                        $"未定义的标签引用: {call.TargetLabel}", lineText, DiagnosticSeverity.Warning));
                    break;

                case MenuOptionStmt opt when !knownLabels.Contains(opt.TargetLabel):
                    warnings.Add(new DslDiagnostic(lineNum, 1,
                        $"未定义的标签引用: {opt.TargetLabel}", lineText, DiagnosticSeverity.Warning));
                    break;

                // 角色引用不检查——speaker 可以是任意名字（不要求先 character 定义）

                // 未定义变量引用（在表达式中）
                case SayStmt sayVar:
                    CheckVariableReferences(sayVar.Text, allVars, lineNum, lineText, warnings);
                    break;

                case IfStmt iff:
                    CheckVariableReferences(iff.Condition, allVars, lineNum, lineText, warnings);
                    break;

                case ElseIfStmt elif:
                    CheckVariableReferences(elif.Condition, allVars, lineNum, lineText, warnings);
                    break;

                case WhileStmt wh:
                    CheckVariableReferences(wh.Condition, allVars, lineNum, lineText, warnings);
                    break;

                case ForStmt forStmt:
                    CheckVariableReferences(forStmt.SourceExpr, allVars, lineNum, lineText, warnings);
                    break;

                // 变量赋值右值中的未定义变量引用（P1: 原逻辑漏检 set/define 右值）
                case SetStmt setDef:
                    CheckVariableReferences(setDef.ValuePart, allVars, lineNum, lineText, warnings);
                    break;

                case DefineStmt defDef:
                    CheckVariableReferences(defDef.ValuePart, allVars, lineNum, lineText, warnings);
                    break;
            }
        }

        // 检查未使用的变量（Info 级别）
        // 全文件扫描 {变量} 引用，覆盖 show/transition/sprite/bg/bgm 等所有引用变量的语句，
        // 修正"变量已使用却被误报未使用"的误报（原逻辑仅扫描 6 种语句类型，导致
        // 变量只在 show/transition 等语句中被引用时被判为未使用）。
        var usedVars = new HashSet<string>();
        foreach (var raw in rawLines)
        {
            var cleaned = DslCommentHelper.CleanLine(raw);
            if (!string.IsNullOrEmpty(cleaned))
                CollectVariableNames(cleaned, usedVars);
        }

        // 跨场景：合并索引器收集的所有 .story 文件的 {变量} 引用，
        // 让"变量在另一场景/另一文件被使用"也能被识别，消除跨场景误报未使用。
        if (indexer != null)
        {
            foreach (var v in indexer.UsedVariableNames)
                usedVars.Add(v);
        }

        foreach (var (name, defLine) in localVars)
        {
            if (!usedVars.Contains(name))
            {
                infos.Add(new DslDiagnostic(defLine, 1,
                    $"变量 '{name}' 已定义但未被引用", null, DiagnosticSeverity.Info));
            }
        }
    }

    /// <summary>检查表达式中的变量引用是否已定义</summary>
    private static void CheckVariableReferences(
        string text,
        HashSet<string> definedVars,
        int lineNum,
        string lineText,
        List<DslDiagnostic> warnings)
    {
        if (string.IsNullOrEmpty(text)) return;

        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '{')
            {
                var end = text.IndexOf('}', i + 1);
                if (end > i)
                {
                    var expr = text.Substring(i + 1, end - i - 1).Trim();
                    // 跳过内联标记
                    if (IsInlineTag(expr))
                    {
                        i = end + 1;
                        continue;
                    }

                    var parts = expr.Split([' ', '+', '-', '*', '/', '%', '>', '<', '=', '!', '?', ':', '&', '|', '(', ')'],
                        StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        // 跳过数字和关键字
                        if (double.TryParse(part, out _)) continue;
                        if (part is "true" or "false" or "random" or "min" or "max") continue;

                        if (!definedVars.Contains(part) && IsValidIdentifier(part))
                        {
                            warnings.Add(new DslDiagnostic(lineNum, 1,
                                $"未定义的变量: {part}", lineText, DiagnosticSeverity.Warning));
                        }
                    }
                    i = end + 1;
                }
                else
                {
                    i++;
                }
            }
            else
            {
                i++;
            }
        }
    }

    /// <summary>从表达式中收集变量名</summary>
    private static void CollectVariableNames(string text, HashSet<string> vars)
    {
        if (string.IsNullOrEmpty(text)) return;

        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '{')
            {
                var end = text.IndexOf('}', i + 1);
                if (end > i)
                {
                    var expr = text.Substring(i + 1, end - i - 1).Trim();
                    if (!IsInlineTag(expr))
                    {
                        var parts = expr.Split([' ', '+', '-', '*', '/', '%', '>', '<', '=', '!', '?', ':', '&', '|', '(', ')'],
                            StringSplitOptions.RemoveEmptyEntries);
                        foreach (var part in parts)
                        {
                            if (IsValidIdentifier(part) && !double.TryParse(part, out _))
                                vars.Add(part);
                        }
                    }
                    i = end + 1;
                }
                else
                {
                    i++;
                }
            }
            else
            {
                i++;
            }
        }
    }

    private static bool IsInlineTag(string content)
    {
        var tags = new[] { "b", "/b", "i", "/i", "w", "fast", "p" };
        if (tags.Contains(content)) return true;
        if (content.StartsWith("color=") || content.StartsWith("/color") ||
            content.StartsWith("font=") || content.StartsWith("/font") ||
            content.StartsWith("size=") || content.StartsWith("/size"))
            return true;
        return false;
    }

    private static bool IsValidIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (!char.IsLetter(s[0]) && s[0] != '_') return false;
        foreach (var c in s)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                return false;
        }
        return true;
    }

    /// <summary>使用缩进层级检查 if/else/while/for 块结构</summary>
    private static void CheckBlockStructure(List<string> rawLines, List<DslDiagnostic> errors)
    {
        var blockStack = new Stack<(string Type, int Indent, int Line)>();

        for (var i = 0; i < rawLines.Count; i++)
        {
            var rawLine = rawLines[i];
            var trimmed = DslCommentHelper.StripInlineComment(rawLine.Trim());

            if (string.IsNullOrEmpty(trimmed) || DslCommentHelper.IsCommentLine(trimmed))
                continue;

            var indent = CountIndent(rawLine);
            var firstWord = GetFirstWord(trimmed);

            if (firstWord == "if" || firstWord == "while" || firstWord == "for" ||
                firstWord == "switch" || firstWord == "foreach" || firstWord == "func")
            {
                blockStack.Push((firstWord, indent, i + 1));
            }
            else if (firstWord == "else")
            {
                if (blockStack.Count == 0 || blockStack.Peek().Type != "if")
                {
                    errors.Add(new DslDiagnostic(i + 1, 1,
                        "else 没有对应的 if", trimmed));
                }
            }
            else if (firstWord == "case" || firstWord == "default")
            {
                // switch 的 case/default
            }
            else
            {
                while (blockStack.Count > 0 && indent <= blockStack.Peek().Indent)
                {
                    blockStack.Pop();
                }
            }
        }

        foreach (var (type, _, line) in blockStack)
        {
            errors.Add(new DslDiagnostic(line, 1,
                $"{type} 块可能未正确关闭（缩进式块结构，检查缩进）", null));
        }
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
