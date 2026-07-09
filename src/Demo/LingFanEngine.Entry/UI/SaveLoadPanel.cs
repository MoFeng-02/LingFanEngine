using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Abstractions.Models.Saves;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Entry.UI;

/// <summary>
/// 存档/读档面板（Demo 层 UI）
/// <para>从存档服务读取存档槽信息，支持保存/读取/删除操作。</para>
/// <para>显示缩略图 + 存档名 + 存档时间，覆盖在 SceneView 上的半透明面板。</para>
/// </summary>
public class SaveLoadPanel : UserControl
{
    private readonly IStateContainer _state;
    private readonly ISaveService? _saveService;
    private readonly IGameController? _controller;
    private readonly ScrollViewer _scrollViewer;
    private readonly StackPanel _slotsPanel;
    private bool _isSaveMode = true;

    /// <summary>面板关闭事件</summary>
    public event Action? Closed;

    public SaveLoadPanel(IStateContainer state, ISaveService? saveService, IGameController? controller)
    {
        _state = state;
        _saveService = saveService;
        _controller = controller;

        // 主容器
        var mainGrid = new Grid();
        mainGrid.Background = new SolidColorBrush(Color.FromArgb(220, 15, 15, 20));
        mainGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
        mainGrid.VerticalAlignment = VerticalAlignment.Stretch;

        // 标题栏
        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 20, 0, 10)
        };

        var saveBtn = new Button { Content = "💾 保存", Margin = new Thickness(5), MinWidth = 100 };
        var loadBtn = new Button { Content = "📂 读取", Margin = new Thickness(5), MinWidth = 100 };
        var closeBtn = new Button { Content = "✕ 关闭", Margin = new Thickness(5), MinWidth = 80 };

        saveBtn.Click += (_, _) => { _isSaveMode = true; RefreshSlots(); };
        loadBtn.Click += (_, _) => { _isSaveMode = false; RefreshSlots(); };
        closeBtn.Click += (_, _) => { Hide(); Closed?.Invoke(); };

        headerPanel.Children.Add(saveBtn);
        headerPanel.Children.Add(loadBtn);
        headerPanel.Children.Add(closeBtn);

        // 存档槽列表
        _slotsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(20, 0, 20, 20)
        };

        _scrollViewer = new ScrollViewer
        {
            Content = _slotsPanel,
            Margin = new Thickness(0, 0, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var layoutGrid = new Grid();
        layoutGrid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Auto));
        layoutGrid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
        layoutGrid.Children.Add(headerPanel);
        Grid.SetRow(headerPanel, 0);
        layoutGrid.Children.Add(_scrollViewer);
        Grid.SetRow(_scrollViewer, 1);
        mainGrid.Children.Add(layoutGrid);

        Content = mainGrid;
        IsVisible = false;
    }

    /// <summary>显示面板</summary>
    public void Show(bool saveMode = true)
    {
        _isSaveMode = saveMode;
        IsVisible = true;
        RefreshSlots();
    }

    /// <summary>隐藏面板</summary>
    public void Hide()
    {
        IsVisible = false;
    }

    /// <summary>刷新存档槽列表</summary>
    private async void RefreshSlots()
    {
        _slotsPanel.Children.Clear();

        if (_saveService == null)
        {
            _slotsPanel.Children.Add(new TextBlock
            {
                Text = "存档服务不可用",
                Foreground = Brushes.White,
                Margin = new Thickness(10),
                FontSize = 16
            });
            return;
        }

        // 获取所有存档槽
        var slots = await _saveService.GetAllSaveSlotsAsync();
        var slotList = slots.ToList();

        // 生成 12 个槽位（如果已有存档则显示，否则显示空槽）
        for (int i = 1; i <= 12; i++)
        {
            var slotId = $"slot_{i}";
            var slotInfo = slotList.FirstOrDefault(s => s.SlotId == slotId);
            var panel = CreateSlotPanel(slotId, i, slotInfo);
            _slotsPanel.Children.Add(panel);
        }
    }

    /// <summary>创建单个存档槽面板</summary>
    private Border CreateSlotPanel(string slotId, int index, SaveSlotInfo? info)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, 60, 60, 80)),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12),
            MinHeight = 80
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(50, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(128, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(0, GridUnitType.Auto));

        // 槽位编号
        var indexText = new TextBlock
        {
            Text = $"{index:00}",
            Foreground = new SolidColorBrush(Color.FromArgb(180, 150, 150, 200)),
            FontSize = 28,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        grid.Children.Add(indexText);
        Grid.SetColumn(indexText, 0);

        // 缩略图
        var thumbContainer = new Border
        {
            Width = 128,
            Height = 72,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(60, 30, 30, 40)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            ClipToBounds = true
        };

        if (info?.Thumbnail != null && info.Thumbnail.Length > 0)
        {
            try
            {
                using var ms = new System.IO.MemoryStream(info.Thumbnail);
                var bitmap = new Bitmap(ms);
                var img = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                thumbContainer.Child = img;
            }
            catch
            {
                // 缩略图数据损坏，显示占位图标
                thumbContainer.Child = new TextBlock
                {
                    Text = "🖼",
                    FontSize = 24,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromArgb(100, 120, 120, 140))
                };
            }
        }
        else
        {
            thumbContainer.Child = new TextBlock
            {
                Text = "🖼",
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromArgb(60, 120, 120, 140))
            };
        }

        grid.Children.Add(thumbContainer);
        Grid.SetColumn(thumbContainer, 1);

        // 存档信息
        var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 3 };

        if (info != null)
        {
            infoPanel.Children.Add(new TextBlock
            {
                Text = info.Name ?? $"存档 {index}",
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeight.Medium
            });
            infoPanel.Children.Add(new TextBlock
            {
                Text = info.UpdateTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                Foreground = new SolidColorBrush(Color.FromArgb(160, 160, 160, 180)),
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0)
            });
            if (!string.IsNullOrEmpty(info.GameVersion))
            {
                infoPanel.Children.Add(new TextBlock
                {
                    Text = $"v{info.GameVersion}",
                    Foreground = new SolidColorBrush(Color.FromArgb(100, 100, 120, 140)),
                    FontSize = 11
                });
            }
        }
        else
        {
            infoPanel.Children.Add(new TextBlock
            {
                Text = "—— 空槽位 ——",
                Foreground = new SolidColorBrush(Color.FromArgb(120, 120, 120, 140)),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        grid.Children.Add(infoPanel);
        Grid.SetColumn(infoPanel, 2);

        // 操作按钮
        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (_isSaveMode)
        {
            var saveBtn = new Button { Content = "保存", Margin = new Thickness(2), MinWidth = 60 };
            saveBtn.Click += async (_, _) => await DoSave(slotId);
            btnPanel.Children.Add(saveBtn);
        }
        else
        {
            if (info != null)
            {
                var loadBtn = new Button { Content = "读取", Margin = new Thickness(2), MinWidth = 60 };
                loadBtn.Click += async (_, _) => await DoLoad(slotId);
                btnPanel.Children.Add(loadBtn);

                var delBtn = new Button { Content = "删除", Margin = new Thickness(2), MinWidth = 60 };
                delBtn.Click += async (_, _) => await DoDelete(slotId);
                btnPanel.Children.Add(delBtn);
            }
        }

        grid.Children.Add(btnPanel);
        Grid.SetColumn(btnPanel, 3);
        border.Child = grid;

        return border;
    }

    /// <summary>执行保存</summary>
    private async Task DoSave(string slotId)
    {
        _controller?.Save(slotId);
        await Task.Delay(300); // 等待保存完成
        RefreshSlots();
    }

    /// <summary>执行读取</summary>
    private async Task DoLoad(string slotId)
    {
        _controller?.Load(slotId);
        await Task.Delay(300);
        Hide();
        Closed?.Invoke();
    }

    /// <summary>执行删除</summary>
    private async Task DoDelete(string slotId)
    {
        if (_saveService != null)
            await _saveService.DeleteAsync(slotId);
        RefreshSlots();
    }
}
