using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.SDK.I18n;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Themes;
using LingFanEngine.SDK.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using MFToolkit.Routing;
using MFToolkit.Routing.Core.Interfaces;
using RouteNavigationEventArgs = MFToolkit.Routing.NavigationEventArgs;

namespace LingFanEngine.SDK.Views;

/// <summary>
/// 工作台窗口——VS Code 风格三栏布局（活动栏 + 侧面板 + 编辑区 + 状态栏）。
/// <para>通过 IRouter.NavigateAsync 导航，订阅 Navigated 事件切换 UI。</para>
/// <para>Router 自动创建 Page 和 ViewModel 实例，并触发 INavigationAware 生命周期。</para>
/// </summary>
public class WorkspaceWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly IRouter _router;
    private readonly IProjectSession _session;
    private readonly ContentControl _sidePanel;
    private readonly ContentControl _editorArea;
    private readonly TextBlock _statusBarText;
    private readonly TextBlock _projectNameText;

    // 侧面板实例缓存
    private Control? _assetCategoryPanel;
    private Control? _buildConfigPanel;

    // 活动栏按钮（按路由路径索引，供高亮；避免神奇下标 Children[N]）
    private readonly Dictionary<string, Button> _activityButtons = new();

    // 当前活动路由路径（用于活动栏高亮）
    private string _currentRoute = "";

    // 统一色板：取自 Themes/Colors.axaml（C# 经 ThemePalette.* 读取）——不再页内硬编码 s_* 画笔
    // 见 LingFanEngine.SDK.Themes.Theme。

    // 活动栏项：图标 / 提示 / 路由路径
    private record ActivityItem(string Icon, string Title, string RoutePath);
    private static readonly ActivityItem[] s_activities =
    [
        new("\uE8B7", SdkLocalizer.Loc("Nav_Assets"), "/assets"),
        new("\uE72E", SdkLocalizer.Loc("Nav_Translation"), "/translation"),
        new("\uE8A5", SdkLocalizer.Loc("Nav_Models"), "/models"),
        new("\uE7B8", SdkLocalizer.Loc("Nav_Build"), "/build"),
        new("\uE713", SdkLocalizer.Loc("Nav_Settings"), "/settings"),
    ];

    public WorkspaceWindow(IServiceProvider services)
    {
        _services = services;
        _router = services.GetRequiredService<IRouter>();
        _session = services.GetRequiredService<IProjectSession>();

        Title = SdkLocalizer.Loc("Ws_Title");
        // 应用默认图标（与 EXE ApplicationIcon 一致）
        Icon = new WindowIcon(Path.Combine(AppContext.BaseDirectory, "Icons", "LingFanIcon_64x64.png"));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = 1280;
        Height = 800;
        MinWidth = 900;
        MinHeight = 600;
        Background = ThemePalette.EditorBg;

        // 组件
        _sidePanel = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        _editorArea = new ContentControl
        {
            Background = ThemePalette.EditorBg,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        _statusBarText = new TextBlock
        {
            FontSize = 12,
            Foreground = ThemePalette.TextBright,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _projectNameText = new TextBlock
        {
            FontSize = 12,
            Foreground = ThemePalette.TextBright,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.Medium,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // 加载窗口状态
        LoadWindowState();

        // 监听项目变化
        _session.ProjectOpened += OnProjectOpened;
        _session.ProjectClosed += OnProjectClosed;

        InitializeComponent();

        // 订阅 Router 导航事件——Router 创建页面和 VM 实例后触发
        _router.Navigated += OnRouterNavigated;

        // 语言切换 → 刷新状态栏/标题/活动栏提示等 code-behind 文案
        SdkLocalizer.CultureChanged += Relocalize;

        // 初始导航到资源管理
        _ = _router.NavigateAsync("/assets");

        // 更新项目名显示
        UpdateProjectDisplay();

        Closed += OnWindowClosed;
    }

    // ===== Router 导航事件处理 =====

    /// <summary>Router 导航完成后的 UI 切换</summary>
    private void OnRouterNavigated(object? sender, RouteNavigationEventArgs args)
    {
        if (args.To?.PageInstance is not Control page) return;

        var routePath = args.To.Entity.RoutePath ?? "";

        // 确保 ViewModel 绑定
        if (args.To.ViewModelInstance != null)
        {
            page.DataContext = args.To.ViewModelInstance;
        }

        // 切换编辑区内容
        // 防止 keepalive 复用的页面实例被重复挂到同一 ContentPresenter（重复导航/重入会抛
        // "The control ... already has a visual parent ContentPresenter"）：同实例则跳过。
        if (!ReferenceEquals(_editorArea.Content, page))
            _editorArea.Content = page;

        // 更新当前路由
        _currentRoute = routePath;

        // 更新活动栏高亮
        UpdateActivityBarHighlight();

        // 切换侧面板（使用缓存的实例）
        _sidePanel.Content = routePath switch
        {
            "/assets" => _assetCategoryPanel ??= CreateAssetCategoryPanel(),
            "/build" => _buildConfigPanel ??= CreateBuildConfigPanel(),
            _ => null
        };

        // 更新状态栏
        _statusBarText.Text = RouteLabel(routePath);
    }

    /// <summary>当前路由对应的本地化文案（状态栏/活动栏提示共用）。</summary>
    private static string RouteLabel(string routePath) => routePath switch
    {
        "/assets" => SdkLocalizer.Loc("Nav_Assets"),
        "/translation" => SdkLocalizer.Loc("Nav_Translation"),
        "/models" => SdkLocalizer.Loc("Nav_Models"),
        "/build" => SdkLocalizer.Loc("Nav_Build"),
        "/settings" => SdkLocalizer.Loc("Nav_Settings"),
        _ => ""
    };

    /// <summary>语言切换：刷新窗口标题、状态栏路由文案与活动栏提示（code-behind 文案）。</summary>
    private void Relocalize()
    {
        UpdateProjectDisplay();
        _statusBarText.Text = RouteLabel(_currentRoute);
        foreach (var (path, btn) in _activityButtons)
            ToolTip.SetTip(btn, RouteLabel(path));
    }

    /// <summary>更新活动栏高亮（按存储按钮引用，杜绝神奇下标）</summary>
    private void UpdateActivityBarHighlight()
    {
        foreach (var (path, btn) in _activityButtons)
            btn.Foreground = path == _currentRoute ? ThemePalette.IconActive : ThemePalette.IconNormal;
    }

    private void InitializeComponent()
    {
        var rootGrid = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("*,Auto"),
        };

        // ===== 主内容区 =====
        var mainGrid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("48,Auto,220,Auto,*"),
        };

        // ===== 活动栏 (48px) =====
        var activityBar = CreateActivityBar();
        mainGrid.Children.Add(activityBar);
        Grid.SetColumn(activityBar, 0);

        // ===== 分隔线 =====
        var sep1 = new Border { Width = 1, Background = ThemePalette.EditorBg };
        mainGrid.Children.Add(sep1);
        Grid.SetColumn(sep1, 1);

        // ===== 侧面板 (220px) =====
        var sideBorder = new Border
        {
            Background = ThemePalette.PanelBg,
            Child = _sidePanel,
        };
        mainGrid.Children.Add(sideBorder);
        Grid.SetColumn(sideBorder, 2);

        // ===== 分隔线 (GridSplitter) =====
        var splitter = new GridSplitter
        {
            Width = 4,
            Background = ThemePalette.Splitter,
            ResizeDirection = GridResizeDirection.Columns,
        };
        mainGrid.Children.Add(splitter);
        Grid.SetColumn(splitter, 3);

        // ===== 编辑区 =====
        mainGrid.Children.Add(_editorArea);
        Grid.SetColumn(_editorArea, 4);

        rootGrid.Children.Add(mainGrid);

        // ===== 状态栏 =====
        var statusBar = CreateStatusBar();
        rootGrid.Children.Add(statusBar);
        Grid.SetRow(statusBar, 1);

        Content = rootGrid;
    }

    /// <summary>创建活动栏</summary>
    private Control CreateActivityBar()
    {
        var bar = new StackPanel
        {
            Background = ThemePalette.ActivityBarBg,
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Spacing = 2,
        };

        // 主导航按钮（含设置）
        foreach (var item in s_activities)
        {
            var btn = CreateActivityButton(item);
            bar.Children.Add(btn);
        }

        // 关闭项目按钮
        var closeBtn = CreateIconButton("\uE72C", SdkLocalizer.Loc("Action_CloseProject"), false);
        closeBtn.Click += (_, _) => CloseProject();
        closeBtn.Margin = new Thickness(0, 16, 0, 0);
        bar.Children.Add(closeBtn);

        return bar;
    }

    /// <summary>创建活动栏按钮</summary>
    private Button CreateActivityButton(ActivityItem item)
    {
        var btn = new Button
        {
            Background = Brushes.Transparent,
            Foreground = ThemePalette.IconNormal,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 48,
            Height = 48,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Tag = item.RoutePath,
            Classes = { "transparent" },
            Content = new TextBlock
            {
                Text = item.Icon,
                FontFamily = FontFamily.Parse("Segoe MDL2 Assets"),
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        btn.PointerEntered += (_, _) =>
        {
            if (_currentRoute != item.RoutePath)
                btn.Foreground = ThemePalette.TextHover;
        };
        btn.PointerExited += (_, _) =>
        {
            if (_currentRoute != item.RoutePath)
                btn.Foreground = ThemePalette.IconNormal;
        };
        btn.Click += async (_, _) => await _router.NavigateAsync(item.RoutePath);
        _activityButtons[item.RoutePath] = btn;
        // 活动栏图标窄 48×48、无标题栏——必须依赖 ToolTip 揭示功能；与 CreateIconButton 行为一致
        ToolTip.SetTip(btn, item.Title);

        return btn;
    }

    /// <summary>创建图标按钮（通用）</summary>
    private Button CreateIconButton(string icon, string tooltip, bool isActive)
    {
        var btn = new Button
        {
            Background = Brushes.Transparent,
            Foreground = isActive ? ThemePalette.IconActive : ThemePalette.IconNormal,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 48,
            Height = 48,
            Classes = { "transparent" },
            Content = new TextBlock
            {
                Text = icon,
                FontFamily = FontFamily.Parse("Segoe MDL2 Assets"),
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        ToolTip.SetTip(btn, tooltip);
        return btn;
    }

    /// <summary>关闭项目——回到启动器</summary>
    private void CloseProject()
    {
        _session.Close();
    }

    private void OnProjectOpened()
    {
        UpdateProjectDisplay();
    }

    private void OnProjectClosed()
    {
        // 由 App 负责窗口切换
    }

    private void UpdateProjectDisplay()
    {
        if (_session.IsProjectOpen && _session.CurrentProject != null)
        {
            _projectNameText.Text = $"  {_session.CurrentProject.Title}  —  {_session.CurrentProject.ProjectDirectory}";
            Title = SdkLocalizer.Loc("Ws_TitleProject", _session.CurrentProject.Title);
        }
        else
        {
            _projectNameText.Text = "  " + SdkLocalizer.Loc("Status_NoProject");
            Title = SdkLocalizer.Loc("Ws_Title");
        }
    }

    /// <summary>状态栏左段内层 Grid：项目名(*列，超宽省略号截断)  |  状态文本(Auto)。</summary>
    private Control CreateLeftStatusBar()
    {
        var sep = new TextBlock
        {
            Text = "|",
            FontSize = 12,
            Foreground = ThemePalette.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
        };
        var inner = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,Auto"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        inner.Children.Add(_projectNameText);
        Grid.SetColumn(_projectNameText, 0);
        inner.Children.Add(sep);
        Grid.SetColumn(sep, 1);
        inner.Children.Add(_statusBarText);
        Grid.SetColumn(_statusBarText, 2);
        return inner;
    }

    // ===== 侧面板创建 =====

    /// <summary>资源分类侧面板</summary>
    private Control CreateAssetCategoryPanel()
    {
        var panel = new StackPanel { Background = ThemePalette.PanelBg };

        panel.Children.Add(new Border
        {
            Background = ThemePalette.ActivityBarBg,
            Padding = new Thickness(12, 8),
            Child = new TextBlock
            {
                Text = SdkLocalizer.Loc("Asset_Category"),
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = ThemePalette.TextHover,
            }
        });

        var categories = new[]
        {
            SdkLocalizer.Loc("Asset_CatAll"), SdkLocalizer.Loc("Asset_CatStory"),
            SdkLocalizer.Loc("Asset_CatImage"), SdkLocalizer.Loc("Asset_CatAudio"),
            SdkLocalizer.Loc("Asset_CatVideo"), SdkLocalizer.Loc("Asset_CatOther"),
        };
        var list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 12,
        };
        foreach (var cat in categories)
            (list.Items as System.Collections.IList)?.Add(new TextBlock { Text = cat, Padding = new Thickness(12, 6) });

        // 从 DI 获取 ViewModel
        var vm = _services.GetService<AssetManagerViewModel>();
        if (vm != null)
        {
            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedIndex >= 0)
                    vm.FilterCategoryCommand?.Execute(list.SelectedIndex);
            };

            var scanBtn = new Button
            {
                Content = SdkLocalizer.Loc("Action_Scan"),
                Margin = new Thickness(8, 8, 8, 0),
                Background = ThemePalette.Accent,
                Foreground = ThemePalette.TextBright,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 6),
                FontSize = 12,
            };
            scanBtn.Click += (_, _) => vm.ScanAssetsCommand?.Execute(null);
            panel.Children.Add(list);
            panel.Children.Add(scanBtn);
        }
        else
        {
            panel.Children.Add(list);
        }

        return panel;
    }

    /// <summary>构建配置侧面板（概览 + 快捷构建按钮）</summary>
    private Control CreateBuildConfigPanel()
    {
        var panel = new StackPanel
        {
            Background = ThemePalette.PanelBg,
            Margin = new Thickness(8, 8, 8, 8),
            Spacing = 8,
        };

        panel.Children.Add(new TextBlock
        {
            Text = SdkLocalizer.Loc("Build_Overview"),
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = ThemePalette.TextHover,
        });

        var vm = _services.GetService<BuildViewModel>();
        if (vm != null)
        {
            // 平台摘要（只读）
            var platformSummary = new TextBlock
            {
                FontSize = 12,
                Foreground = ThemePalette.TextMuted,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            };
            void UpdatePlatformSummary()
            {
                var parts = new System.Collections.Generic.List<string>();
                if (vm.TargetWindows) parts.Add("Windows ✓");
                if (vm.TargetLinux) parts.Add("Linux ✓");
                if (vm.TargetMacOSArm64) parts.Add("macOS (arm64) ✓");
                if (vm.TargetMacOSX64) parts.Add("macOS (x64) ✓");
                platformSummary.Text = SdkLocalizer.Loc("Build_Platform",
                    parts.Count > 0 ? string.Join("  ", parts) : SdkLocalizer.Loc("Build_None"));
            }
            UpdatePlatformSummary();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(BuildViewModel.TargetWindows) or nameof(BuildViewModel.TargetLinux) or nameof(BuildViewModel.TargetMacOSArm64) or nameof(BuildViewModel.TargetMacOSX64))
                    UpdatePlatformSummary();
            };
            panel.Children.Add(platformSummary);

            // 加密摘要（只读）
            var encSummary = new TextBlock
            {
                FontSize = 12,
                Foreground = ThemePalette.TextMuted,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            };
            void UpdateEncSummary()
            {
                if (!vm.EnableEncryption)
                    encSummary.Text = SdkLocalizer.Loc("Build_EncNone");
                else if (!vm.EncryptResources)
                    encSummary.Text = SdkLocalizer.Loc("Build_EncNoRes");
                else
                {
                    var count = 0;
                    foreach (var item in vm.EncryptFileTypes)
                        if (item.IsChecked) count++;
                    encSummary.Text = SdkLocalizer.Loc("Build_EncRes", count);
                }
            }
            UpdateEncSummary();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(BuildViewModel.EnableEncryption) or nameof(BuildViewModel.EncryptResources))
                    UpdateEncSummary();
            };
            panel.Children.Add(encSummary);

            // AOT 摘要
            var aotSummary = new TextBlock
            {
                FontSize = 12,
                Foreground = ThemePalette.TextMuted,
            };
            void UpdateAotSummary() => aotSummary.Text = SdkLocalizer.Loc("Build_Aot",
                    SdkLocalizer.Loc(vm.PublishAot ? "Word_Enabled" : "Word_Disabled"));
            UpdateAotSummary();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(BuildViewModel.PublishAot))
                    UpdateAotSummary();
            };
            panel.Children.Add(aotSummary);

            panel.Children.Add(new Border { Height = 1, Background = ThemePalette.ActivityBarBg });

            // 构建按钮
            var buildBtn = new Button
            {
                Content = SdkLocalizer.Loc("Action_StartBuild"),
                Command = vm.BuildCommand,
                FontSize = 13,
                Padding = new Thickness(16, 6),
                Background = ThemePalette.Accent,
                Foreground = ThemePalette.TextBright,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            panel.Children.Add(buildBtn);

            // 进度条
            var progress = new ProgressBar
            {
                Height = 4,
                Value = vm.BuildProgress,
            };
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(BuildViewModel.BuildProgress))
                    progress.Value = vm.BuildProgress;
            };
            panel.Children.Add(progress);
        }

        return panel;
    }

    // ===== 状态栏 =====

    private Control CreateStatusBar()
    {
        var leftSection = CreateLeftStatusBar();
        var versionText = new TextBlock
        {
            Text = "灵泛引擎 SDK v0.1.0",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 4, 0),
        };
        var outerGrid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
        };
        outerGrid.Children.Add(leftSection);
        Grid.SetColumn(leftSection, 0);
        outerGrid.Children.Add(versionText);
        Grid.SetColumn(versionText, 1);

        return new Border
        {
            Background = ThemePalette.StatusBarBg,
            Padding = new Thickness(8, 2),
            Child = outerGrid,
        };
    }

    // ===== 窗口生命周期 =====

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        // 退订语言切换事件，避免多工作台窗口叠加订阅
        SdkLocalizer.CultureChanged -= Relocalize;

        // 释放当前挂载的页面与侧面板，使 Router keepalive 缓存的页实例在窗口关闭后失去可视父级，
        // 避免下一个项目打开时新建的工作台复用"仍被旧窗口挂载"的页实例而抛
        // "The control ... already has a visual parent ContentPresenter"。
        _editorArea.Content = null;
        _sidePanel.Content = null;

        SaveWindowState();
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
                    Position = new PixelPoint((int)x, (int)y);
            }
            else
            {
                Width = 1280;
                Height = 800;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkspaceWindow] 窗口状态加载失败: {ex.Message}");
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkspaceWindow] 窗口状态保存失败: {ex.Message}");
        }
    }
}
