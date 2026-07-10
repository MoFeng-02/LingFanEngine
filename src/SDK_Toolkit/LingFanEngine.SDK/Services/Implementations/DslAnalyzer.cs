using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LingFanEngine.DslCore;
using LingFanEngine.SDK.Dsl.Analysis;
using LingFanEngine.SDK.Dsl.Highlight;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>DSL 分析器实现（编辑时验证，非运行时编译）</summary>
public class DslAnalyzer : IDslAnalyzer
{
    /// <inheritdoc/>
    public Task<DslAnalysisResult> AnalyzeAsync(string source, string filePath)
    {
        var sw = Stopwatch.StartNew();

        var statements = ParseSource(source);
        var rawLines = source.Split(["\r\n", "\n", "\r"], System.StringSplitOptions.None).ToList();

        var (errors, warnings) = DiagnosticCollector.Collect(statements, rawLines);
        var variables = VariableCollector.Collect(statements);
        var references = SceneReferenceResolver.Collect(statements, filePath);

        sw.Stop();

        var result = new DslAnalysisResult
        {
            Success = errors.Count == 0,
            Errors = errors,
            Warnings = warnings,
            Variables = variables,
            References = references,
            Ast = statements,
            Elapsed = sw.Elapsed,
            FilePath = filePath,
        };

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public async Task<DslAnalysisResult> AnalyzeDirectoryAsync(string directory)
    {
        var storyFiles = Directory.GetFiles(directory, "*.story", SearchOption.AllDirectories);
        var allStatements = new List<(string FilePath, List<DslStatement> Statements)>();
        var allErrors = new List<DslDiagnostic>();
        var allWarnings = new List<DslDiagnostic>();
        var allVariables = new List<VariableInfo>();
        var sw = Stopwatch.StartNew();

        foreach (var file in storyFiles)
        {
            var source = await File.ReadAllTextAsync(file);
            var statements = ParseSource(source);
            allStatements.Add((file, statements));

            var rawLines = source.Split(["\r\n", "\n", "\r"], System.StringSplitOptions.None).ToList();
            var (errors, warnings) = DiagnosticCollector.Collect(statements, rawLines);
            allErrors.AddRange(errors);
            allWarnings.AddRange(warnings);

            allVariables.AddRange(VariableCollector.Collect(statements));
        }

        var allReferences = SceneReferenceResolver.ResolveAll(allStatements);

        sw.Stop();

        return new DslAnalysisResult
        {
            Success = allErrors.Count == 0,
            Errors = allErrors,
            Warnings = allWarnings,
            Variables = allVariables,
            References = allReferences,
            Elapsed = sw.Elapsed,
            FilePath = directory,
        };
    }

    /// <inheritdoc/>
    public List<HighlightToken> GetHighlights(string source)
    {
        return Highlighter.GetHighlights(source);
    }

    /// <summary>
    /// 使用 DslCore 的 DslStatementParser 逐行解析源码
    /// </summary>
    private static List<DslStatement> ParseSource(string source)
    {
        var statements = new List<DslStatement>();
        var lines = source.Split(["\r\n", "\n", "\r"], System.StringSplitOptions.None);

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var stmt = DslStatementParser.ParseLine(trimmed, i);
            if (stmt != null)
                statements.Add(stmt);
        }

        return statements;
    }
}
