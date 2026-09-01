using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Views;

namespace LingFanEngine.Entry.UI.Dialogs;

/// <summary>
/// 全屏 NVL 对话框模板——全屏半透明背景，顶部累积文本
/// <para>适用：NVL 模式 / 重要剧情</para>
/// <para>Phase 65：独立 Control，委托 DialogEngine 共享打字机/NVL累积/内联标记逻辑</para>
/// <para>2026-09（B1/C4）：改用 DialogTextRenderer 增量渲染（O(新增)），修复三个旧缺陷——
/// ① 每帧 ApplyInlineMarkup 全量重建（O(全文)，长累积文本卡顿）；
/// ② SetNvlText 返回的可见坐标被当 raw 坐标切片（含说话者标签时首帧渲染乱码截断）；
/// ③ NVL 溢出裁剪 SkipHint 不传递（裁剪后追加检测失败，整段文本从零重打——
/// 用户感知为点击推进后约 1 秒延迟才出现新句）。</para>
/// </summary>
public class FullScreenNvlDialogBox : UserControl, IDialogBox
{
    private readonly DialogEngine _engine;
    private readonly DialogTextRenderer _renderer;
    private readonly TextBlock _contentText;
    private readonly Border _root;
    private readonly ScrollViewer _scroll;
    private readonly IStateContainer _state;

    public bool IsComplete => _engine.IsComplete;
    public bool IsPausedByTag => _engine.IsPausedByTag;

    public FullScreenNvlDialogBox(IStateContainer state)
    {
        _state = state;
        _engine = new DialogEngine(state);

        _contentText = new TextBlock
        {
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(40, 30, 40, 40),
            Foreground = Brushes.White
        };
        _renderer = new DialogTextRenderer(_contentText);

        _scroll = new ScrollViewer
        {
            Content = _contentText,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        _root = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            Child = _scroll,
            IsVisible = false,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Content = _root;

        _root.PointerPressed += (_, _) =>
        {
            if (!_root.IsVisible) return;
            if (IsPausedByTag && !IsComplete) { _engine.ResumeFromPause(); return; }
            if (!IsComplete) { SkipToEnd(); }
            else if (_engine.TryConsumeClick())
            {
                _state.Set(StateKeys.Dialog.Complete, true);
                _state.Set(StateKeys.Dialog.WaitingSayComplete, true);
            }
        };
    }

    public void SetText(string text, string? speaker = null)
    {
        var nvlActive = _state.Get<bool>(StateKeys.Nvl.Active);
        if (nvlActive)
        {
            // NVL 累积（含溢出裁剪 SkipHint——与内置 DialogBox 同路径，C4）
            var skipHint = _state.Get<int>(StateKeys.Nvl.SkipHint);
            _state.Set(StateKeys.Nvl.SkipHint, -1);
            _engine.SetNvlText(text, skipHint);
            if (_state.Get<bool>(StateKeys.Rollback.IsReplay))
                _engine.SkipToEnd();
        }
        else
        {
            _engine.SetText(text);
        }

        _renderer.Reset(_engine.Segments);
        _renderer.RenderUpTo(_engine.CharIndex);
        _root.IsVisible = true;
    }

    public void Advance(double deltaSeconds)
    {
        if (!_root.IsVisible || IsComplete) return;
        var before = _engine.CharIndex;
        _engine.Advance(deltaSeconds);
        if (_engine.CharIndex != before)
        {
            _renderer.RenderUpTo(_engine.CharIndex);
            ScrollToLatest();
        }
    }

    public void SkipToEnd()
    {
        _engine.SkipToEnd();
        _renderer.RenderUpTo(_engine.CharIndex);
        ScrollToLatest();
    }

    public void Hide() => _root.IsVisible = false;
    public void ResetNvlState() => _engine.ResetNvlState();
    public bool TryConsumeClick() => _engine.TryConsumeClick();
    public Control AsControl() => this;

    /// <summary>滚动到最新行（NVL 追加新句后保证可见——对标 Ren'Py NVL 滚动）</summary>
    private void ScrollToLatest()
    {
        if (_scroll.Extent.Height > _scroll.Viewport.Height)
            _scroll.ScrollToEnd();
    }
}

/// <summary>全屏 NVL 对话框工厂</summary>
public class FullScreenNvlDialogBoxFactory : IDialogBoxFactory
{
    public IDialogBox Create(IStateContainer state) => new FullScreenNvlDialogBox(state);
}
