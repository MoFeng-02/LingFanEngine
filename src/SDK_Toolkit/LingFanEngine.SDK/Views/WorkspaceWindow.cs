using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.ViewModels;
using LingFanEngine.SDK.Views.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace LingFanEngine.SDK.Views;

/// <summary>
/// 工作台窗口——VS Code 风格三栏布局（活动栏 + 侧面板 + 编辑区 + 状态栏）。
/// </summary>
public class WorkspaceWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly IProjectSession _session;
    private readonly ContentControl _sidePanel;
    private readonly ContentControl _editorArea;
    private readonly TextBlock _statusBarText;
    private readonly TextBlock _projectNameText;
    private int _activeIndex = 0;

    // 暗色主题
    private static readonly IBrush s_activityBarBg = new SolidColorBrush(Color.Parse("#333333"));
    private static readonly IBrush s_sideBarBg = new SolidColorBrush(Color.Parse("#252526"));
    private static readonly IBrush s_editorBg = new SolidColorBrush(Color.Parse("#1E1E1E"));
    private static readonly IBrush s_statusBarBg = new SolidColorBrush(Color.Parse("#007ACC"));
    private static readonly IBrush s_iconActive = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush s_iconNormal = new SolidColorBrush(Color.Parse("#858585"));
    private static readonly IBrush s_iconHover = new SolidColorBrush(Color.Parse("#CCCCCC"));
    private static readonly IBrush s_splitterBg = new SolidColorBrush(Color.Parse("#1E1E1E"));

    // 活动栏项
    private record ActivityItem(string Icon, string Title, ActivityPage Page);
    private static readonly ActivityItem[] s_activities =
    [
        new("\uE734", "故事编辑", ActivityPage.StoryEditor),
        new("\uE8B7", "资源管理", ActivityPage.Assets),
        new("\uE7B8", "构建发布", ActivityPage.Build),
    ];

    public WorkspaceWindow(IServiceProvider services)
    {
        _services = services;
        _session = services.GetRequiredService<IProjectSession>();

        Title = "灵泛引擎 SDK — 工作台";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = 1280;
        Height = 800;
        MinWidth = 900;
        MinHeight = 600;
        Background = s_editorBg;

        // 组件
        _sidePanel = new ContentControl();
        _editorArea = new ContentControl { Background = s_editorBg };
        _statusBarText = new TextBlock
        {
            FontSize = 12,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _projectNameText = new TextBlock
        {
            FontSize = 12,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.Medium,
        };

        // 加载窗口状态
        LoadWindowState();

        // 监听项目变化
        _session.ProjectOpened += OnProjectOpened;
        _session.ProjectClosed += OnProjectClosed;

        InitializeComponent();

        // 默认选择故事编辑
        SelectActivity(0);

        // 更新项目名显示
        UpdateProjectDisplay();

        Closed += (_, _) => SaveWindowState();
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
        var sep1 = new Border { Width = 1, Background = new SolidColorBrush(Color.Parse("#1E1E1E")) };
        mainGrid.Children.Add(sep1);
        Grid.SetColumn(sep1, 1);

        // ===== 侧面板 (220px) =====
        var sideBorder = new Border
        {
            Background = s_sideBarBg,
            Child = _sidePanel,
        };
        mainGrid.Children.Add(sideBorder);
        Grid.SetColumn(sideBorder, 2);

        // ===== 分隔线 (GridSplitter) =====
        var splitter = new GridSplitter
        {
            Width = 4,
            Background = s_splitterBg,
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
            Background = s_activityBarBg,
            Orientation = Orientation.Vertical,
        };

        // 主导航按钮
        for (int i = 0; i < s_activities.Length; i++)
        {
            var item = s_activities[i];
            var btn = CreateActivityButton(item, i);
            bar.Children.Add(btn);
        }

        // 弹性间隔
        bar.Children.Add(new Border { Height = 8 });

        // 设置按钮
        var settingsBtn = CreateIconButton("\uE713", "设置", false);
        settingsBtn.Click += (_, _) => SelectActivity(-1);
        bar.Children.Add(settingsBtn);

        // 关闭项目按钮
        var closeBtn = CreateIconButton("\uE72C", "关闭项目", false);
        closeBtn.Click += (_, _) => CloseProject();
        bar.Children.Add(closeBtn);

        return bar;
    }

    /// <summary>创建活动栏按钮</summary>
    private Button CreateActivityButton(ActivityItem item, int index)
    {
        var btn = new Button
        {
            Background = Brushes.Transparent,
            Foreground = s_iconNormal,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 48,
            Height = 48,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Tag = index,
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
            if (_activeIndex != index)
                btn.Foreground = s_iconHover;
        };
        btn.PointerExited += (_, _) =>
        {
            if (_activeIndex != index)
                btn.Foreground = s_iconNormal;
        };
        btn.Click += (_, _) => SelectActivity(index);

        return btn;
    }

    /// <summary>创建图标按钮（通用）</summary>
    private Button CreateIconButton(string icon, string tooltip, bool isActive)
    {
        var btn = new Button
        {
            Background = Brushes.Transparent,
            Foreground = isActive ? s_iconActive : s_iconNormal,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 48,
            Height = 48,
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

    /// <summary>选择活动页</summary>
    private void SelectActivity(int index)
    {
        _activeIndex = index;

        // 更新活动栏高亮
        if (Content is Grid rootGrid && rootGrid.Children[0] is Grid mainGrid)
        {
            if (mainGrid.Children[0] is StackPanel bar)
            {
                for (int i = 0; i < s_activities.Length && i < bar.Children.Count; i++)
                {
                    if (bar.Children[i] is Button btn)
                    {
                        btn.Foreground = i == index ? s_iconActive : s_iconNormal;
                    }
                }
            }
        }

        // 切换内容
        switch (index)
        {
            case 0:
                ShowStoryEditor();
                break;
            case 1:
                ShowAssets();
                break;
            case 2:
                ShowBuild();
                break;
            case -1:
                ShowSettings();
                break;
        }
    }

    /// <summary>显示故事编辑器</summary>
    private void ShowStoryEditor()
    {
        var vm = _services.GetRequiredService<StoryEditorViewModel>();
        var page = new StoryEditorPage(vm);
        _editorArea.Content = page;
        _sidePanel.Content = CreateFileTreePanel(vm);
        _statusBarText.Text = "故事编辑器";
    }

    /// <summary>显示资源管理</summary>
    private void ShowAssets()
    {
        var vm = _services.GetRequiredService<AssetManagerViewModel>();
        var page = new AssetManagerPage(vm);
        _editorArea.Content = page;
        _sidePanel.Content = CreateAssetCategoryPanel(vm);
        _statusBarText.Text = "资源管理";
    }

    /// <summary>显示构建发布</summary>
    private void ShowBuild()
    {
        var vm = _services.GetRequiredService<BuildViewModel>();
        var page = new BuildPage(vm);
        _editorArea.Content = page;
        _sidePanel.Content = CreateBuildConfigPanel(vm);
        _statusBarText.Text = "构建发布";
    }

    /// <summary>显示设置</summary>
    private void ShowSettings()
    {
        var vm = _services.GetRequiredService<SettingsViewModel>();
        var page = new SettingsPage(vm);
        _editorArea.Content = page;
        _sidePanel.Content = null;
        _statusBarText.Text = "设置";
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
            Title = $"灵泛引擎 SDK — {_session.CurrentProject.Title}";
        }
        else
        {
            _projectNameText.Text = "  未打开项目";
            Title = "灵泛引擎 SDK — 工作台";
        }
    }

    // ===== 侧面板创建 =====

    /// <summary>文件树侧面板（故事编辑器）</summary>
    private Control CreateFileTreePanel(StoryEditorViewModel vm)
    {
        var panel = new StackPanel { Background = s_sideBarBg };

        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#333333")),
            Padding = new Thickness(12, 8),
            Child = new TextBlock
            {
                Text = "故事文件",
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            }
        });

        var fileTree = new Components.FileTreeView();
        if (vm.StoriesDirectory != null)
            fileTree.LoadDirectory(vm.StoriesDirectory);

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StoryEditorViewModel.StoriesDirectory)
                && vm.StoriesDirectory != null)
            {
                fileTree.LoadDirectory(vm.StoriesDirectory);
            }
        };

        fileTree.FileOpenRequested += async path => await vm.OpenFileCommand.ExecuteAsync(path);
        fileTree.CreateFileRequested += async dir => await vm.CreateNewFileCommand.ExecuteAsync(dir);

        panel.Children.Add(fileTree);
        return panel;
    }

    /// <summary>资源分类侧面板</summary>
    private Control CreateAssetCategoryPanel(AssetManagerViewModel vm)
    {
        var panel = new StackPanel { Background = s_sideBarBg };

        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#333333")),
            Padding = new Thickness(12, 8),
            Child = new TextBlock
            {
                Text = "资源分类",
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            }
        });

        var categories = new[] { "全部资源", "故事文件", "图片", "音频", "视频", "其他" };
        var list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 12,
        };
        foreach (var cat in categories)
            (list.Items as System.Collections.IList)?.Add(new TextBlock { Text = cat, Padding = new Thickness(12, 6) });

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex >= 0)
                vm.FilterCategoryCommand?.Execute(list.SelectedIndex);
        };

        panel.Children.Add(list);

        var scanBtn = new Button
        {
            Content = "扫描资源",
            Margin = new Thickness(8, 8, 8, 0),
            Background = new SolidColorBrush(Color.Parse("#0E639C")),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 6),
            FontSize = 12,
        };
        scanBtn.Click += (_, _) => vm.ScanAssetsCommand?.Execute(null);
        panel.Children.Add(scanBtn);

        return panel;
    }

    /// <summary>构建配置侧面板</summary>
    private Control CreateBuildConfigPanel(BuildViewModel vm)
    {
        var panel = new StackPanel
        {
            Background = s_sideBarBg,
            Margin = new Thickness(8, 8, 8, 8),
            Spacing = 8,
        };

        panel.Children.Add(new TextBlock
        {
            Text = "构建配置",
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
        });

        panel.Children.Add(new TextBlock { Text = "目标平台", FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#888888")) });

        var winCheck = new CheckBox { Content = "Windows", FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")) };
        winCheck.IsChecked = vm.TargetWindows;
        winCheck.IsCheckedChanged += (_, _) => vm.TargetWindows = winCheck.IsChecked ?? false;
        panel.Children.Add(winCheck);

        var linuxCheck = new CheckBox { Content = "Linux", FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")) };
        linuxCheck.IsChecked = vm.TargetLinux;
        linuxCheck.IsCheckedChanged += (_, _) => vm.TargetLinux = linuxCheck.IsChecked ?? false;
        panel.Children.Add(linuxCheck);

        var macCheck = new CheckBox { Content = "macOS", FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")) };
        macCheck.IsChecked = vm.TargetMacOS;
        macCheck.IsCheckedChanged += (_, _) => vm.TargetMacOS = macCheck.IsChecked ?? false;
        panel.Children.Add(macCheck);

        var encCheck = new CheckBox { Content = "启用加密", FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")) };
        encCheck.IsChecked = vm.EnableEncryption;
        encCheck.IsCheckedChanged += (_, _) => vm.EnableEncryption = encCheck.IsChecked ?? false;
        panel.Children.Add(encCheck);

        var aotCheck = new CheckBox { Content = "AOT 发布", FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")) };
        aotCheck.IsChecked = vm.PublishAot;
        aotCheck.IsCheckedChanged += (_, _) => vm.PublishAot = aotCheck.IsChecked ?? false;
        panel.Children.Add(aotCheck);

        return panel;
    }

    // ===== 状态栏 =====

    private Control CreateStatusBar()
    {
        return new Border
        {
            Background = s_statusBarBg,
            Padding = new Thickness(8, 2),
            Child = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 16,
                        Children =
                        {
                            _projectNameText,
                            new TextBlock { Text = "|", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)) },
                            _statusBarText,
                        }
                    },
                    new TextBlock
                    {
                        Text = "灵泛引擎 SDK v0.1.0",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
                        VerticalAlignment = VerticalAlignment.Center,
                    }
                }
            }
        };
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
        }
    }
}

/// <summary>活动页枚举</summary>
public enum ActivityPage
{
    StoryEditor,
    Assets,
    Build,
    Settings
}
