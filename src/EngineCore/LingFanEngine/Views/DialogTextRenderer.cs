using Avalonia.Controls.Documents;
using Avalonia.Controls;

namespace LingFanEngine.Views;

/// <summary>
/// 对话文本增量渲染器（B1 提取，2026-09）——按 DialogEngine.Segments 增量构建 Run。
/// <para>打字机推进只增长边界段 Run 的文本，已完整分段原位补全——
/// 不做每帧全量 markup 解析 / Run 重建（NVL 长累积文本的主要开销）。</para>
/// <para>从内置 DialogBox 的私有状态机提取为公共组件，供自定义对话框模板
/// （如 FullScreenNvlDialogBox）复用——模板侧旧式「每帧 ApplyInlineMarkup 全量重建」
/// 是 O(全文) 的，且以可见坐标切片 raw 文本会渲染出错位乱码。</para>
/// <para>线程：仅 UI 线程调用（与控件同线程）。</para>
/// </summary>
public sealed class DialogTextRenderer
{
    private readonly TextBlock _target;

    /// <summary>当前渲染分段表（与 DialogEngine.Segments 同生命周期）</summary>
    private IReadOnlyList<DialogEngine.Segment> _renderSegs = [];
    /// <summary>已完整渲染的分段数（边界段不算）</summary>
    private int _renderedSegs;
    /// <summary>边界段已显示的字符数</summary>
    private int _lastSegShown;
    /// <summary>Inlines 末尾是否为当前边界段的部分 Run（true=原位更新而非追加新 Run）</summary>
    private bool _hasBoundaryRun;

    /// <param name="target">渲染目标文本块（其 Inlines 被 renderer 管理）</param>
    public DialogTextRenderer(TextBlock target)
    {
        _target = target;
        _target.Inlines ??= new InlineCollection();
    }

    /// <summary>重置渲染状态（新文本加载时调用——引擎 SetText/SkipToEnd 后）</summary>
    public void Reset(IReadOnlyList<DialogEngine.Segment> segments)
    {
        _renderSegs = segments;
        _renderedSegs = 0;
        _lastSegShown = 0;
        _hasBoundaryRun = false;
        _target.Inlines!.Clear();
    }

    /// <summary>
    /// 增量渲染到可见字符位置：补全新完成的分段 Run，边界段只更新其 Text。
    /// <para>幂等——重复调用同一位置零开销；只前进不后退（打字机单向）。</para>
    /// </summary>
    public void RenderUpTo(int visibleIndex)
    {
        var inlines = _target.Inlines!;
        var segs = _renderSegs;
        int consumed = 0;
        for (int i = 0; i < segs.Count; i++)
        {
            var seg = segs[i];
            var segEnd = consumed + seg.Text.Length;
            if (segEnd <= visibleIndex)
            {
                // 完整显示段——确保 Run 存在且为全文
                EnsureFullSegment(inlines, i, seg);
                consumed = segEnd;
                continue;
            }
            // 边界段——只显示到 visibleIndex
            var shown = Math.Max(0, Math.Min(seg.Text.Length, visibleIndex - consumed));
            EnsureBoundarySegment(inlines, i, seg, shown);
            return; // 边界段之后的内容尚未到达
        }
    }

    /// <summary>确保第 i 段的完整 Run 已加入（幂等——已完整则跳过；边界 Run 原位补全）</summary>
    private void EnsureFullSegment(InlineCollection inlines, int i, DialogEngine.Segment seg)
    {
        if (i < _renderedSegs) return; // 已渲染过
        if (seg.Text.Length == 0) { _renderedSegs = i + 1; _hasBoundaryRun = false; return; }
        if (_hasBoundaryRun)
        {
            // 该段已有边界 Run（部分文本）——原位补全为整段文本，不新增 Run
            ((Run)inlines[^1]).Text = seg.Text;
        }
        else
        {
            var run = new Run(seg.Text);
            DialogEngine.ApplyStyle(run, seg.Style);
            inlines.Add(run);
        }
        _renderedSegs = i + 1;
        _lastSegShown = seg.Text.Length;
        _hasBoundaryRun = false;
    }

    /// <summary>边界段 Run：已存在则原位更新 Text，不存在则创建（追加新 Run 只发生一次）</summary>
    private void EnsureBoundarySegment(InlineCollection inlines, int i, DialogEngine.Segment seg, int shown)
    {
        // 边界段恒有 i == _renderedSegs（前面的完整段每段推进 _renderedSegs）
        if (_hasBoundaryRun)
        {
            // 已存在的边界 Run——原位更新文本（增量：仅一次小字符串切片）
            if (shown != _lastSegShown && shown > 0)
            {
                var run = (Run)inlines[^1];
                run.Text = seg.Text[..shown];
                _lastSegShown = shown;
            }
        }
        else if (shown > 0)
        {
            // 边界段尚未创建——创建并显示前 shown 个字符
            var run = new Run(seg.Text[..shown]);
            DialogEngine.ApplyStyle(run, seg.Style);
            inlines.Add(run);
            _lastSegShown = shown;
            _hasBoundaryRun = true;
        }
    }
}
