using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.ViewModels;

namespace LingFanEngine.SDK.Views;

/// <summary>
/// 启动器窗口——项目选择/创建入口（Godot 风格）。
/// </summary>
public class LauncherWindow : Window
{
    private readonly LauncherViewModel _viewModel;

    // 暗色主题
    private static readonly IBrush s_bg = new SolidColorBrush(Color.Parse("#1E1E1E"));
    private static readonly IBrush s_cardBg = new SolidColorBrush(Color.Parse("#252526"));
    private static readonly IBrush s_cardHover = new SolidColorBrush(Color.Parse("#2D2D30"));
    private static readonly IBrush s_accent = new SolidColorBrush(Color.Parse("#0E639C"));
    private static readonly IBrush s_accentText = new SolidColorBrush(Color.Parse("#4FC1FF"));
    private static readonly IBrush s_text = new SolidColorBrush(Color.Parse("#CCCCCC"));
    private static readonly IBrush s_textDim = new SolidColorBrush(Color.Parse("#888888"));
    private static readonly IBrush s_titleBar = new SolidColorBrush(Color.Parse("#2D2D30"));
    private static readonly IBrush s_btnBg = new SolidColorBrush(Color.Parse("#3A3D41"));

    // Segoe MDL2 图标字符
    private const string IconRocket = "\uE7B7";   // Rocket
    private const string IconAdd = "\uE710";      // Add/Plus
    private const string IconFolder = "\uE8B7";   // Folder
    private const string IconSearch = "\uE721";   // Search
    private const string IconPackage = "\uE7B8";  // Package

    public LauncherWindow(LauncherViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        Title = "灵泛引擎 SDK — 启动器";
        Width = 900;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        MinWidth = 700;
        MinHeight = 480;
        Background = s_bg;

        InitializeComponent();
    }

    private void InitializeComponent()
    {
        var rootGrid = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"),
        };

        // ===== 标题栏 =====
        var titleBar = CreateTitleBar();
        rootGrid.Children.Add(titleBar);
        Grid.SetRow(titleBar, 0);

        // ===== 主体 =====
        var body = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("240,*"),
        };
        Grid.SetRow(body, 1);

        var sidebar = CreateSidebar();
        body.Children.Add(sidebar);
        Grid.SetColumn(sidebar, 0);

        var projectGrid = CreateProjectGrid();
        body.Children.Add(projectGrid);
        Grid.SetColumn(projectGrid, 1);

        rootGrid.Children.Add(body);

        // ===== 底部状态栏 =====
        var statusBar = CreateStatusBar();
        rootGrid.Children.Add(statusBar);
        Grid.SetRow(statusBar, 2);

        Content = rootGrid;
    }

    /// <summary>标题栏</summary>
    private Control CreateTitleBar()
    {
        return new Border
        {
            Background = s_titleBar,
            Padding = new Thickness(24, 16, 24, 16),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = IconRocket,
                        FontFamily = FontFamily.Parse("Segoe MDL2 Assets"),
                        FontSize = 28,
                        Foreground = s_accentText,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "灵泛引擎 SDK",
                                FontSize = 20,
                                FontWeight = FontWeight.SemiBold,
                                Foreground = Brushes.White,
                            },
                            new TextBlock
                            {
                                Text = "叙事 / 沙盒小说游戏创作工具",
                                FontSize = 12,
                                Foreground = s_textDim,
                            },
                        }
                    }
                }
            }
        };
    }

    /// <summary>左侧操作侧边栏</summary>
    private Control CreateSidebar()
    {
        var panel = new StackPanel
        {
            Background = s_cardBg,
            Spacing = 8,
        };

        // ===== 新建项目区域 =====
        var newProjectSection = new StackPanel
        {
            Margin = new Thickness(12, 16, 12, 8),
            Spacing = 8,
        };

        var newBtn = CreateIconButtonWithText(IconAdd, "新建项目", s_accent, Brushes.White);
        newBtn.Click += (_, _) => _viewModel.ToggleNewProjectPanelCommand.Execute(null);
        newProjectSection.Children.Add(newBtn);

        // 新建项目表单（折叠面板）
        var formPanel = new StackPanel
        {
            Spacing = 6,
            IsVisible = false,
        };

        // AOT 安全：手动监听 IsNewProjectPanelVisible
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LauncherViewModel.IsNewProjectPanelVisible))
                formPanel.IsVisible = _viewModel.IsNewProjectPanelVisible;
        };

        var (namePanel, nameBox) = CreateFormField("项目名称");
        BindTwoWayText(nameBox, () => _viewModel.NewProjectName, v => _viewModel.NewProjectName = v, nameof(LauncherViewModel.NewProjectName));
        formPanel.Children.Add(namePanel);

        var (titlePanel, titleBox) = CreateFormField("游戏名称");
        BindTwoWayText(titleBox, () => _viewModel.NewProjectTitle, v => _viewModel.NewProjectTitle = v, nameof(LauncherViewModel.NewProjectTitle));
        formPanel.Children.Add(titlePanel);

        var (authorPanel, authorBox) = CreateFormField("作者");
        BindTwoWayText(authorBox, () => _viewModel.NewProjectAuthor, v => _viewModel.NewProjectAuthor = v, nameof(LauncherViewModel.NewProjectAuthor));
        formPanel.Children.Add(authorPanel);

        var (versionPanel, versionBox) = CreateFormField("版本号");
        BindTwoWayText(versionBox, () => _viewModel.NewProjectVersion, v => _viewModel.NewProjectVersion = v, nameof(LauncherViewModel.NewProjectVersion));
        formPanel.Children.Add(versionPanel);

        var (descPanel, descBox) = CreateFormField("描述（可选）");
        BindTwoWayText(descBox, () => _viewModel.NewProjectDescription, v => _viewModel.NewProjectDescription = v, nameof(LauncherViewModel.NewProjectDescription));
        formPanel.Children.Add(descPanel);

        // 路径选择
        var pathPanel = new StackPanel { Spacing = 4 };
        pathPanel.Children.Add(new TextBlock
        {
            Text = "输出目录",
            FontSize = 11,
            Foreground = s_textDim,
        });
        var pathRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
        };
        var pathBox = new TextBox { FontSize = 12 };
        BindTwoWayText(pathBox, () => _viewModel.NewProjectPath, v => _viewModel.NewProjectPath = v, nameof(LauncherViewModel.NewProjectPath));
        Grid.SetColumn(pathBox, 0);
        pathRow.Children.Add(pathBox);

        var browseBtn = new Button
        {
            Content = "...",
            Padding = new Thickness(8, 4),
            Background = s_btnBg,
            Foreground = s_text,
            BorderThickness = new Thickness(0),
        };
        browseBtn.Click += (_, _) => _viewModel.BrowseOutputDirCommand.Execute(null);
        Grid.SetColumn(browseBtn, 1);
        pathRow.Children.Add(browseBtn);

        pathPanel.Children.Add(pathRow);
        formPanel.Children.Add(pathPanel);

        // 创建按钮
        var createBtn = new Button
        {
            Content = "创建",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = s_accent,
            Foreground = Brushes.White,
            Padding = new Thickness(12, 8),
            FontSize = 13,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 4, 0, 0),
        };
        createBtn.Click += async (_, _) => await _viewModel.CreateProjectCommand.ExecuteAsync(null);
        formPanel.Children.Add(createBtn);

        newProjectSection.Children.Add(formPanel);
        panel.Children.Add(newProjectSection);

        // 分隔线
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.Parse("#3C3C3C")),
            Margin = new Thickness(12, 0),
        });

        // 打开按钮
        var openBtn = CreateIconButtonWithText(IconFolder, "打开项目...", Brushes.Transparent, s_text);
        openBtn.Margin = new Thickness(12, 8, 12, 0);
        openBtn.Click += (_, _) => _viewModel.BrowseProjectCommand.Execute(null);
        panel.Children.Add(openBtn);

        return panel;
    }

    /// <summary>创建带图标+文字的按钮</summary>
    private Button CreateIconButtonWithText(string icon, string text, IBrush bg, IBrush fg)
    {
        return new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = icon,
                        FontFamily = FontFamily.Parse("Segoe MDL2 Assets"),
                        FontSize = 16,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = text,
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                }
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = bg,
            Foreground = fg,
            Padding = new Thickness(12, 10),
            BorderThickness = new Thickness(0),
        };
    }

    /// <summary>创建表单字段（标签+文本框），返回(容器, 文本框)</summary>
    private (Control Panel, TextBox Box) CreateFormField(string label)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = s_textDim,
        });
        var box = new TextBox { FontSize = 12 };
        stack.Children.Add(box);
        return (stack, box);
    }

    /// <summary>AOT 安全的双向文本绑定（VM→TextBox + TextBox→VM，含 PropertyChanged 订阅）</summary>
    private void BindTwoWayText(TextBox box, Func<string> getter, Action<string> setter, string propertyName)
    {
        // VM → TextBox（初始 + PropertyChanged 响应）
        box.Text = getter();
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == propertyName && box.Text != getter())
                box.Text = getter();
        };
        // TextBox → VM
        box.TextChanged += (_, _) => setter(box.Text);
    }

    /// <summary>项目卡片网格</summary>
    private Control CreateProjectGrid()
    {
        var grid = new Grid { Background = s_bg };

        // 空状态提示
        var emptyHint = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 12,
            IsVisible = false,
        };
        emptyHint.Children.Add(new TextBlock
        {
            Text = IconPackage,
            FontFamily = FontFamily.Parse("Segoe MDL2 Assets"),
            FontSize = 48,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = s_textDim,
        });
        emptyHint.Children.Add(new TextBlock
        {
            Text = "还没有项目\n点击左侧「新建项目」或「打开项目」开始",
            FontSize = 14,
            Foreground = s_textDim,
            TextAlignment = TextAlignment.Center,
        });
        grid.Children.Add(emptyHint);

        // 项目列表
        var scrollViewer = new ScrollViewer
        {
            Padding = new Thickness(24, 16),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var itemsControl = new ItemsControl
        {
            ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel
            {
                ItemWidth = 260,
                ItemHeight = 140,
                ItemSpacing = 12,
                LineSpacing = 12,
            }),
        };

        // AOT 安全：手动管理 ItemsSource
        itemsControl.ItemsSource = _viewModel.RecentProjects;
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LauncherViewModel.RecentProjects))
            {
                // RecentProjects 是同一个 ObservableCollection 引用，ItemsControl 会自动更新
            }
        };

        // 空状态可见性
        UpdateEmptyHintVisibility(emptyHint, _viewModel.RecentProjects.Count);
        ((System.Collections.Specialized.INotifyCollectionChanged)_viewModel.RecentProjects).CollectionChanged += (_, _) =>
        {
            UpdateEmptyHintVisibility(emptyHint, _viewModel.RecentProjects.Count);
        };

        itemsControl.ItemTemplate = new FuncDataTemplate<RecentProject>((item, _) =>
        {
            var card = new Border
            {
                Background = s_cardBg,
                CornerRadius = new CornerRadius(6),
                BorderBrush = new SolidColorBrush(Color.Parse("#3C3C3C")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16),
                Cursor = new Cursor(StandardCursorType.Hand),
            };

            var cardContent = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"),
            };

            // 标题行
            var titleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = IconPackage,
                        FontFamily = FontFamily.Parse("Segoe MDL2 Assets"),
                        FontSize = 20,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = s_accentText,
                    },
                    new TextBlock
                    {
                        Text = item?.Name ?? "",
                        FontSize = 16,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center,
                    }
                }
            };
            cardContent.Children.Add(titleRow);

            // 路径
            var pathText = new TextBlock
            {
                Text = item?.Path ?? "",
                FontSize = 11,
                Foreground = s_textDim,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 8, 0, 0),
            };
            Grid.SetRow(pathText, 1);
            cardContent.Children.Add(pathText);

            // 底部信息行
            var bottomRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = item?.LastOpened.ToString("yyyy-MM-dd HH:mm") ?? "",
                        FontSize = 11,
                        Foreground = s_textDim,
                    },
                    new TextBlock
                    {
                        Text = "> 打开",
                        FontSize = 11,
                        Foreground = s_accentText,
                    }
                }
            };
            Grid.SetRow(bottomRow, 2);
            cardContent.Children.Add(bottomRow);

            card.Child = cardContent;

            // 交互
            card.PointerEntered += (_, _) => card.Background = s_cardHover;
            card.PointerExited += (_, _) => card.Background = s_cardBg;
            card.PointerPressed += async (_, e) =>
            {
                if (e.Pointer.IsPrimary)
                {
                    await _viewModel.OpenProjectCommand.ExecuteAsync(item?.Path);
                }
            };

            return card;
        });

        scrollViewer.Content = itemsControl;
        grid.Children.Add(scrollViewer);

        return grid;
    }

    /// <summary>更新空状态提示可见性</summary>
    private static void UpdateEmptyHintVisibility(Control hint, int count)
    {
        hint.IsVisible = count == 0;
    }

    /// <summary>底部状态栏</summary>
    private Control CreateStatusBar()
    {
        var statusText = new TextBlock
        {
            FontSize = 11,
            Foreground = s_textDim,
        };

        // AOT 安全：手动监听 StatusMessage
        statusText.Text = _viewModel.StatusMessage;
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LauncherViewModel.StatusMessage))
                statusText.Text = _viewModel.StatusMessage;
        };

        return new Border
        {
            Background = s_titleBar,
            BorderBrush = new SolidColorBrush(Color.Parse("#3C3C3C")),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 4),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 16,
                Children =
                {
                    statusText,
                    new TextBlock { Text = "|", FontSize = 11, Foreground = s_textDim },
                    new TextBlock { Text = "SDK v0.1.1", FontSize = 11, Foreground = s_textDim },
                    new TextBlock { Text = "|", FontSize = 11, Foreground = s_textDim },
                    new TextBlock { Text = ".NET 10", FontSize = 11, Foreground = s_textDim },
                }
            }
        };
    }
}
