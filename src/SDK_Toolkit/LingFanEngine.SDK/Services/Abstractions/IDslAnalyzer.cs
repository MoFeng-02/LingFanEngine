using System.Collections.Generic;
using System.Threading.Tasks;
using LingFanEngine.SDK.Dsl.Highlight;
using LingFanEngine.SDK.Editor;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Services.Abstractions;

/// <summary>DSL 分析器（编辑时验证，非运行时编译）</summary>
public interface IDslAnalyzer
{
    /// <summary>分析单个源文件</summary>
    Task<DslAnalysisResult> AnalyzeAsync(string source, string filePath);

    /// <summary>分析单个源文件（含跨文件引用检测，P1-1）</summary>
    Task<DslAnalysisResult> AnalyzeWithIndexerAsync(string source, string filePath, DslDefinitionIndexer? indexer);

    /// <summary>分析目录下所有 .story 文件</summary>
    Task<DslAnalysisResult> AnalyzeDirectoryAsync(string directory);

    /// <summary>获取高亮 token 列表</summary>
    List<HighlightToken> GetHighlights(string source);
}
