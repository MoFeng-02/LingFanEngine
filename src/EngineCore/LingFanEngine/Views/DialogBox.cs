using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Views;

public class DialogBox : UserControl, IDialogBox
{
    /// <summary>侧脸图列宽度（像素）</summary>
    private const double SideImageColumnWidth = 120;

    private readonly TextBlock _speakerText;
    private readonly TextBlock _contentText;
    private readonly Border _root;
    private readonly Image _bgImage;
    private readonly Image _sideImage;
    private readonly IStateContainer _state;
    private string _fullText = "";
    private int _charIndex;
    private double _typeTimer;
    private string? _lastSideImage;
    /// <summary>NVL 模式下已显示的文本长度（打字机跳过此部分，只打新追加的行）</summary>
    private int _nvlSkipLength;
    /// <summary>NVL 模式下上一帧的文本（用于检测是追加还是 nvl clear 后的新开始）</summary>
    private string _nvlPrevText = "";

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
            if (value == null) return;
            _ = Task.Run(() =>
            {
                try
                {
                    var bmp = new Avalonia.Media.Imaging.Bitmap(value);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => _bgImage.Source = bmp);
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[DialogBox] BackgroundImage load failed: {value} — {ex.Message}"); }
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
        _sideImage = new Image
        {
            Stretch = Avalonia.Media.Stretch.UniformToFill,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsVisible = false
        };
        var overlay = new Grid();
        overlay.Children.Add(_bgImage);
        overlay.Children.Add(stack);
        // 对话框内容区域：左侧侧脸图 + 右侧文本
        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition(SideImageColumnWidth, GridUnitType.Pixel));
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        Grid.SetColumn(_sideImage, 0);
        Grid.SetColumn(overlay, 1);
        contentGrid.Children.Add(_sideImage);
        contentGrid.Children.Add(overlay);
        _root = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            Child = contentGrid, IsVisible = false
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

        // Phase 24: 更新侧脸图
        UpdateSideImage();

        // 颜色/字体样式（NVL 和 ADV 模式都需要）
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

        // NVL 模式：调整对话框样式（全屏半透明，而非底部小条）
        var nvlActive = _state.Get<bool>(StateKeys.Nvl.Active);
        if (nvlActive)
        {
            _root.VerticalAlignment = VerticalAlignment.Stretch;
            _root.HorizontalAlignment = HorizontalAlignment.Stretch;
            _contentText.FontSize = 16;
            _contentText.Margin = new Thickness(20, 10, 20, 20);
            // NVL 模式下隐藏单个说话者（累积文本已包含说话者名称内联）
            _speakerText.IsVisible = false;
            _root.IsVisible = true;

            // 计算打字机跳过长度——已有文本立即显示，只对新追加的行打字机
            if (!string.IsNullOrEmpty(_nvlPrevText) && text.StartsWith(_nvlPrevText) && text.Length > _nvlPrevText.Length)
            {
                // 文本被追加——跳过已有部分，只打字机新内容
                _nvlSkipLength = _nvlPrevText.Length;
                _charIndex = _nvlSkipLength;
                // 调整计时器使打字机从新内容开始
                var speed = _state.Get<double?>(StateKeys.Dialog.TypewriterSpeed) ?? TypeSpeed;
                _typeTimer = _nvlSkipLength / Math.Max(speed, 1.0);
                // 立即显示已有文本
                ApplyInlineMarkup(_fullText[.._nvlSkipLength]);
            }
            else
            {
                // 文本完全变化（nvl clear 后新开始）——从零开始打字机
                _nvlSkipLength = 0;
                _charIndex = 0;
                ApplyInlineMarkup("");
            }
            _nvlPrevText = text;
            return;
        }

        // ADV 模式——重置 NVL 内部追踪状态
        _nvlSkipLength = 0;
        _nvlPrevText = "";

        _root.VerticalAlignment = VerticalAlignment.Bottom;
        _root.HorizontalAlignment = HorizontalAlignment.Stretch;
        _contentText.FontSize = 18;
        _contentText.Margin = new Thickness(10, 4, 10, 10);

        _speakerText.IsVisible = !string.IsNullOrEmpty(speaker);
        if (_speakerText.IsVisible) _speakerText.Text = speaker;
        _root.IsVisible = true;
        ApplyInlineMarkup("");
    }

    public void Advance(double deltaSeconds)
    {
        if (!_root.IsVisible || IsComplete) return;
        // Phase 24: 每帧同步侧脸图（可能在对话期间切换）
        UpdateSideImage();
        var twEnabled = _state.ContainsKey(StateKeys.Dialog.TypewriterEnabled)
            ? _state.Get<bool>(StateKeys.Dialog.TypewriterEnabled) : true;
        if (!twEnabled) { SkipToEnd(); return; }
        // Phase 44: instant=true 时跳过打字机效果，瞬时显示全部文本
        if (_state.ContainsKey(StateKeys.Dialog.Instant) && _state.Get<bool>(StateKeys.Dialog.Instant))
        { SkipToEnd(); return; }
        if (IsPausedByTag) return;

        // NVL 模式：跳过已显示的旧文本，只对新追加的行打字机
        if (_nvlSkipLength > 0 && _charIndex < _nvlSkipLength)
        {
            _charIndex = _nvlSkipLength;
        }

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
    public void Hide() { _root.IsVisible = false; _sideImage.IsVisible = false; }

    /// <summary>重置 NVL 模式内部状态（场景切换或退出 NVL 模式时调用）</summary>
    public void ResetNvlState() { _nvlSkipLength = 0; _nvlPrevText = ""; }

    /// <summary>
    /// Phase 24: 从 __dialog_side_image 状态键读取侧脸图路径并渲染
    /// <para>路径为空或无法加载时隐藏侧脸图区域</para>
    /// </summary>
    private void UpdateSideImage()
    {
        var sidePath = _state.Get<string>(StateKeys.Dialog.SideImage);
        if (sidePath == _lastSideImage) return; // 路径未变化，跳过
        _lastSideImage = sidePath;

        if (string.IsNullOrEmpty(sidePath))
        {
            _sideImage.IsVisible = false;
            _sideImage.Source = null;
            return;
        }

        try
        {
            _sideImage.Source = new Avalonia.Media.Imaging.Bitmap(sidePath);
            _sideImage.IsVisible = true;
        }
        catch
        {
            _sideImage.IsVisible = false;
        }
    }

    /// <inheritdoc/>
    public Control AsControl() => this;

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
        ParseInline(text, 0, text.Length, null);
    }

    /// <summary>
    /// 递归解析内联标记，支持嵌套标签如 {b}{color=#FFD700}秘密{/color}{/b}
    /// </summary>
    private int ParseInline(string text, int start, int end, InlineStyle? parentStyle)
    {
        int pos = start;
        while (pos < end)
        {
            if (text[pos] == '{')
            {
                int close = text.IndexOf('}', pos);
                if (close < 0 || close >= end) { AppendRun(text[pos..end], parentStyle); return end; }
                var tag = text[(pos + 1)..close];
                var tagName = tag.Contains('=') ? tag[..tag.IndexOf('=')] : tag;
                var attr = tag.Contains('=') ? tag[(tag.IndexOf('=') + 1)..] : null;

                // 闭合标签 → 返回（让调用者处理）
                if (tagName.StartsWith('/'))
                    return close + 1;

                // 开放标签 → 递归解析内部内容，合并样式
                var childStyle = MergeStyle(parentStyle, tagName, attr);
                int afterTag = close + 1;
                int nextClose = ParseInline(text, afterTag, end, childStyle);
                pos = nextClose;
            }
            else
            {
                // 普通文本——找到下一个 { 或 end
                int nextBrace = text.IndexOf('{', pos);
                if (nextBrace < 0 || nextBrace >= end) nextBrace = end;
                AppendRun(text[pos..nextBrace], parentStyle);
                pos = nextBrace;
            }
        }
        return pos;
    }

    /// <summary>合并内联样式（子标签叠加父标签的样式）</summary>
    private static InlineStyle? MergeStyle(InlineStyle? parent, string tagName, string? attr)
    {
        var s = parent ?? new InlineStyle();
        return tagName switch
        {
            "b" => s with { Bold = true },
            "i" => s with { Italic = true },
            "u" => s with { Underline = true },
            "color" => attr != null ? s with { Color = attr } : s,
            "font" => attr != null ? s with { Font = attr } : s,
            "size" => double.TryParse(attr, out var fs) ? s with { Size = fs } : s,
            _ => s
        };
    }

    private void AppendRun(string text, InlineStyle? style)
    {
        if (string.IsNullOrEmpty(text)) return;
        var run = new Run(text);
        if (style != null)
        {
            if (style.Bold) run.FontWeight = FontWeight.Bold;
            if (style.Italic) run.FontStyle = FontStyle.Italic;
            if (style.Underline) run.TextDecorations = TextDecorations.Underline;
            if (style.Color != null) run.Foreground = new SolidColorBrush(Color.Parse(style.Color));
            if (style.Font != null) run.FontFamily = new FontFamily(style.Font);
            if (style.Size.HasValue) run.FontSize = style.Size.Value;
        }
        _contentText.Inlines.Add(run);
    }

    /// <summary>内联样式数据（支持嵌套叠加）</summary>
    private record InlineStyle(
        bool Bold = false, bool Italic = false, bool Underline = false,
        string? Color = null, string? Font = null, double? Size = null);
#pragma warning restore CS8602
}
