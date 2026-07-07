using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Entry.UI;

/// <summary>
/// 调试控制台面板（Demo 层 UI）
/// <para>从 StateKeys.Debug.Logs 读取日志列表，实时显示调试信息。</para>
/// <para>支持清空日志、开启/关闭调试模式。</para>
/// </summary>
public class DebugConsolePanel : UserControl
{
    private readonly IStateContainer _state;
    private readonly IGameController? _controller;
    private StackPanel? _logPanel;
    private ScrollViewer? _scrollViewer;
    private TextBlock? _statusText;

    /// <summary>面板关闭事件</summary>
    public event Action? Closed;

    public DebugConsolePanel(IStateContainer state, IGameController? controller)
    {
        _state = state;
        _controller = controller;

        BuildUI();
        IsVisible = false;
    }

    private void BuildUI()
    {
        var mainPanel = new Panel();

        // 半透明背景
        var bgBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 15, 15, 25)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        bgBorder.PointerPressed += (_, _) => { Hide(); Closed?.Invoke(); };
        mainPanel.Children.Add(bgBorder);

        // 内容容器
        var contentBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(245, 20, 20, 30)),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(30),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 标题栏
        var titleBar = new Grid();
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "🔧 调试控制台",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.FromArgb(220, 100, 255, 150)),
            Margin = new Thickness(15, 12, 15, 8)
        };
        Grid.SetColumn(title, 0);
        titleBar.Children.Add(title);

        var closeBtn = new Button
        {
            Content = "✕",
            FontSize = 14,
            Margin = new Thickness(0, 8, 12, 8),
            Padding = new Thickness(8, 3),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        closeBtn.Click += (_, _) => { Hide(); Closed?.Invoke(); };
        Grid.SetColumn(closeBtn, 1);
        titleBar.Children.Add(closeBtn);

        Grid.SetRow(titleBar, 0);
        grid.Children.Add(titleBar);

        // 工具栏
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(15, 0, 15, 8)
        };

        _statusText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(180, 180, 180, 180)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 15, 0)
        };
        toolbar.Children.Add(_statusText);

        var clearBtn = new Button
        {
            Content = "🗑 清空",
            FontSize = 12,
            Padding = new Thickness(10, 3),
            Margin = new Thickness(5, 0),
            Background = new SolidColorBrush(Color.FromArgb(40, 80, 80, 100)),
            BorderThickness = new Thickness(0)
        };
        clearBtn.Click += (_, _) =>
        {
            _controller?.ClearDebugLogs();
            RefreshLogs();
        };
        toolbar.Children.Add(clearBtn);

        var toggleDebugBtn = new Button
        {
            Content = "调试模式",
            FontSize = 12,
            Padding = new Thickness(10, 3),
            Margin = new Thickness(5, 0),
            Background = new SolidColorBrush(Color.FromArgb(40, 80, 80, 100)),
            BorderThickness = new Thickness(0),
            Tag = "toggle_debug"
        };
        toggleDebugBtn.Click += (_, _) =>
        {
            var enabled = _state.Get<bool>(StateKeys.Debug.Enabled);
            _state.Set(StateKeys.Debug.Enabled, !enabled);
            UpdateStatusText();
        };
        toolbar.Children.Add(toggleDebugBtn);

        Grid.SetRow(toolbar, 1);
        grid.Children.Add(toolbar);

        // 日志列表
        _logPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(5)
        };

        _scrollViewer = new ScrollViewer
        {
            Content = _logPanel,
            Margin = new Thickness(10, 0, 10, 10),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(_scrollViewer, 2);
        grid.Children.Add(_scrollViewer);

        contentBorder.Child = grid;
        mainPanel.Children.Add(contentBorder);

        Content = mainPanel;
    }

    /// <summary>显示面板</summary>
    public void Show()
    {
        UpdateStatusText();
        RefreshLogs();
        IsVisible = true;
    }

    /// <summary>隐藏面板</summary>
    public void Hide()
    {
        IsVisible = false;
    }

    /// <summary>更新状态文本</summary>
    private void UpdateStatusText()
    {
        if (_statusText == null) return;
        var enabled = _state.Get<bool>(StateKeys.Debug.Enabled);
        var logs = _state.Get<List<DebugLogEntry>>(StateKeys.Debug.Logs) ?? [];
        _statusText.Text = $"调试模式: {(enabled ? "开" : "关")} | 日志: {logs.Count} 条";

        // 更新切换按钮文本
        if (Content is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is Border border && border.Child is Grid grid)
                {
                    foreach (var row in grid.Children)
                    {
                        if (row is StackPanel toolbar)
                        {
                            foreach (var item in toolbar.Children)
                            {
                                if (item is Button btn && btn.Tag is string tag && tag == "toggle_debug")
                                {
                                    btn.Content = enabled ? "调试模式 [开]" : "调试模式 [关]";
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>刷新日志列表</summary>
    public void RefreshLogs()
    {
        if (_logPanel == null) return;
        _logPanel.Children.Clear();
        UpdateStatusText();

        var logs = _state.Get<List<DebugLogEntry>>(StateKeys.Debug.Logs) ?? [];

        if (logs.Count == 0)
        {
            _logPanel.Children.Add(new TextBlock
            {
                Text = "暂无调试日志",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(120, 180, 180, 180)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 30, 0, 0)
            });
            return;
        }

        // 倒序显示（最新在上）
        for (int i = logs.Count - 1; i >= 0; i--)
        {
            var entry = logs[i];
            _logPanel.Children.Add(CreateLogItem(entry));
        }

        // 滚动到顶部（最新日志）
        _scrollViewer?.ScrollToHome();
    }

    /// <summary>创建单条日志项</summary>
    private Control CreateLogItem(DebugLogEntry entry)
    {
        var color = entry.Level switch
        {
            "Error" => Color.FromArgb(220, 255, 100, 100),
            "Warning" => Color.FromArgb(220, 255, 200, 100),
            "Debug" => Color.FromArgb(180, 150, 200, 255),
            _ => Color.FromArgb(200, 200, 220, 200)
        };

        var timeStr = entry.Timestamp.ToString("HH:mm:ss.fff");

        var text = new TextBlock
        {
            Text = $"[{timeStr}] [{entry.Level}] {entry.Message}" +
                   (string.IsNullOrEmpty(entry.Source) ? "" : $" ({entry.Source})"),
            FontSize = 12,
            Foreground = new SolidColorBrush(color),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(5, 2),
            FontFamily = new FontFamily("Consolas,Menlo,monospace")
        };

        return new Border
        {
            Child = text,
            BorderBrush = new SolidColorBrush(Color.FromArgb(20, 60, 60, 80)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(3)
        };
    }
}
