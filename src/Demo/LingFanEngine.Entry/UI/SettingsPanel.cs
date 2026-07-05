using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Entry.UI;

/// <summary>
/// 设置面板（Demo 层 UI）
/// <para>提供音量、文字速度、自动模式延迟、全屏等偏好设置。</para>
/// <para>设置值直接写入状态容器（__pref_* 前缀），通过 SaveSystemState 持久化。</para>
/// </summary>
public class SettingsPanel : UserControl
{
    private readonly IStateContainer _state;

    /// <summary>面板关闭事件</summary>
    public event Action? Closed;

    public SettingsPanel(IStateContainer state)
    {
        _state = state;

        var mainGrid = new Grid();
        mainGrid.Background = new SolidColorBrush(Color.FromArgb(230, 15, 15, 25));
        mainGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
        mainGrid.VerticalAlignment = VerticalAlignment.Stretch;

        var layoutGrid = new Grid();
        layoutGrid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Auto));
        layoutGrid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
        layoutGrid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Auto));

        // 标题栏
        var header = new TextBlock
        {
            Text = "⚙ 设置",
            Foreground = Brushes.White,
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 20, 0, 15)
        };
        layoutGrid.Children.Add(header);
        Grid.SetRow(header, 0);

        // 设置内容
        var contentScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(30, 0, 30, 10)
        };

        var contentPanel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 15 };

        // 音量设置区域
        contentPanel.Children.Add(CreateSectionLabel("🔊 音量设置"));
        contentPanel.Children.Add(CreateSliderRow("主音量", StateKeys.Preferences.MasterVolume, 0, 1, 0.01));
        contentPanel.Children.Add(CreateSliderRow("BGM 音量", StateKeys.Preferences.BgmVolume, 0, 1, 0.01));
        contentPanel.Children.Add(CreateSliderRow("SE 音量", StateKeys.Preferences.SeVolume, 0, 1, 0.01));
        contentPanel.Children.Add(CreateSliderRow("语音音量", StateKeys.Preferences.VoiceVolume, 0, 1, 0.01));
        contentPanel.Children.Add(CreateCheckboxRow("静音", StateKeys.Preferences.MasterMuted));

        // 播放设置区域
        contentPanel.Children.Add(CreateSectionLabel("▶ 播放设置"));
        contentPanel.Children.Add(CreateSliderRow("打字机速度（字符/秒）", StateKeys.Preferences.TextSpeed, 1, 100, 1));
        contentPanel.Children.Add(CreateSliderRow("自动模式延迟（秒）", StateKeys.Preferences.AutoForwardDelay, 0.5, 10, 0.1));
        contentPanel.Children.Add(CreateCheckboxRow("允许跳过未读文本", StateKeys.Preferences.SkipUnseen));

        // 显示设置区域
        contentPanel.Children.Add(CreateSectionLabel("🖥 显示设置"));
        contentPanel.Children.Add(CreateCheckboxRow("全屏模式", StateKeys.Preferences.Fullscreen));

        contentScroll.Content = contentPanel;
        layoutGrid.Children.Add(contentScroll);
        Grid.SetRow(contentScroll, 1);

        // 底部按钮
        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 20, 20)
        };

        var closeBtn = new Button { Content = "关闭", MinWidth = 100, Margin = new Thickness(5) };
        closeBtn.Click += (_, _) => { Hide(); Closed?.Invoke(); };
        btnPanel.Children.Add(closeBtn);

        layoutGrid.Children.Add(btnPanel);
        Grid.SetRow(btnPanel, 2);

        mainGrid.Children.Add(layoutGrid);
        Content = mainGrid;
        IsVisible = false;
    }

    /// <summary>显示面板</summary>
    public void Show() { IsVisible = true; }

    /// <summary>隐藏面板</summary>
    public void Hide() { IsVisible = false; }

    /// <summary>创建分区标题</summary>
    private TextBlock CreateSectionLabel(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromArgb(200, 130, 200, 255)),
        FontSize = 18,
        FontWeight = FontWeight.Bold,
        Margin = new Thickness(0, 10, 0, 5)
    };

    /// <summary>创建滑块行</summary>
    private Border CreateSliderRow(string label, string stateKey, double min, double max, double step)
    {
        var currentValue = _state.Get<double>(stateKey);
        if (currentValue == 0 && stateKey.Contains("Volume"))
            currentValue = 1.0; // 默认音量

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(30, 50, 50, 70)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(150, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(60, GridUnitType.Pixel));

        var labelText = new TextBlock
        {
            Text = label,
            Foreground = Brushes.White,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(labelText);
        Grid.SetColumn(labelText, 0);

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = currentValue,
            TickFrequency = step,
            Margin = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(slider);
        Grid.SetColumn(slider, 1);

        var valueText = new TextBlock
        {
            Text = FormatValue(currentValue, stateKey),
            Foreground = new SolidColorBrush(Color.FromArgb(180, 180, 180, 200)),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        grid.Children.Add(valueText);
        Grid.SetColumn(valueText, 2);

        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
            {
                var val = slider.Value;
                _state.Set(stateKey, val);
                valueText.Text = FormatValue(val, stateKey);

                // 同步自动模式延迟到 Playback
                if (stateKey == StateKeys.Preferences.AutoForwardDelay)
                    _state.Set(StateKeys.Playback.AutoDelay, val);
            }
        };

        border.Child = grid;
        return border;
    }

    /// <summary>创建复选框行</summary>
    private Border CreateCheckboxRow(string label, string stateKey)
    {
        var currentValue = _state.Get<bool>(stateKey);

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(30, 50, 50, 70)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        var checkbox = new CheckBox
        {
            IsChecked = currentValue,
            VerticalAlignment = VerticalAlignment.Center
        };

        var labelText = new TextBlock
        {
            Text = label,
            Foreground = Brushes.White,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(checkbox);
        panel.Children.Add(labelText);

        checkbox.Click += (_, _) =>
        {
            _state.Set(stateKey, checkbox.IsChecked ?? false);
        };

        border.Child = panel;
        return border;
    }

    /// <summary>格式化显示值</summary>
    private static string FormatValue(double val, string stateKey)
    {
        if (stateKey.Contains("Volume"))
            return $"{val * 100:F0}%";
        if (stateKey == StateKeys.Preferences.TextSpeed)
            return $"{val:F0} 字/秒";
        if (stateKey == StateKeys.Preferences.AutoForwardDelay)
            return $"{val:F1} 秒";
        return val.ToString("F1");
    }
}
