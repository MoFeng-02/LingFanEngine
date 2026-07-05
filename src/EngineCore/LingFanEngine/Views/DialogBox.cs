using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Views;

public class DialogBox : UserControl
{
    private readonly TextBlock _speakerText;
    private readonly TextBlock _contentText;
    private readonly Border _root;
    private readonly Image _bgImage;
    private readonly IStateContainer _state;
    private string _fullText = "";
    private int _charIndex;
    private double _typeTimer;

    public double TypeSpeed { get; set; } = 60;
    public bool IsComplete => _charIndex >= _fullText.Length;
    public bool IsPausedByTag { get; private set; }

    private string? _bgPath; // 当前背景图路径

    public string? BackgroundImage
    {
        get => _bgPath;
        set
        {
            _bgPath = value;
            if (value != null)
                _ = Task.Run(() =>
                {
                    try { Avalonia.Threading.Dispatcher.UIThread.Invoke(() => _bgImage.Source = new Avalonia.Media.Imaging.Bitmap(value)); }
                    catch { }
                });
        }
    }

    public DialogBox(IStateContainer state)
    {
        _state = state;
        _speakerText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 200, 100)),
            FontSize = 16, FontWeight = FontWeight.Bold,
            Margin = new Thickness(10, 6, 10, 0), IsVisible = false
        };
        _contentText = new TextBlock
        {
            FontSize = 18, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(10, 4, 10, 10)
        };
        var stack = new StackPanel();
        stack.Children.Add(_speakerText);
        stack.Children.Add(_contentText);
        _bgImage = new Image { Stretch = Avalonia.Media.Stretch.UniformToFill, Opacity = 0.3 };
        var overlay = new Grid();
        overlay.Children.Add(_bgImage);
        overlay.Children.Add(stack);
        _root = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            Child = overlay, IsVisible = false
        };
        Content = _root;
        _root.PointerPressed += (_, _) =>
        {
            if (!_root.IsVisible) return;
            if (IsPausedByTag && !IsComplete) { IsPausedByTag = false; return; }
            if (!IsComplete) { SkipToEnd(); }
            else { _state.Set(StateKeys.Dialog.Complete, true); _state.Set(StateKeys.Dialog.WaitingSayComplete, true); }
        };
    }

    public void SetText(string text, string? speaker = null)
    {
        _fullText = text;
        _charIndex = 0;
        _typeTimer = 0;
        IsPausedByTag = false;
        // 标记打字机未完成（供 Skip/Auto 模式检测）
        _state.Set(StateKeys.Dialog.TypewriterDone, false);

        // NVL 模式：调整对话框样式（全屏半透明，而非底部小条）
        var nvlActive = _state.Get<bool>(StateKeys.Nvl.Active);
        if (nvlActive)
        {
            _root.VerticalAlignment = VerticalAlignment.Stretch;
            _root.HorizontalAlignment = HorizontalAlignment.Stretch;
            _contentText.FontSize = 16;
            _contentText.Margin = new Thickness(20, 10, 20, 20);
            // NVL 模式下隐藏单个说话者（多角色累积文本已包含说话者）
            _speakerText.IsVisible = false;
            _root.IsVisible = true;
            ApplyInlineMarkup("");
            return;
        }
        else
        {
            _root.VerticalAlignment = VerticalAlignment.Bottom;
            _root.HorizontalAlignment = HorizontalAlignment.Stretch;
            _contentText.FontSize = 18;
            _contentText.Margin = new Thickness(10, 4, 10, 10);
        }

        var spkColor = _state.Get<string>(StateKeys.Dialog.SpeakerColor) ?? _state.Get<string>(StateKeys.Dialog.SpeakerColorDefault);
        var txtColor = _state.Get<string>(StateKeys.Dialog.TextColor) ?? _state.Get<string>(StateKeys.Dialog.TextColorDefault);
        var spkFont = _state.Get<string>(StateKeys.Dialog.SpeakerFont);
        var txtFont = _state.Get<string>(StateKeys.Dialog.TextFont);

        _speakerText.Foreground = !string.IsNullOrEmpty(spkColor)
            ? new SolidColorBrush(Color.Parse(spkColor)) : new SolidColorBrush(Color.FromArgb(220, 255, 200, 100));
        _contentText.Foreground = !string.IsNullOrEmpty(txtColor)
            ? new SolidColorBrush(Color.Parse(txtColor)) : Brushes.White;
        if (!string.IsNullOrEmpty(spkFont)) _speakerText.FontFamily = new FontFamily(spkFont);
        if (!string.IsNullOrEmpty(txtFont)) _contentText.FontFamily = new FontFamily(txtFont);

        _speakerText.IsVisible = !string.IsNullOrEmpty(speaker);
        if (_speakerText.IsVisible) _speakerText.Text = speaker;
        _root.IsVisible = true;
        ApplyInlineMarkup("");
    }

    public void Advance(double deltaSeconds)
    {
        if (!_root.IsVisible || IsComplete) return;
        var twEnabled = _state.ContainsKey(StateKeys.Dialog.TypewriterEnabled)
            ? _state.Get<bool>(StateKeys.Dialog.TypewriterEnabled) : true;
        if (!twEnabled) { SkipToEnd(); return; }
        if (IsPausedByTag) return;

        var fastIdx = _fullText.IndexOf("{fast}", StringComparison.Ordinal);
        if (fastIdx >= 0) { SkipToEnd(); return; }

        _typeTimer += deltaSeconds;
        var speed = _state.Get<double?>(StateKeys.Dialog.TypewriterSpeed) ?? TypeSpeed;
        int charsToShow = (int)(_typeTimer * speed);
        if (charsToShow > _charIndex)
        {
            _charIndex = Math.Min(charsToShow, _fullText.Length);
            var start = _charIndex;
            while (_charIndex < _fullText.Length && _fullText[_charIndex] != ' ' &&
                   _fullText[_charIndex] != '，' && _fullText[_charIndex] != '。' &&
                   _fullText[_charIndex] != '！' && _fullText[_charIndex] != '？' &&
                   _charIndex - start < 5) _charIndex++;
        }

        var raw = _fullText[..Math.Min(_charIndex, _fullText.Length)];
        var wIdx = raw.IndexOf("{w}");
        if (wIdx >= 0) { _charIndex = wIdx; IsPausedByTag = true; _fullText = _fullText.Remove(wIdx, 3); raw = _fullText[..Math.Min(_charIndex, _fullText.Length)]; }
        var pIdx = raw.IndexOf("{p}");
        if (pIdx >= 0) { _charIndex = pIdx; IsPausedByTag = true; _fullText = _fullText.Remove(pIdx, 3); raw = _fullText[..Math.Min(_charIndex, _fullText.Length)]; }

        // 剥离末尾未闭合的 {tag，防止标签字符泄露到打字机画面
        var cleaned = StripTrailingUnclosedTag(raw);
        ApplyInlineMarkup(cleaned);

        // 打字机完成时通知状态容器
        if (IsComplete)
            _state.Set(StateKeys.Dialog.TypewriterDone, true);
    }

    public void SkipToEnd() { _charIndex = _fullText.Length; IsPausedByTag = false; ApplyInlineMarkup(_fullText); _state.Set(StateKeys.Dialog.TypewriterDone, true); }
    public void Hide() { _root.IsVisible = false; }

    /// <summary>去掉末尾未闭合的 {xxx 片段（不包含 }），防止渲染时字符泄露</summary>
    private static string StripTrailingUnclosedTag(string raw)
    {
        int lastOpen = raw.LastIndexOf('{');
        if (lastOpen < 0) return raw;
        int closeAfter = raw.IndexOf('}', lastOpen);
        if (closeAfter >= 0) return raw; // 闭合标签完整，不需要裁剪
        return raw[..lastOpen]; // 截断到最后一个 { 之前
    }

    // ========== Inline Markup ==========

#pragma warning disable CS8602
    private void ApplyInlineMarkup(string raw)
    {
        _contentText.Inlines.Clear();
        var text = raw.Replace("{w}", "").Replace("{fast}", "").Replace("{p}", "");
        int pos = 0;
        while (pos < text.Length)
        {
            if (text[pos] == '{')
            {
                int close = text.IndexOf('}', pos);
                if (close < 0) { AppendRun(text[pos..]); break; }
                var tag = text[(pos + 1)..close];
                int endTag = text.IndexOf("{/" + tag.Split('=')[0] + "}", close + 1);
                var inner = endTag >= 0 ? text[(close + 1)..endTag] : text[(close + 1)..];
                var attr = tag.Contains('=') ? tag[(tag.IndexOf('=') + 1)..] : null;
                var tagName = tag.Split('=')[0];
                switch (tagName)
                {
                    case "b": AppendRun(inner, bold: true); break;
                    case "i": AppendRun(inner, italic: true); break;
                    case "u": AppendRun(inner, underline: true); break;
                    case "color": AppendRun(inner, color: attr); break;
                    case "font": AppendRun(inner, font: attr); break;
                    case "size": { var s = double.TryParse(attr, out var fs) ? fs : (double?)null; AppendRun(inner, size: s); break; }
                    default: AppendRun("{" + tag + "}"); break;
                }
                pos = endTag >= 0 ? endTag + tagName.Length + 3 : text.Length;
            }
            else
            {
                int nextBrace = text.IndexOf('{', pos + 1);
                if (nextBrace < 0) { AppendRun(text[pos..]); break; }
                AppendRun(text[pos..nextBrace]);
                pos = nextBrace;
            }
        }
    }

    private void AppendRun(string text, bool bold = false, bool italic = false,
        bool underline = false, string? color = null, string? font = null, double? size = null)
    {
        var run = new Run(text);
        if (bold) run.FontWeight = FontWeight.Bold;
        if (italic) run.FontStyle = FontStyle.Italic;
        if (underline) run.TextDecorations = TextDecorations.Underline;
        if (color != null) run.Foreground = new SolidColorBrush(Color.Parse(color));
        if (font != null) run.FontFamily = new FontFamily(font);
        if (size.HasValue) run.FontSize = size.Value;
        _contentText.Inlines.Add(run);
    }
#pragma warning restore CS8602
}
