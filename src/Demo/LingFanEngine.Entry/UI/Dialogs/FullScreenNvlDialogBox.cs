using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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
/// </summary>
public class FullScreenNvlDialogBox : UserControl, IDialogBox
{
    private readonly DialogEngine _engine;
    private readonly TextBlock _contentText;
    private readonly Border _root;
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

        var scroll = new ScrollViewer
        {
            Content = _contentText,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        _root = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            Child = scroll,
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
            else { _state.Set(StateKeys.Dialog.Complete, true); _state.Set(StateKeys.Dialog.WaitingSayComplete, true); }
        };
    }

    public void SetText(string text, string? speaker = null)
    {
        var nvlActive = _state.Get<bool>(StateKeys.Nvl.Active);
        if (nvlActive)
        {
            var oldLen = _engine.SetNvlText(text);
            if (oldLen > 0)
                DialogEngine.ApplyInlineMarkup(_contentText.Inlines!, text[..oldLen]);
            else
                DialogEngine.ApplyInlineMarkup(_contentText.Inlines!, "");
        }
        else
        {
            _engine.SetText(text);
            DialogEngine.ApplyInlineMarkup(_contentText.Inlines!, "");
        }
        _root.IsVisible = true;
    }

    public void Advance(double deltaSeconds)
    {
        if (!_root.IsVisible || IsComplete) return;
        var raw = _engine.Advance(deltaSeconds);
        if (raw != null)
            DialogEngine.ApplyInlineMarkup(_contentText.Inlines!, raw);
    }

    public void SkipToEnd()
    {
        var full = _engine.SkipToEnd();
        DialogEngine.ApplyInlineMarkup(_contentText.Inlines!, full);
    }

    public void Hide() => _root.IsVisible = false;
    public void ResetNvlState() => _engine.ResetNvlState();
    public Control AsControl() => this;
}

/// <summary>全屏 NVL 对话框工厂</summary>
public class FullScreenNvlDialogBoxFactory : IDialogBoxFactory
{
    public IDialogBox Create(IStateContainer state) => new FullScreenNvlDialogBox(state);
}
