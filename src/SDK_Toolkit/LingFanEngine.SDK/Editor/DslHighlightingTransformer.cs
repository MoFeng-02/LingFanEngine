using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Rendering;
using LingFanEngine.SDK.Dsl.Highlight;

namespace LingFanEngine.SDK.Editor;

/// <summary>
/// DSL 语法高亮转换器——将 Highlighter 的 HighlightToken 映射到 AvaloniaEdit 颜色。
/// <para>实现 IVisualLineTransformer，在 AvaloniaEdit 渲染每行时着色。</para>
/// <para>使用防抖机制：源码变化后延迟 150ms 再重新分词，避免每次按键都全量解析。</para>
/// </summary>
public class DslHighlightingTransformer : IVisualLineTransformer
{
    private string _source = "";
    private List<HighlightToken> _tokens = [];
    private bool _dirty = true;
    private long _lastSetTime;  // Stopwatch.GetTimestamp() of last SetSource call
    private const int DebounceMs = 150;

    // 颜色缓存（VS Code Dark+ 风格）
    private static readonly IBrush s_keywordBrush = new SolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush s_stringBrush = new SolidColorBrush(Color.Parse("#CE9178"));
    private static readonly IBrush s_commentBrush = new SolidColorBrush(Color.Parse("#6A9955"));
    private static readonly IBrush s_variableBrush = new SolidColorBrush(Color.Parse("#4EC9B0"));
    private static readonly IBrush s_numberBrush = new SolidColorBrush(Color.Parse("#B5CEA8"));
    private static readonly IBrush s_symbolBrush = new SolidColorBrush(Color.Parse("#D4D4D4"));
    private static readonly IBrush s_labelBrush = new SolidColorBrush(Color.Parse("#DCDCAA"));
    private static readonly IBrush s_plainBrush = new SolidColorBrush(Color.Parse("#D4D4D4"));

    // P0-1 精细分类颜色
    private static readonly IBrush s_styleNameBrush = new SolidColorBrush(Color.Parse("#DCDCAA"));     // 黄褐
    private static readonly IBrush s_characterNameBrush = new SolidColorBrush(Color.Parse("#4EC9B0")); // 青绿
    private static readonly IBrush s_propertyNameBrush = new SolidColorBrush(Color.Parse("#9CDCFE"));  // 浅蓝
    private static readonly IBrush s_propertyValueBrush = new SolidColorBrush(Color.Parse("#CE9178")); // 橙褐
    private static readonly IBrush s_colorValueBrush = new SolidColorBrush(Color.Parse("#CE9178"));    // 橙褐
    private static readonly IBrush s_pathValueBrush = new SolidColorBrush(Color.Parse("#CE9178"));     // 橙褐
    private static readonly IBrush s_sceneNameBrush = new SolidColorBrush(Color.Parse("#DCDCAA"));     // 黄褐
    private static readonly IBrush s_inlineTagBrush = new SolidColorBrush(Color.Parse("#569CD6"));     // 蓝色
    private static readonly IBrush s_functionBrush = new SolidColorBrush(Color.Parse("#DCDCAA"));      // 黄褐
    private static readonly IBrush s_operatorBrush = new SolidColorBrush(Color.Parse("#D4D4D4"));      // 浅灰
    private static readonly IBrush s_infoBrush = new SolidColorBrush(Color.Parse("#75BEFF"));          // 蓝色

    /// <summary>设置源码并标记需要重新分词</summary>
    public void SetSource(string source)
    {
        _source = source;
        _lastSetTime = Stopwatch.GetTimestamp();
        _dirty = true;
    }

    /// <summary>标记需要重新计算（下次渲染时刷新）</summary>
    public void Invalidate()
    {
        _dirty = true;
    }

    private void EnsureTokens()
    {
        if (!_dirty) return;
        // 防抖：距离上次 SetSource 不足 150ms 则跳过（用旧 token 渲染）
        var elapsed = Stopwatch.GetElapsedTime(_lastSetTime, Stopwatch.GetTimestamp());
        if (elapsed.TotalMilliseconds < DebounceMs) return;

        _tokens = string.IsNullOrEmpty(_source)
            ? []
            : Highlighter.GetHighlights(_source);
        _dirty = false;
    }

    public void Transform(ITextRunConstructionContext context, IList<VisualLineElement> elements)
    {
        EnsureTokens();
        if (_tokens.Count == 0) return;

        var visualLine = context.VisualLine;
        var lineStartOffset = visualLine.FirstDocumentLine.Offset;
        var lineEndOffset = visualLine.LastDocumentLine.Offset + visualLine.LastDocumentLine.Length;

        // 为当前行构建 offset → color 的段映射
        var segments = new List<(int Start, int End, IBrush Brush)>();

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

            segments.Add((clampedStart, clampedEnd, brush));
        }

        if (segments.Count == 0) return;

        // 对每个元素应用颜色
        foreach (var element in elements)
        {
            if (element is not VisualLineText textElement) continue;

            var elementStart = textElement.RelativeTextOffset;
            var elementEnd = elementStart + textElement.DocumentLength;

            // 查找此元素范围内的颜色——优先使用第一个匹配的段
            IBrush? brush = null;
            foreach (var (segStart, segEnd, segBrush) in segments)
            {
                if (segEnd > elementStart && segStart < elementEnd)
                {
                    brush = segBrush;
                    break;
                }
            }

            if (brush != null)
            {
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
            HighlightCategory.StyleName => s_styleNameBrush,
            HighlightCategory.CharacterName => s_characterNameBrush,
            HighlightCategory.PropertyName => s_propertyNameBrush,
            HighlightCategory.PropertyValue => s_propertyValueBrush,
            HighlightCategory.ColorValue => s_colorValueBrush,
            HighlightCategory.PathValue => s_pathValueBrush,
            HighlightCategory.SceneName => s_sceneNameBrush,
            HighlightCategory.InlineTag => s_inlineTagBrush,
            HighlightCategory.Function => s_functionBrush,
            HighlightCategory.Operator => s_operatorBrush,
            HighlightCategory.Info => s_infoBrush,
            _ => s_plainBrush,
        };
    }
}
