using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using DslDiagnostic = LingFanEngine.SDK.Models.DslDiagnostic;

namespace LingFanEngine.SDK.Editor;

/// <summary>
/// DSL 文本标记服务——在编辑器中绘制错误/警告波浪线。
/// <para>实现 IBackgroundRenderer，在 AvaloniaEdit 渲染背景层时绘制波浪线。</para>
/// </summary>
public class DslTextMarkerService : IBackgroundRenderer
{
    private readonly TextView _textView;
    private readonly List<TextMarker> _markers = [];

    // 颜色
    private static readonly IBrush s_errorBrush = new SolidColorBrush(Color.Parse("#F44747"));
    private static readonly IBrush s_warningBrush = new SolidColorBrush(Color.Parse("#CCA700"));

    public DslTextMarkerService(TextView textView)
    {
        _textView = textView;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
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

            DrawSquiggle(drawingContext, startX, y, endX, marker.IsError ? s_errorBrush : s_warningBrush);
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
                _markers.Add(new TextMarker(line, err.Column, length, err.Message, true));
            }
        }

        foreach (var warn in warnings)
        {
            var (line, length) = GetMarkerRange(warn, document);
            if (line > 0)
            {
                _markers.Add(new TextMarker(line, warn.Column, length, warn.Message, false));
            }
        }
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
internal record TextMarker(int Line, int Column, int Length, string Message, bool IsError);
