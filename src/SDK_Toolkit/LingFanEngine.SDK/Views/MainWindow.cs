using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.SDK.ViewModels;
using MFToolkit.Routing;
using MFToolkit.Routing.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace LingFanEngine.SDK.Views;

/// <summary>
/// 主窗口——暗色主题 IDE 风格 + 窗口状态持久化
/// </summary>
public class MainWindow : Window
{
    private readonly IRouter _router;
    private readonly ContentControl _contentHost;
    private readonly IServiceProvider _services;
    private Button? _activeNavButton;

    // 暗色主题颜色
    private static readonly IBrush s_titleBarBg = new SolidColorBrush(Color.Parse("#2D2D30"));
    private static readonly IBrush s_navBg = new SolidColorBrush(Color.Parse("#252526"));
    private static readonly IBrush s_navItemNormal = new SolidColorBrush(Color.Parse("#252526"));
    private static readonly IBrush s_navItemHover = new SolidColorBrush(Color.Parse("#2D2D30"));
    private static readonly IBrush s_navItemActive = new SolidColorBrush(Color.Parse("#0E639C"));
    private static readonly IBrush s_contentBg = new SolidColorBrush(Color.Parse("#1E1E1E"));
    private static readonly IBrush s_textColor = new SolidColorBrush(Color.Parse("#CCCCCC"));
    private static readonly IBrush s_textActive = new SolidColorBrush(Color.Parse("#FFFFFF"));

    // 导航项配置
    private record NavItem(string Title, string Route, string Icon);
    private static readonly NavItem[] s_navItems =
    [
        new("项目管理", "/project", "\uE7C3"),  // 文件夹图标
        new("故事编辑", "/editor", "\uE734"),   // 编辑图标
        new("资源管理", "/assets", "\uE8B7"),   // 图片图标
        new("构建发布", "/build", "\uE7B8"),    // 打包图标
        new("设置", "/settings", "\uE713"),     // 设置图标
    ];

    public MainWindow(IServiceProvider services)
    {
        _services = services;
        _router = services.GetRequiredService<IRouter>();

        Title = "灵泛引擎 SDK";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // 加载持久化窗口状态
        LoadWindowState();

        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        DataContext = viewModel;

        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("220,*"),
            Background = s_contentBg,
        };

        // ===== 左侧导航栏 =====
        var navPanel = new StackPanel
        {
            Background = s_navBg,
        };

        // Logo 区域
        var logoPanel = new Border
        {
            Background = s_titleBarBg,
            Padding = new Thickness(16, 20, 16, 20),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "\uE7B7",  // Rocket icon
                        FontFamily = FontFamily.Parse("Segoe MDL2 Assets"),
                        FontSize = 20,
                        Foreground = s_textActive,
                    },
                    new TextBlock
                    {
                        Text = "灵泛引擎 SDK",
                        FontSize = 15,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = s_textActive,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };
        navPanel.Children.Add(logoPanel);

        // 导航按钮
        foreach (var item in s_navItems)
        {
            var navButton = CreateNavButton(item, viewModel);
            navPanel.Children.Add(navButton);
        }

        grid.Children.Add(navPanel);

        // ===== 右侧内容区 =====
        _contentHost = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = s_contentBg,
        };
        Grid.SetColumn(_contentHost, 1);
        grid.Children.Add(_contentHost);

        Content = grid;

        // 订阅路由导航事件
        _router.Navigated += OnNavigated;

        // 初始导航到项目页
        _ = _router.NavigateAsync("/project");

        // 窗口关闭时保存状态
        Closed += (_, _) => SaveWindowState();
    }

    /// <summary>创建暗色导航按钮</summary>
    private Button CreateNavButton(NavItem item, MainWindowViewModel viewModel)
    {
        var btn = new Button
        {
            Background = s_navItemNormal,
            Foreground = s_textColor,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            FontSize = 13,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = item.Icon,
                        FontFamily = FontFamily.Parse("Segoe MDL2 Assets"),
                        FontSize = 16,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = item.Title,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
            Command = viewModel.NavigateCommand,
            CommandParameter = item.Route,
            Tag = item.Route,
        };

        btn.PointerEntered += (_, _) =>
        {
            if (_activeNavButton != btn)
                btn.Background = s_navItemHover;
        };
        btn.PointerExited += (_, _) =>
        {
            if (_activeNavButton != btn)
                btn.Background = s_navItemNormal;
        };
        btn.Click += (_, _) =>
        {
            UpdateActiveNavButton(btn);
        };

        return btn;
    }

    /// <summary>更新导航高亮</summary>
    private void UpdateActiveNavButton(Button btn)
    {
        if (_activeNavButton != null)
        {
            _activeNavButton.Background = s_navItemNormal;
            _activeNavButton.Foreground = s_textColor;
        }
        _activeNavButton = btn;
        btn.Background = s_navItemActive;
        btn.Foreground = s_textActive;
    }

    /// <summary>路由导航完成时更新内容区</summary>
    private void OnNavigated(object? sender, MFToolkit.Routing.NavigationEventArgs e)
    {
        if (e.To?.PageInstance is Control page)
        {
            _contentHost.Content = page;
        }

        // 更新导航高亮
        var route = e.To?.Entity.RoutePath ?? "";
        var mainGrid = Content as Grid;
        if (mainGrid?.Children[0] is StackPanel navPanel)
        {
            foreach (var child in navPanel.Children)
            {
                if (child is Button btn && btn.Tag?.ToString() == route)
                {
                    UpdateActiveNavButton(btn);
                    break;
                }
            }
        }
    }

    // ===== 窗口状态持久化 =====

    private static string StateFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LingFanEngine", "sdk_window_state.json");

    private void LoadWindowState()
    {
        try
        {
            if (!File.Exists(StateFilePath))
            {
                Width = 1280;
                Height = 800;
                return;
            }

            var json = File.ReadAllText(StateFilePath);
            var parts = json.Split(',');
            if (parts.Length >= 4)
            {
                Width = double.TryParse(parts[0], out var w) ? w : 1280;
                Height = double.TryParse(parts[1], out var h) ? h : 800;
                if (double.TryParse(parts[2], out var x) && double.TryParse(parts[3], out var y))
                {
                    Position = new PixelPoint((int)x, (int)y);
                }
            }
            else
            {
                Width = 1280;
                Height = 800;
            }
        }
        catch
        {
            Width = 1280;
            Height = 800;
        }
    }

    private void SaveWindowState()
    {
        try
        {
            var dir = Path.GetDirectoryName(StateFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var pos = Position;
            File.WriteAllText(StateFilePath, $"{Width},{Height},{pos.X},{pos.Y}");
        }
        catch
        {
            // 持久化失败——忽略
        }
    }
}
