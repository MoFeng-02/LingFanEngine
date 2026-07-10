using System.Collections.Generic;
using System.Linq;
using LingFanEngine.DslCore;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Dsl.Analysis;

/// <summary>
/// 遍历 DslStatement 列表提取所有变量
/// </summary>
public static class VariableCollector
{
    /// <summary>收集 DSL 语句列表中所有变量定义和引用</summary>
    public static List<VariableInfo> Collect(IEnumerable<DslStatement> statements)
    {
        var variables = new Dictionary<string, VariableInfo>();

        foreach (var stmt in statements)
        {
            CollectFromStatement(stmt, variables);
        }

        return variables.Values.ToList();
    }

    private static void CollectFromStatement(DslStatement stmt, Dictionary<string, VariableInfo> variables)
    {
        switch (stmt)
        {
            case SetStmt set:
                AddOrUpdate(variables, set.Key, set.ValuePart, stmt.LineNumber);
                break;

            case DefineStmt define:
                AddOrUpdate(variables, define.Key, define.ValuePart, stmt.LineNumber);
                break;

            case LetStmt let:
                AddOrUpdate(variables, let.Key, let.ValuePart, stmt.LineNumber);
                break;

            case SayStmt say:
                // 提取 {variable} 表达式中的变量引用
                CollectExpressions(say.Text, stmt.LineNumber, variables);
                break;

            case IfStmt:
                CollectExpressions(((IfStmt)stmt).Condition, stmt.LineNumber, variables);
                break;

            case ElseIfStmt:
                CollectExpressions(((ElseIfStmt)stmt).Condition, stmt.LineNumber, variables);
                break;

            case WhileStmt:
                CollectExpressions(((WhileStmt)stmt).Condition, stmt.LineNumber, variables);
                break;

            case ForStmt forStmt:
                AddOrUpdate(variables, forStmt.VarName, null, stmt.LineNumber);
                CollectExpressions(forStmt.SourceExpr, stmt.LineNumber, variables);
                break;
        }
    }

    /// <summary>从文本中提取 {variable} 引用</summary>
    private static void CollectExpressions(string text, int line, Dictionary<string, VariableInfo> variables)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '{')
            {
                var end = text.IndexOf('}', i + 1);
                if (end > i)
                {
                    var expr = text.Substring(i + 1, end - i - 1).Trim();
                    // 简单提取变量名（表达式中的第一个标识符）
                    var varName = ExtractVariableName(expr);
                    if (!string.IsNullOrEmpty(varName))
                    {
                        if (variables.TryGetValue(varName, out var info))
                        {
                            info.ReferenceLines.Add(line);
                        }
                        else
                        {
                            var newInfo = new VariableInfo(varName, null, 0, new List<int>());
                            newInfo.ReferenceLines.Add(line);
                            variables[varName] = newInfo;
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

    /// <summary>从表达式中提取变量名（简单实现：取第一个标识符）</summary>
    private static string? ExtractVariableName(string expr)
    {
        var parts = expr.Split([' ', '+', '-', '*', '/', '%', '>', '<', '=', '!', '?', ':', '&', '|'],
            StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : null;
    }

    private static void AddOrUpdate(Dictionary<string, VariableInfo> variables,
        string key, string? value, int line)
    {
        if (variables.TryGetValue(key, out var info))
        {
            // 更新定义行（如果之前只有引用）
            if (info.DefinitionLine == 0)
            {
                var updated = info with { DefinitionLine = line, Value = value };
                variables[key] = updated;
            }
        }
        else
        {
            variables[key] = new VariableInfo(key, value, line, new List<int>());
        }
    }
}
