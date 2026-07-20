using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Views;

namespace _LingFanEngineTemplateTitle_.UI.Dialogs;

/// <summary>
/// 中央气泡对话框模板——圆角白色半透明背景，居中显示
/// <para>适用：内心独白 / OS / 旁白</para>
/// <para>Phase 65：独立 Control，委托 DialogEngine 共享打字机/内联标记逻辑</para>
/// </summary>
public class CenterBubbleDialogBox : UserControl, IDialogBox
{
    private readonly DialogEngine _engine;
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
            if (IsPausedByTag && !IsComplete)
            {
                //_engine.ResumeFromPause();
                SkipToEnd();
                return;
            }
            if (!IsComplete) { SkipToEnd(); }
            else { _state.Set(StateKeys.Dialog.Complete, true); _state.Set(StateKeys.Dialog.WaitingSayComplete, true); }
        };
    }

    public void SetText(string text, string? speaker = null)
    {
        _engine.SetText(text);
        DialogEngine.ApplyInlineMarkup(_contentText.Inlines!, "");
        _bubble.IsVisible = true;
    }

    public void Advance(double deltaSeconds)
    {
        if (!_bubble.IsVisible || IsComplete) return;
        var raw = _engine.Advance(deltaSeconds);
        if (raw != null)
            DialogEngine.ApplyInlineMarkup(_contentText.Inlines!, raw);
    }

    public void SkipToEnd()
    {
        var full = _engine.SkipToEnd();
        DialogEngine.ApplyInlineMarkup(_contentText.Inlines!, full);
    }

    public void Hide() => _bubble.IsVisible = false;
    public void ResetNvlState() => _engine.ResetNvlState();
    public Control AsControl() => this;
}

/// <summary>中央气泡对话框工厂</summary>
public class CenterBubbleDialogBoxFactory : IDialogBoxFactory
{
    public IDialogBox Create(IStateContainer state) => new CenterBubbleDialogBox(state);
}
