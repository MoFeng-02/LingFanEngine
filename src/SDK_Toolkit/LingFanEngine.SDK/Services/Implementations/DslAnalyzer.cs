using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LingFanEngine.DslCore;
using LingFanEngine.SDK.Dsl;
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
        return Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();

            var statements = ParseSource(source);
            var rawLines = source.Split(["\r\n", "\n", "\r"], System.StringSplitOptions.None).ToList();

            var (errors, warnings) = DiagnosticCollector.Collect(statements, rawLines);
            var variables = VariableCollector.Collect(statements);
            var references = SceneReferenceResolver.Collect(statements, filePath);

            sw.Stop();

            return new DslAnalysisResult
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
        });
    }

    /// <summary>分析单个源文件（含跨文件引用检测，P1-1）</summary>
    public Task<DslAnalysisResult> AnalyzeWithIndexerAsync(string source, string filePath, Editor.DslDefinitionIndexer? indexer)
    {
        return Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();

            var statements = ParseSource(source);
            var rawLines = source.Split(["\r\n", "\n", "\r"], System.StringSplitOptions.None).ToList();

            var (errors, warnings, infos) = DiagnosticCollector.CollectWithReferences(statements, rawLines, indexer);
            var variables = VariableCollector.Collect(statements);
            var references = SceneReferenceResolver.Collect(statements, filePath);

            sw.Stop();

            return new DslAnalysisResult
            {
                Success = errors.Count == 0,
                Errors = errors,
                Warnings = warnings,
                Infos = infos,
                Variables = variables,
                References = references,
                Ast = statements,
                Elapsed = sw.Elapsed,
                FilePath = filePath,
            };
        });
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
            // 跳过空行和注释行
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var trimmed = DslCommentHelper.StripInlineComment(line.Trim());
            if (string.IsNullOrEmpty(trimmed) || DslCommentHelper.IsCommentLine(trimmed))
                continue;

            try
            {
                var stmt = DslStatementParser.ParseLine(trimmed, i);
                if (stmt != null)
                    statements.Add(stmt);
            }
            catch
            {
                // 解析失败的行跳过——诊断收集器会报告语法错误
            }
        }

        return statements;
    }
}
