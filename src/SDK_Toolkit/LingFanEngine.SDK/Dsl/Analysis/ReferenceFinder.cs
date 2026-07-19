using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LingFanEngine.DslCore;
using LingFanEngine.SDK.Dsl;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Dsl.Analysis;

/// <summary>
/// 全项目引用查找器（P0-4 Find All References）
/// <para>遍历 Stories/ 目录所有 .story 文件，收集指定符号的所有引用位置。</para>
/// </summary>
public static class ReferenceFinder
{
    /// <summary>查找指定符号在项目中的所有引用</summary>
    /// <param name="storiesDirectory">Stories 目录路径</param>
    /// <param name="symbolName">符号名称</param>
    /// <param name="kind">引用类型（决定搜索策略）</param>
    public static async Task<List<ReferenceResult>> FindReferencesAsync(
        string storiesDirectory,
        string symbolName,
        ReferenceKind kind)
    {
        var results = new List<ReferenceResult>();

        if (!Directory.Exists(storiesDirectory) || string.IsNullOrEmpty(symbolName))
            return results;

        var storyFiles = Directory.GetFiles(storiesDirectory, "*.story", SearchOption.AllDirectories);

        foreach (var file in storyFiles)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];

                    // 跳过注释和空行（支持 # 和 // 注释）
                    var cleaned = DslCommentHelper.CleanLine(line);
                    if (string.IsNullOrEmpty(cleaned))
                        continue;

                    if (MatchesReference(cleaned, symbolName, kind))
                    {
                        var column = FindColumn(cleaned, symbolName);
                        results.Add(new ReferenceResult(file, i + 1, column, cleaned, kind));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReferenceFinder] 文件读取错误: {ex.Message}");
            }
        }

        return results;
    }

    /// <summary>根据 DSL 语句列表查找引用（单文件内存模式）</summary>
    public static List<ReferenceResult> FindReferencesInStatements(
        List<DslStatement> statements,
        List<string> rawLines,
        string filePath,
        string symbolName,
        ReferenceKind kind)
    {
        var results = new List<ReferenceResult>();

        foreach (var stmt in statements)
        {
            if (MatchesStatement(stmt, symbolName, kind))
            {
                var lineNum = stmt.LineNumber + 1;
                var lineText = lineNum >= 1 && lineNum <= rawLines.Count
                    ? rawLines[lineNum - 1].Trim()
                    : "";
                results.Add(new ReferenceResult(filePath, lineNum, 1, lineText, kind));
            }
        }

        return results;
    }

    /// <summary>检查行文本是否引用了指定符号</summary>
    private static bool MatchesReference(string lineText, string symbolName, ReferenceKind kind)
    {
        return kind switch
        {
            ReferenceKind.Variable => ContainsVariableReference(lineText, symbolName),
            ReferenceKind.Scene => ContainsSceneReference(lineText, symbolName),
            ReferenceKind.Label => ContainsLabelReference(lineText, symbolName),
            ReferenceKind.Character => ContainsCharacterReference(lineText, symbolName),
            ReferenceKind.Style => ContainsStyleReference(lineText, symbolName),
            ReferenceKind.Function => ContainsFunctionReference(lineText, symbolName),
            ReferenceKind.Sprite => ContainsSpriteReference(lineText, symbolName),
            _ => false,
        };
    }

    /// <summary>检查 DSL 语句是否引用了指定符号</summary>
    private static bool MatchesStatement(DslStatement stmt, string symbolName, ReferenceKind kind)
    {
        return kind switch
        {
            ReferenceKind.Variable => stmt switch
            {
                SayStmt say => ContainsVariableReference(say.Text, symbolName),
                IfStmt iff => ContainsVariableReference(iff.Condition, symbolName),
                ElseIfStmt elif => ContainsVariableReference(elif.Condition, symbolName),
                WhileStmt wh => ContainsVariableReference(wh.Condition, symbolName),
                ForStmt forStmt => ContainsVariableReference(forStmt.SourceExpr, symbolName),
                SetStmt set => set.Key == symbolName || ContainsVariableReference(set.ValuePart, symbolName),
                DefineStmt def => def.Key == symbolName || ContainsVariableReference(def.ValuePart, symbolName),
                LetStmt let => let.Key == symbolName || ContainsVariableReference(let.ValuePart, symbolName),
                _ => false,
            },
            ReferenceKind.Scene => stmt switch
            {
                NavigateStmt nav => nav.Path == symbolName,
                SceneStmt scene => scene.SceneName == symbolName,
                CallScreenStmt cs => cs.SceneName == symbolName,
                _ => false,
            },
            ReferenceKind.Label => stmt switch
            {
                JumpStmt jump => jump.TargetLabel == symbolName,
                CallStmt call => call.TargetLabel == symbolName,
                MenuOptionStmt opt => opt.TargetLabel == symbolName,
                _ => false,
            },
            ReferenceKind.Character => stmt switch
            {
                SayStmt say => say.Speaker == symbolName,
                _ => false,
            },
            ReferenceKind.Style => stmt switch
            {
                ShowElementStmt elem => elem.Element.Properties.TryGetValue("class", out var cls) && cls.ToString() == symbolName,
                _ => false,
            },
            ReferenceKind.Function => stmt switch
            {
                _ => false,
            },
            ReferenceKind.Sprite => stmt switch
            {
                SpriteStateStmt ss => ss.Id == symbolName,
                SpriteMoveStmt sm => sm.Id == symbolName,
                SpriteHideStmt sh => sh.Id == symbolName,
                _ => false,
            },
            _ => false,
        };
    }

    private static bool ContainsVariableReference(string text, string varName)
    {
        if (string.IsNullOrEmpty(text)) return false;
        // 检查 {varName} 或 {varName ...} 模式
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '{')
            {
                var end = text.IndexOf('}', i + 1);
                if (end > i)
                {
                    var expr = text.Substring(i + 1, end - i - 1).Trim();
                    // 分割运算符，检查是否包含 varName
                    var parts = expr.Split([' ', '+', '-', '*', '/', '%', '>', '<', '=', '!', '?', ':', '&', '|', '(', ')'],
                        StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Contains(varName))
                        return true;
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
        return false;
    }

    private static bool ContainsSceneReference(string line, string sceneName)
    {
        var lower = line.ToLowerInvariant();
        // navigate "sceneName" 或 scene "sceneName" 或 call_screen "sceneName"
        if (lower.StartsWith("navigate ") && line.Contains($"\"{sceneName}\""))
            return true;
        if (lower.StartsWith("scene ") && line.Contains($"\"{sceneName}\""))
            return true;
        if (lower.StartsWith("call_screen ") && line.Contains($"\"{sceneName}\""))
            return true;
        return false;
    }

    private static bool ContainsLabelReference(string line, string labelName)
    {
        var lower = line.ToLowerInvariant();
        // jump labelName 或 call labelName
        if (lower.StartsWith("jump ") && line.Contains(labelName))
            return true;
        if (lower.StartsWith("call ") && line.Contains(labelName))
            return true;
        // menu 选项 "text" -> labelName
        if (line.Contains("->") && line.Contains(labelName))
            return true;
        return false;
    }

    private static bool ContainsCharacterReference(string line, string charKey)
    {
        // speaker="charKey" 或 by "charKey"
        if (line.Contains($"speaker=\"{charKey}\""))
            return true;
        if (line.Contains($"by \"{charKey}\""))
            return true;
        return false;
    }

    private static bool ContainsStyleReference(string line, string styleName)
    {
        // class="styleName" 或 style="styleName"
        if (line.Contains($"class=\"{styleName}\""))
            return true;
        if (line.Contains($"style=\"{styleName}\""))
            return true;
        return false;
    }

    private static bool ContainsFunctionReference(string line, string funcName)
    {
        // funcName(  调用
        if (line.Contains($"{funcName}("))
            return true;
        return false;
    }

    private static bool ContainsSpriteReference(string line, string spriteId)
    {
        var lower = line.ToLowerInvariant();
        // sprite_state "id" / sprite_move "id" / sprite_hide "id"
        if ((lower.StartsWith("sprite_state ") || lower.StartsWith("sprite_move ") || lower.StartsWith("sprite_hide "))
            && line.Contains($"\"{spriteId}\""))
            return true;
        return false;
    }

    /// <summary>查找符号在行中的列位置</summary>
    private static int FindColumn(string line, string symbol)
    {
        var idx = line.IndexOf(symbol);
        return idx >= 0 ? idx + 1 : 1;
    }
}
