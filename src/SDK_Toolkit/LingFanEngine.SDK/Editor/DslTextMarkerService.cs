using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using DslDiagnostic = LingFanEngine.SDK.Models.DslDiagnostic;

namespace LingFanEngine.SDK.Editor;

/// <summary>
/// DSL 文本标记服务——在编辑器中绘制错误/警告/信息波浪线 + 括号匹配高亮。
/// <para>实现 IBackgroundRenderer，在 AvaloniaEdit 渲染背景层时绘制。</para>
/// <para>P1-1: 新增 Info 级别颜色。P1-3: 新增括号匹配背景高亮。</para>
/// </summary>
public class DslTextMarkerService : IBackgroundRenderer
{
    private readonly TextView _textView;
    private readonly List<TextMarker> _markers = [];
    private readonly List<HighlightMarker> _highlightMarkers = [];

    // 颜色
    private static readonly IBrush s_errorBrush = new SolidColorBrush(Color.Parse("#F44747"));
    private static readonly IBrush s_warningBrush = new SolidColorBrush(Color.Parse("#CCA700"));
    private static readonly IBrush s_infoBrush = new SolidColorBrush(Color.Parse("#75BEFF"));

    // P1-3: 括号匹配高亮颜色
    private static readonly IBrush s_bracketMatchBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 0));

    public DslTextMarkerService(TextView textView)
    {
        _textView = textView;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        // 绘制括号匹配高亮（在波浪线之前绘制，作为背景）
        foreach (var hl in _highlightMarkers)
        {
            var visualLine = textView.GetVisualLine(hl.Line);
            if (visualLine == null) continue;

            var lineStartOffset = visualLine.FirstDocumentLine.Offset;
            var startOffset = lineStartOffset + hl.Column - 1;
            var endOffset = lineStartOffset + hl.Column - 1 + hl.Length;

            var startX = visualLine.GetVisualPosition(startOffset, VisualYPosition.TextTop).X;
            var endX = visualLine.GetVisualPosition(endOffset, VisualYPosition.TextTop).X;
            var yTop = visualLine.GetVisualPosition(startOffset, VisualYPosition.TextTop).Y;
            var height = visualLine.Height;

            if (endX > startX)
            {
                drawingContext.DrawRectangle(s_bracketMatchBrush, null, new Rect(startX, yTop, endX - startX, height), 2, 2);
            }
        }

        // 绘制波浪线
        foreach (var marker in _markers)
        {
            var visualLine = textView.GetVisualLine(marker.Line);
            if (visualLine == null) continue;

            // 获取该行的像素区域
            var yPos = visualLine.VisualTop;
            var lineHeight = visualLine.Height;
            var y = yPos + lineHeight - 2; // 波浪线在文字底部

            // 获取行起始和结束的 x 坐标
            var lineStartOffset = visualLine.FirstDocumentLine.Offset;
            var columnOffset = System.Math.Max(0, marker.Column - 1);
            var startOffset = lineStartOffset + columnOffset;
            var endOffset = lineStartOffset + columnOffset + marker.Length;

            var startX = visualLine.GetVisualPosition(startOffset, VisualYPosition.TextTop).X;
            var endX = visualLine.GetVisualPosition(endOffset, VisualYPosition.TextTop).X;

            if (endX <= startX)
            {
                startX = 0;
                endX = textView.Bounds.Width;
            }

            var brush = marker.Severity switch
            {
                Models.DiagnosticSeverity.Error => s_errorBrush,
                Models.DiagnosticSeverity.Warning => s_warningBrush,
                _ => s_infoBrush,
            };

            DrawSquiggle(drawingContext, startX, y, endX, brush);
        }
    }

    private static void DrawSquiggle(DrawingContext context, double startX, double y, double endX, IBrush brush)
    {
        if (endX <= startX) return;

        var pen = new Pen(brush, 1);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Avalonia.Point(startX, y), false);
            var x = startX;
            var toggle = false;
            while (x < endX)
            {
                x += 3;
                var ny = toggle ? y + 2 : y;
                ctx.LineTo(new Avalonia.Point(System.Math.Min(x, endX), ny));
                toggle = !toggle;
            }
            ctx.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }

    /// <summary>更新所有标记</summary>
    public void UpdateMarkers(
        List<DslDiagnostic> errors,
        List<DslDiagnostic> warnings,
        TextDocument document)
    {
        _markers.Clear();

        foreach (var err in errors)
        {
            var (line, length) = GetMarkerRange(err, document);
            if (line > 0)
            {
                _markers.Add(new TextMarker(line, err.Column, length, err.Message, Models.DiagnosticSeverity.Error));
            }
        }

        foreach (var warn in warnings)
        {
            var (line, length) = GetMarkerRange(warn, document);
            if (line > 0)
            {
                _markers.Add(new TextMarker(line, warn.Column, length, warn.Message, Models.DiagnosticSeverity.Warning));
            }
        }
    }

    /// <summary>更新所有标记（含 Info 级别，P1-1）</summary>
    public void UpdateMarkersWithInfo(
        List<DslDiagnostic> errors,
        List<DslDiagnostic> warnings,
        List<DslDiagnostic> infos,
        TextDocument document)
    {
        _markers.Clear();

        foreach (var err in errors)
        {
            var (line, length) = GetMarkerRange(err, document);
            if (line > 0)
                _markers.Add(new TextMarker(line, err.Column, length, err.Message, Models.DiagnosticSeverity.Error));
        }

        foreach (var warn in warnings)
        {
            var (line, length) = GetMarkerRange(warn, document);
            if (line > 0)
                _markers.Add(new TextMarker(line, warn.Column, length, warn.Message, Models.DiagnosticSeverity.Warning));
        }

        foreach (var info in infos)
        {
            var (line, length) = GetMarkerRange(info, document);
            if (line > 0)
                _markers.Add(new TextMarker(line, info.Column, length, info.Message, Models.DiagnosticSeverity.Info));
        }
    }

    /// <summary>设置括号匹配高亮（P1-3）</summary>
    public void SetBracketHighlight(int line1, int column1, int length1, int line2, int column2, int length2)
    {
        _highlightMarkers.Clear();
        _highlightMarkers.Add(new HighlightMarker(line1, column1, length1));
        _highlightMarkers.Add(new HighlightMarker(line2, column2, length2));
    }

    /// <summary>清除括号匹配高亮（P1-3）</summary>
    public void ClearBracketHighlight()
    {
        _highlightMarkers.Clear();
    }

    private static (int Line, int Length) GetMarkerRange(DslDiagnostic diag, TextDocument document)
    {
        var line = diag.Line;
        if (line < 1 || line > document.LineCount)
            return (0, 0);

        var lineText = document.GetText(document.GetLineByNumber(line));
        var length = string.IsNullOrEmpty(diag.SourceLine)
            ? lineText.Length
            : diag.SourceLine.Trim().Length;

        return (line, System.Math.Max(1, length));
    }

    public KnownLayer Layer => KnownLayer.Background;
}

/// <summary>文本标记（波浪线）数据</summary>
internal record TextMarker(int Line, int Column, int Length, string Message, Models.DiagnosticSeverity Severity);

/// <summary>高亮标记（括号匹配背景高亮，P1-3）</summary>
internal record HighlightMarker(int Line, int Column, int Length);
