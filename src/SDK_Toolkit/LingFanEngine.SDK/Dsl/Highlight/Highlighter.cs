using System.Collections.Generic;
using LingFanEngine.Dsl.LanguageService;
using LingFanEngine.DslCore;
using LingFanEngine.SDK.Dsl.Highlight;

namespace LingFanEngine.SDK.Dsl.Highlight;

/// <summary>
/// 轻量高亮器——作为 <see cref="IDslLanguageService"/> 的前端适配（规划 M3：SDK.Dsl 改为语言服务前端适配层）。
/// <para>不再持有任何词法 / 分类逻辑：token 流与语义分类全部来自语言服务（消费 DslCore 单一真相源），
/// 本类仅把 <see cref="SemanticCategory"/> 映射到 SDK 的 <see cref="HighlightCategory"/>（UI 呈现关切，正确留在 SDK），
/// 并复用 DslCore 的 <see cref="DslInlineTags"/> 做 {var}/{b} 内联标记着色。</para>
/// <para>手写 Lexer（Dsl/Lexer/Lexer.cs）已在 M3 移除——本类不再依赖它。</para>
/// </summary>
public static class Highlighter
{
    private static IDslLanguageService? s_service;

    /// <summary>在组合根（AppHostBuilder.Build）处注入唯一的语言服务实例，确保高亮与编辑器查询共享同一内存索引。</summary>
    public static void Initialize(IDslLanguageService service) => s_service = service;

    /// <summary>从源码生成高亮 token 列表（无 filePath 时使用 scratch 文档键）。</summary>
    public static List<HighlightToken> GetHighlights(string source) => GetHighlights(source, null);

    /// <summary>从源码生成高亮 token 列表，绑定到具体文件（与编辑器查询共享跨文件索引）。</summary>
    public static List<HighlightToken> GetHighlights(string source, string? filePath) => GetHighlights(source, filePath, null);

    /// <summary>从源码生成高亮 token 列表，绑定到具体文件并透传脏区（token 层增量重词法）。</summary>
    public static List<HighlightToken> GetHighlights(string source, string? filePath, DirtyRange? dirty)
    {
        var highlights = new List<HighlightToken>();
        if (string.IsNullOrEmpty(source)) return highlights;

        var svc = s_service;
        if (svc == null) return highlights; // 未初始化（理论上不会发生）

        var key = filePath ?? "__scratch__";
        svc.UpdateDocument(key, source, dirty);

        var tokens = svc.GetSemanticTokens(key);
        var sourceSpan = source.AsSpan();
        foreach (var t in tokens)
        {
            var cat = MapCategory(t.Category, sourceSpan.Slice(t.Offset, t.Length));
            highlights.Add(new HighlightToken(t.Offset, t.Length, cat));
        }

        // {var} 与内联标记 {b}/{color=} 着色（使用 DslCore 单一真相源）
        AddExpressionHighlights(source, highlights);
        return highlights;
    }

    /// <summary>强制同步文档到语言服务索引（折叠/诊断前确保索引为最新文本）。</summary>
    public static void UpdateDocument(string filePath, string text)
        => s_service?.UpdateDocument(filePath, text);

    /// <summary>结构化块折叠区段（来自语言服务单源块结构）。</summary>
    public static IReadOnlyList<(int Start, int End)> GetFoldingRegions(string filePath)
    {
        var svc = s_service;
        if (svc == null) return Array.Empty<(int, int)>();
        return svc.GetFoldingRegions(filePath);
    }

    /// <summary>结构化块嵌套深度（来自语言服务单源块结构）。</summary>
    public static int[] GetLineBlockDepths(string filePath)
    {
        var svc = s_service;
        if (svc == null) return Array.Empty<int>();
        return svc.GetLineBlockDepths(filePath);
    }

    /// <summary>结构化语义 token 列表（来自语言服务，供括号匹配跳过字面量）。</summary>
    public static IReadOnlyList<SemanticToken> GetSemanticTokens(string filePath)
    {
        var svc = s_service;
        if (svc == null) return Array.Empty<SemanticToken>();
        return svc.GetSemanticTokens(filePath);
    }

    /// <summary>语义类别 → SDK 高亮类别。UI 颜色映射在 DslHighlightingTransformer 中完成。</summary>
    private static HighlightCategory MapCategory(SemanticCategory cat, ReadOnlySpan<char> text)
    {
        switch (cat)
        {
            case SemanticCategory.Comment: return HighlightCategory.Comment;
            case SemanticCategory.String:
            {
                var s = text.ToString();
                if (DslFileExtensions.IsPathValue(s)) return HighlightCategory.PathValue;
                if (s.Length >= 1 && s[0] == '#') return HighlightCategory.ColorValue;
                return HighlightCategory.String;
            }
            case SemanticCategory.Number: return HighlightCategory.Number;
            case SemanticCategory.Identifier: return HighlightCategory.Plain;
            case SemanticCategory.Symbol: return HighlightCategory.Symbol;
            case SemanticCategory.Keyword: return HighlightCategory.Keyword;
            case SemanticCategory.ControlFlow: return HighlightCategory.ControlFlow;
            case SemanticCategory.Navigation: return HighlightCategory.Navigation;
            case SemanticCategory.DataOp: return HighlightCategory.DataOp;
            case SemanticCategory.Media: return HighlightCategory.Media;
            case SemanticCategory.Parameter: return HighlightCategory.PropertyName;
            case SemanticCategory.ElementAttribute: return HighlightCategory.PropertyName;
            case SemanticCategory.Literal: return HighlightCategory.PropertyValue;
            case SemanticCategory.UiDisplay: return HighlightCategory.Uielement;
            case SemanticCategory.UiContainer: return HighlightCategory.UiContainer;
            case SemanticCategory.UiInteractive: return HighlightCategory.UiInteractive;
            // 符号名：定义站点着主语句蓝，引用站点着变量绿（与 {var} 一致）
            case SemanticCategory.SymbolDefinition: return HighlightCategory.Keyword;
            case SemanticCategory.SymbolReference: return HighlightCategory.Variable;
            default: return HighlightCategory.Plain;
        }
    }

    private static void AddExpressionHighlights(string source, List<HighlightToken> highlights)
    {
        var i = 0;
        while (i < source.Length)
        {
            if (source[i] == '{')
            {
                var end = source.IndexOf('}', i + 1);
                if (end > i)
                {
                    var content = source.Substring(i + 1, end - i - 1).Trim();
                    if (DslInlineTags.IsInlineTag(content))
                        highlights.Add(new HighlightToken(i, end - i + 1, HighlightCategory.InlineTag));
                    else if (content.Length > 0)
                        highlights.Add(new HighlightToken(i, end - i + 1, HighlightCategory.Variable));
                    i = end + 1;
                }
                else i++;
            }
            else i++;
        }
    }
}
