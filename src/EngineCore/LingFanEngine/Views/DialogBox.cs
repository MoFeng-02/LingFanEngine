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
    private readonly DialogEngine _engine;
    private string? _lastSideImage;

    /// <summary>打字机速度（字符/秒）——委托 DialogEngine</summary>
    public double TypeSpeed
    {
        get => _engine.TypeSpeed;
        set => _engine.TypeSpeed = value;
    }
    public bool IsComplete => _engine.IsComplete;
    public bool IsPausedByTag => _engine.IsPausedByTag;

    // ── 增量渲染（B1：按 Segments 增量构建 Run，消除每帧全量 markup 重建） ──
    /// <summary>增量渲染器（状态机已提取为共享组件 DialogTextRenderer，供模板复用）</summary>
    private readonly DialogTextRenderer _renderer;

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
            }).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    System.Diagnostics.Debug.WriteLine($"[DialogBox] BackgroundImage Task.Run faulted: {t.Exception?.GetBaseException().Message}");
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    public DialogBox(IStateContainer state)
    {
        _state = state;
        _engine = new DialogEngine(state);
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
        _renderer = new DialogTextRenderer(_contentText);
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
        // Phase 24: 更新侧脸图（新对话立即生效，不等下一帧）
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

        // 回溯重放（A5）：重看已读内容不打字机——直接完整显示
        var isReplay = _state.Get<bool>(StateKeys.Rollback.IsReplay);

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

            // Phase 65: 委托 DialogEngine 处理 NVL 累积逻辑（含溢出裁剪 SkipHint，C4）
            var skipHint = _state.Get<int>(StateKeys.Nvl.SkipHint);
            _state.Set(StateKeys.Nvl.SkipHint, -1);
            _engine.SetNvlText(text, skipHint);
            if (isReplay) _engine.SkipToEnd();
            ResetRenderState();
            _renderer.RenderUpTo(_engine.CharIndex);
            return;
        }

        // ADV 模式——委托 DialogEngine
        _engine.SetText(text);
        if (isReplay) _engine.SkipToEnd();

        _root.VerticalAlignment = VerticalAlignment.Bottom;
        _root.HorizontalAlignment = HorizontalAlignment.Stretch;
        _contentText.FontSize = 18;
        _contentText.Margin = new Thickness(10, 4, 10, 10);

        _speakerText.IsVisible = !string.IsNullOrEmpty(speaker);
        if (_speakerText.IsVisible) _speakerText.Text = speaker;
        _root.IsVisible = true;
        ResetRenderState();
        _renderer.RenderUpTo(_engine.CharIndex);
    }

    // ========== Inline Markup ==========

    // Phase 65: 委托 DialogEngine.Advance 推进打字机；
    // 2026-09（B1）：渲染按 Segments 增量构建——只增长边界段 Run 的文本，
    // 不再每帧全量解析 markup / 重建全部 Run（长文本与 NVL 累积场景的主要开销）
    public void Advance(double deltaSeconds)
    {
        if (!_root.IsVisible || IsComplete) return;
        // Phase 24: 每帧同步侧脸图（可能在对话期间切换）
        UpdateSideImage();
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

    public void Hide() { _root.IsVisible = false; _sideImage.IsVisible = false; }

    /// <summary>统一前进点击闸门（C5）——委托 DialogEngine.TryConsumeClick</summary>
    public bool TryConsumeClick() => _engine.TryConsumeClick();

    // ========== 增量渲染（B1——委托 DialogTextRenderer） ==========

    /// <summary>重置增量渲染状态（新文本加载时调用）</summary>
    private void ResetRenderState() => _renderer.Reset(_engine.Segments);

    /// <summary>重置 NVL 模式内部状态（场景切换或退出 NVL 模式时调用）</summary>
    public void ResetNvlState() => _engine.ResetNvlState();

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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DialogBox] SideImage 加载失败: {ex.Message}");
            _sideImage.IsVisible = false;
        }
    }

    /// <inheritdoc/>
    public Control AsControl() => this;
}
