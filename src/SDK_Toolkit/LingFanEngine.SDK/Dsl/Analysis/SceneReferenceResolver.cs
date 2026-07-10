using System.Collections.Generic;
using System.Linq;
using LingFanEngine.DslCore;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Dsl.Analysis;

/// <summary>
/// 跨文件场景/标签引用解析
/// </summary>
public static class SceneReferenceResolver
{
    /// <summary>遍历 DSL 语句列表找所有场景/标签引用</summary>
    public static List<SceneReference> Collect(IEnumerable<DslStatement> statements, string filePath)
    {
        var references = new List<SceneReference>();

        foreach (var stmt in statements)
        {
            CollectFromStatement(stmt, filePath, references);
        }

        return references;
    }

    private static void CollectFromStatement(DslStatement stmt, string filePath, List<SceneReference> references)
    {
        switch (stmt)
        {
            case NavigateStmt nav:
                references.Add(new SceneReference(filePath, stmt.LineNumber, nav.Path, nav.SceneName,
                    ReferenceType.Navigate));
                break;

            case JumpStmt jump:
                references.Add(new SceneReference(filePath, stmt.LineNumber, "", jump.TargetLabel,
                    ReferenceType.Jump));
                break;

            case CallStmt call:
                references.Add(new SceneReference(filePath, stmt.LineNumber, "", call.TargetLabel,
                    ReferenceType.Call));
                break;

            case SceneStmt scene:
                references.Add(new SceneReference(filePath, stmt.LineNumber, scene.SceneName, null,
                    ReferenceType.Scene));
                break;

            case CallScreenStmt callScreen:
                references.Add(new SceneReference(filePath, stmt.LineNumber, callScreen.SceneName, null,
                    ReferenceType.Navigate));
                break;

            case MenuOptionStmt menuOpt:
                references.Add(new SceneReference(filePath, stmt.LineNumber, "", menuOpt.TargetLabel,
                    ReferenceType.Jump));
                break;
        }
    }

    /// <summary>
    /// 跨文件解析：将引用与目录中其他 .story 文件的场景定义交叉引用
    /// </summary>
    public static List<SceneReference> ResolveAll(
        IEnumerable<(string FilePath, List<DslStatement> Statements)> allFiles)
    {
        var allReferences = new List<SceneReference>();

        foreach (var (filePath, statements) in allFiles)
        {
            allReferences.AddRange(Collect(statements, filePath));
        }

        return allReferences;
    }
}
