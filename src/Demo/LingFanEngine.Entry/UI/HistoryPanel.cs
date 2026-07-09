using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models;

namespace LingFanEngine.Entry.UI;

/// <summary>
/// 对话历史/回看面板（Demo 层 UI）
/// <para>从状态容器读取 List&lt;DialogHistoryEntry&gt; 渲染历史对话列表。</para>
/// <para>对标 Ren'Py 的 backlog/回看界面。</para>
/// <para>支持点击条目跳转到对应回溯检查点（CheckpointIndex &gt;= 0 时可跳转）。</para>
/// </summary>
public class HistoryPanel : UserControl
{
    private readonly IStateContainer _state;
    private readonly IGameController? _controller;
    private readonly ScrollViewer _scrollViewer;
    private readonly StackPanel _contentPanel;

    /// <summary>面板关闭事件</summary>
    public event Action? Closed;

    public HistoryPanel(IStateContainer state, IGameController? controller = null)
    {
        _state = state;
        _controller = controller;

        var mainGrid = new Grid();
        mainGrid.Background = new SolidColorBrush(Color.FromArgb(235, 10, 10, 18));
        mainGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
        mainGrid.VerticalAlignment = VerticalAlignment.Stretch;

        var layoutGrid = new Grid();
        layoutGrid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Auto));
        layoutGrid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
        layoutGrid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Auto));

        // 标题栏
        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 20, 0, 10)
        };

        var title = new TextBlock
        {
            Text = "📜 对话历史",
            Foreground = Brushes.White,
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 20, 0)
        };

        var hint = new TextBlock
        {
            Text = "💡 点击条目可回溯到该对话",
            Foreground = new SolidColorBrush(Color.FromArgb(140, 140, 160, 200)),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 20, 0)
        };

        var closeBtn = new Button { Content = "✕ 关闭", MinWidth = 80 };
        closeBtn.Click += (_, _) => { Hide(); Closed?.Invoke(); };

        headerPanel.Children.Add(title);
        headerPanel.Children.Add(hint);
        headerPanel.Children.Add(closeBtn);
        layoutGrid.Children.Add(headerPanel);
        Grid.SetRow(headerPanel, 0);

        // 历史内容
        _contentPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(30, 0, 30, 10)
        };

        _scrollViewer = new ScrollViewer
        {
            Content = _contentPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        layoutGrid.Children.Add(_scrollViewer);
        Grid.SetRow(_scrollViewer, 1);

        // 底部操作栏
        var bottomPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 15)
        };

        var clearBtn = new Button { Content = "清空历史", MinWidth = 100, Margin = new Thickness(5) };
        clearBtn.Click += (_, _) =>
        {
            _state.Set(StateKeys.History.Entries, new List<DialogHistoryEntry>());
            RefreshHistory();
        };

        bottomPanel.Children.Add(clearBtn);
        layoutGrid.Children.Add(bottomPanel);
        Grid.SetRow(bottomPanel, 2);

        mainGrid.Children.Add(layoutGrid);
        Content = mainGrid;
        IsVisible = false;
    }

    /// <summary>显示面板并刷新内容</summary>
    public void Show()
    {
        IsVisible = true;
        RefreshHistory();
        // 滚动到底部（最新对话）
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _scrollViewer.ScrollToEnd();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>隐藏面板</summary>
    public void Hide()
    {
        IsVisible = false;
    }

    /// <summary>刷新历史对话列表</summary>
    public void RefreshHistory()
    {
        _contentPanel.Children.Clear();

        var history = _state.Get<List<DialogHistoryEntry>>(StateKeys.History.Entries);
        if (history == null || history.Count == 0)
        {
            _contentPanel.Children.Add(new TextBlock
            {
                Text = "暂无对话历史",
                Foreground = new SolidColorBrush(Color.FromArgb(120, 120, 120, 140)),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0)
            });
            return;
        }

        foreach (var entry in history)
        {
            _contentPanel.Children.Add(CreateHistoryEntryPanel(entry));
        }
    }

    /// <summary>创建单条历史记录面板</summary>
    private Border CreateHistoryEntryPanel(DialogHistoryEntry entry)
    {
        var canRollback = entry.CheckpointIndex >= 0 && _controller != null;

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(25, 40, 40, 60)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 0, 0, 6)
        };

        // 可回溯的条目添加悬停效果和指针样式
        if (canRollback)
        {
            border.Cursor = new Cursor(StandardCursorType.Hand);
            border.PointerEntered += (_, _) =>
                border.Background = new SolidColorBrush(Color.FromArgb(50, 60, 80, 120));
            border.PointerExited += (_, _) =>
                border.Background = new SolidColorBrush(Color.FromArgb(25, 40, 40, 60));
            border.PointerPressed += async (_, _) =>
            {
                if (entry.CheckpointIndex >= 0)
                {
                    if (_controller != null)
                    {
                        await _controller.RollbackToAsync(entry.CheckpointIndex);
                        Hide();
                        Closed?.Invoke();
                    }
                }
            };
        }

        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };

        // 说话者 + 场景 + 时间 + 回溯标记
        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };

        if (!string.IsNullOrEmpty(entry.Speaker))
        {
            headerPanel.Children.Add(new TextBlock
            {
                Text = entry.Speaker,
                Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 200, 100)),
                FontSize = 15,
                FontWeight = FontWeight.Bold
            });
        }

        if (!string.IsNullOrEmpty(entry.SceneName))
        {
            headerPanel.Children.Add(new TextBlock
            {
                Text = $"[{entry.SceneName}]",
                Foreground = new SolidColorBrush(Color.FromArgb(120, 120, 150, 180)),
                FontSize = 12
            });
        }

        headerPanel.Children.Add(new TextBlock
        {
            Text = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
            Foreground = new SolidColorBrush(Color.FromArgb(100, 100, 100, 120)),
            FontSize = 11
        });

        // 可回溯条目显示回溯图标
        if (canRollback)
        {
            headerPanel.Children.Add(new TextBlock
            {
                Text = "↩ 可回溯",
                Foreground = new SolidColorBrush(Color.FromArgb(160, 100, 180, 255)),
                FontSize = 11,
                FontWeight = FontWeight.Medium
            });
        }

        panel.Children.Add(headerPanel);

        // 对话文本
        panel.Children.Add(new TextBlock
        {
            Text = entry.Text,
            Foreground = canRollback
                ? new SolidColorBrush(Color.FromArgb(240, 240, 240, 255))
                : Brushes.White,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });

        border.Child = panel;
        return border;
    }
}
