using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Entry.UI;

/// <summary>
/// CG鉴赏面板（Demo 层 UI）
/// <para>从 StateKeys.Gallery.Unlocked 读取已解锁 CG 列表，以网格形式展示。</para>
/// <para>点击 CG 可全屏查看，带标题和场景信息。</para>
/// </summary>
public class GalleryPanel : UserControl
{
    private readonly IStateContainer _state;
    private readonly GameController? _controller;
    private ScrollViewer? _scrollViewer;
    private WrapPanel? _cgWrapPanel;
    private Border? _fullViewBorder;
    private Image? _fullViewImage;

    /// <summary>面板关闭事件</summary>
    public event Action? Closed;

    public GalleryPanel(IStateContainer state, GameController? controller)
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
            Background = new SolidColorBrush(Color.FromArgb(240, 25, 25, 35)),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(40),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 标题栏
        var titleBar = new Grid();
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "🖼 CG鉴赏",
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 200, 100)),
            Margin = new Thickness(20, 15, 20, 10)
        };
        Grid.SetColumn(title, 0);
        titleBar.Children.Add(title);

        var closeBtn = new Button
        {
            Content = "✕",
            FontSize = 16,
            Margin = new Thickness(0, 10, 15, 10),
            Padding = new Thickness(10, 4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        closeBtn.Click += (_, _) => { Hide(); Closed?.Invoke(); };
        Grid.SetColumn(closeBtn, 1);
        titleBar.Children.Add(closeBtn);

        Grid.SetRow(titleBar, 0);
        grid.Children.Add(titleBar);

        // CG 网格区域
        _cgWrapPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10)
        };

        _scrollViewer = new ScrollViewer
        {
            Content = _cgWrapPanel,
            Margin = new Thickness(10, 0, 10, 10),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(_scrollViewer, 1);
        grid.Children.Add(_scrollViewer);

        contentBorder.Child = grid;
        mainPanel.Children.Add(contentBorder);

        // 全屏查看层
        _fullViewBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(250, 0, 0, 0)),
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _fullViewImage = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20)
        };
        _fullViewBorder.Child = _fullViewImage;
        _fullViewBorder.PointerPressed += (_, _) => { _fullViewBorder.IsVisible = false; };
        mainPanel.Children.Add(_fullViewBorder);

        Content = mainPanel;
    }

    /// <summary>显示面板</summary>
    public void Show()
    {
        RefreshGallery();
        IsVisible = true;
    }

    /// <summary>隐藏面板</summary>
    public void Hide()
    {
        IsVisible = false;
        _fullViewBorder!.IsVisible = false;
    }

    /// <summary>刷新 CG 列表</summary>
    public void RefreshGallery()
    {
        if (_cgWrapPanel == null) return;
        _cgWrapPanel.Children.Clear();

        var entries = _state.Get<List<GalleryEntry>>(StateKeys.Gallery.Unlocked) ?? [];

        if (entries.Count == 0)
        {
            _cgWrapPanel.Children.Add(new TextBlock
            {
                Text = "暂无已解锁的 CG",
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromArgb(150, 200, 200, 200)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 50, 0, 0)
            });
            return;
        }

        foreach (var entry in entries)
        {
            var cgCard = CreateCgCard(entry);
            _cgWrapPanel.Children.Add(cgCard);
        }
    }

    /// <summary>创建单个 CG 卡片</summary>
    private Control CreateCgCard(GalleryEntry entry)
    {
        var card = new Border
        {
            Width = 200,
            Height = 160,
            Margin = new Thickness(8),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(60, 50, 50, 70)),
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // CG 缩略图
        var img = new Image
        {
            Stretch = Stretch.UniformToFill,
            Margin = new Thickness(2)
        };
        try
        {
            if (System.IO.File.Exists(entry.ImagePath))
                img.Source = new Bitmap(entry.ImagePath);
        }
        catch { /* 图片加载失败，显示占位 */ }
        Grid.SetRow(img, 0);
        grid.Children.Add(img);

        // 标题
        var title = new TextBlock
        {
            Text = entry.Title ?? entry.Id,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(200, 220, 220, 220)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(2, 4, 2, 4),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(title, 1);
        grid.Children.Add(title);

        card.Child = grid;

        // 点击查看大图
        card.PointerPressed += (_, _) =>
        {
            if (_fullViewImage != null && _fullViewBorder != null)
            {
                try
                {
                    if (System.IO.File.Exists(entry.ImagePath))
                        _fullViewImage.Source = new Bitmap(entry.ImagePath);
                }
                catch { }
                _fullViewBorder.IsVisible = true;
            }
        };

        return card;
    }
}
