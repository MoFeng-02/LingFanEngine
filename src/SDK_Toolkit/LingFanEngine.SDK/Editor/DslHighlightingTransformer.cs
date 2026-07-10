using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Rendering;
using LingFanEngine.SDK.Dsl.Highlight;

namespace LingFanEngine.SDK.Editor;

/// <summary>
/// DSL 语法高亮转换器——将 Highlighter 的 HighlightToken 映射到 AvaloniaEdit 颜色。
/// <para>实现 IVisualLineTransformer，在 AvaloniaEdit 渲染每行时着色。</para>
/// </summary>
public class DslHighlightingTransformer : IVisualLineTransformer
{
    private string _source = "";
    private List<HighlightToken> _tokens = [];
    private bool _dirty = true;

    // 颜色缓存（VS Code Dark+ 风格）
    private static readonly IBrush s_keywordBrush = new SolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush s_stringBrush = new SolidColorBrush(Color.Parse("#CE9178"));
    private static readonly IBrush s_commentBrush = new SolidColorBrush(Color.Parse("#6A9955"));
    private static readonly IBrush s_variableBrush = new SolidColorBrush(Color.Parse("#4EC9B0"));
    private static readonly IBrush s_numberBrush = new SolidColorBrush(Color.Parse("#B5CEA8"));
    private static readonly IBrush s_symbolBrush = new SolidColorBrush(Color.Parse("#D4D4D4"));
    private static readonly IBrush s_labelBrush = new SolidColorBrush(Color.Parse("#DCDCAA"));
    private static readonly IBrush s_plainBrush = new SolidColorBrush(Color.Parse("#D4D4D4"));

    /// <summary>设置源码并重新生成高亮 token</summary>
    public void SetSource(string source)
    {
        _source = source;
        _dirty = true;
    }

    /// <summary>标记需要重新计算（下次渲染时刷新）</summary>
    public void Invalidate()
    {
        _dirty = true;
    }

    private void EnsureTokens()
    {
        if (_dirty)
        {
            _tokens = string.IsNullOrEmpty(_source)
                ? []
                : Highlighter.GetHighlights(_source);
            _dirty = false;
        }
    }

    public void Transform(ITextRunConstructionContext context, IList<VisualLineElement> elements)
    {
        EnsureTokens();
        if (_tokens.Count == 0) return;

        var visualLine = context.VisualLine;
        var lineStartOffset = visualLine.FirstDocumentLine.Offset;
        var lineEndOffset = visualLine.LastDocumentLine.Offset + visualLine.LastDocumentLine.Length;

        // 为当前行构建 offset → color 的映射
        var colorMap = new Dictionary<int, IBrush>(); // key = offset relative to line start

        foreach (var token in _tokens)
        {
            var tokenStart = token.Start;
            var tokenEnd = token.Start + token.Length;

            // 跳过不在当前行的 token
            if (tokenEnd <= lineStartOffset || tokenStart >= lineEndOffset)
                continue;

            // 裁剪到当前行范围内
            var clampedStart = System.Math.Max(tokenStart, lineStartOffset) - lineStartOffset;
            var clampedEnd = System.Math.Min(tokenEnd, lineEndOffset) - lineStartOffset;

            var brush = GetBrush(token.Category);
            if (brush == null) continue;

            // 标记范围内的每个偏移
            for (var i = clampedStart; i < clampedEnd; i++)
            {
                colorMap[i] = brush;
            }
        }

        if (colorMap.Count == 0) return;

        // 对每个元素应用颜色
        foreach (var element in elements)
        {
            if (element is not VisualLineText textElement) continue;

            var elementStart = textElement.RelativeTextOffset;
            var elementEnd = elementStart + textElement.DocumentLength;

            // 查找此元素范围内的颜色
            IBrush? brush = null;
            for (var i = elementStart; i < elementEnd; i++)
            {
                if (colorMap.TryGetValue(i, out var b))
                {
                    brush = b;
                    break;
                }
            }

            if (brush != null)
            {
                // 通过 TextRunProperties 设置颜色
                textElement.TextRunProperties.SetForegroundBrush(brush);
            }
        }
    }

    private static IBrush? GetBrush(HighlightCategory category)
    {
        return category switch
        {
            HighlightCategory.Keyword => s_keywordBrush,
            HighlightCategory.String => s_stringBrush,
            HighlightCategory.Comment => s_commentBrush,
            HighlightCategory.Variable => s_variableBrush,
            HighlightCategory.Number => s_numberBrush,
            HighlightCategory.Symbol => s_symbolBrush,
            HighlightCategory.Label => s_labelBrush,
            _ => s_plainBrush,
        };
    }
}
