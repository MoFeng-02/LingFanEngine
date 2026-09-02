using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Views;

namespace LingFanEngine.Entry.UI.Dialogs;

/// <summary>
/// 中央气泡对话框模板——圆角半透明背景，居中显示
/// <para>适用：内心独白 / OS / 旁白</para>
/// <para>Phase 65：独立 Control，委托 DialogEngine 共享打字机/内联标记逻辑</para>
/// <para>2026-09（B1/C4/C5 对齐）：改用 DialogTextRenderer 增量渲染（O(新增)），修复四个旧缺陷——
/// ① 每帧 ApplyInlineMarkup 全量重建（O(全文)）；
/// ② NVL 路径缺失（NVL 模式下 template="center" 会丢失累积语义——教程约定非 fullscreen
///    模板在 NVL 下也应累积显示）；
/// ③ TryConsumeClick 未委托（接口默认恒 true，防重入闸门失效——完成后的穿透点击重复触发 Complete）；
/// ④ {w}/{p} 暂停时点击 SkipToEnd 而非 ResumeFromPause（与内置/全屏模板行为不一致）。</para>
/// </summary>
public class CenterBubbleDialogBox : UserControl, IDialogBox
{
    private readonly DialogEngine _engine;
    private readonly DialogTextRenderer _renderer;
    private readonly TextBlock _contentText;
    private readonly Border _bubble;
    private readonly IStateContainer _state;

    public bool IsComplete => _engine.IsComplete;
    public bool IsPausedByTag => _engine.IsPausedByTag;

    public CenterBubbleDialogBox(IStateContainer state)
    {
        _state = state;
        _engine = new DialogEngine(state);

        _contentText = new TextBlock
        {
            FontSize = 20,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(24, 16, 24, 16),
            Foreground = Brushes.White
        };
        _renderer = new DialogTextRenderer(_contentText);

        _bubble = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(224, 30, 30, 40)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(4),
            Child = _contentText,
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 600
        };
        Content = _bubble;

        _bubble.PointerPressed += (_, _) =>
        {
            if (!_bubble.IsVisible) return;
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
        if (_state.Get<bool>(StateKeys.Nvl.Active))
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
        _bubble.IsVisible = true;
    }

    public void Advance(double deltaSeconds)
    {
        if (!_bubble.IsVisible || IsComplete) return;
        var before = _engine.CharIndex;
        _engine.Advance(deltaSeconds);
        if (_engine.CharIndex != before)
            _renderer.RenderUpTo(_engine.CharIndex);
    }

    public void SkipToEnd()
    {
        _engine.SkipToEnd();
        _renderer.RenderUpTo(_engine.CharIndex);
    }

    public void Hide() => _bubble.IsVisible = false;
    public void ResetNvlState() => _engine.ResetNvlState();
    public bool TryConsumeClick() => _engine.TryConsumeClick();
    public Control AsControl() => this;
}

/// <summary>中央气泡对话框工厂</summary>
public class CenterBubbleDialogBoxFactory : IDialogBoxFactory
{
    public IDialogBox Create(IStateContainer state) => new CenterBubbleDialogBox(state);
}
