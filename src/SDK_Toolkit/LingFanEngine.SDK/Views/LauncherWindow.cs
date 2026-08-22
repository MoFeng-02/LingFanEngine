using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.IO;
using LingFanEngine.SDK.I18n;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Themes;
using LingFanEngine.SDK.ViewModels;

namespace LingFanEngine.SDK.Views;

/// <summary>
/// 启动器窗口——项目选择/创建入口（Godot 风格）。
/// </summary>
public class LauncherWindow : Window
{
    private readonly LauncherViewModel _viewModel;

    // 统一色板：取自 Themes/Colors.axaml（C# 经 ThemePalette.* 读取）——不再页内硬编码。

    // Segoe MDL2 图标字符
    private const string IconRocket = "\uE7B7";   // Rocket
    private const string IconAdd = "\uE710";      // Add/Plus
    private const string IconFolder = "\uE8B7";   // Folder
    private const string IconSearch = "\uE721";   // Search
    private const string IconPackage = "\uE7B8";  // Package
    private const string IconTrash = "\uE74D";    // Delete

    public LauncherWindow(LauncherViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        Title = SdkLocalizer.Loc("Window_Title");
        // 应用默认图标（与 EXE ApplicationIcon 一致）
        Icon = new WindowIcon(Path.Combine(AppContext.BaseDirectory, "Icons", "LingFanIcon_64x64.png"));
        Width = 900;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        MinWidth = 700;
        MinHeight = 480;
        Background = ThemePalette.EditorBg;

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
            Background = ThemePalette.PanelDeeper,
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
                        Foreground = ThemePalette.Title,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = SdkLocalizer.Loc("Brand_Title"),
                                FontSize = 20,
                                FontWeight = FontWeight.SemiBold,
                                Foreground = ThemePalette.TextBright,
                            },
                            new TextBlock
                            {
                                Text = SdkLocalizer.Loc("Brand_Tagline"),
                                FontSize = 12,
                                Foreground = ThemePalette.TextMuted,
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
            Background = ThemePalette.PanelBg,
            Spacing = 8,
        };

        // ===== 新建项目区域 =====
        var newProjectSection = new StackPanel
        {
            Margin = new Thickness(12, 16, 12, 8),
            Spacing = 8,
        };

        var newBtn = CreateIconButtonWithText(IconAdd, SdkLocalizer.Loc("Action_NewProject"), ThemePalette.Accent, ThemePalette.TextBright);
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

        var (namePanel, nameBox) = CreateFormField(SdkLocalizer.Loc("Form_ProjectName"));
        BindTwoWayText(nameBox, () => _viewModel.NewProjectName, v => _viewModel.NewProjectName = v, nameof(LauncherViewModel.NewProjectName));
        formPanel.Children.Add(namePanel);

        var (titlePanel, titleBox) = CreateFormField(SdkLocalizer.Loc("Form_GameTitle"));
        BindTwoWayText(titleBox, () => _viewModel.NewProjectTitle, v => _viewModel.NewProjectTitle = v, nameof(LauncherViewModel.NewProjectTitle));
        formPanel.Children.Add(titlePanel);

        var (authorPanel, authorBox) = CreateFormField(SdkLocalizer.Loc("Form_Author"));
        BindTwoWayText(authorBox, () => _viewModel.NewProjectAuthor, v => _viewModel.NewProjectAuthor = v, nameof(LauncherViewModel.NewProjectAuthor));
        formPanel.Children.Add(authorPanel);

        var (versionPanel, versionBox) = CreateFormField(SdkLocalizer.Loc("Form_Version"));
        BindTwoWayText(versionBox, () => _viewModel.NewProjectVersion, v => _viewModel.NewProjectVersion = v, nameof(LauncherViewModel.NewProjectVersion));
        formPanel.Children.Add(versionPanel);

        var (descPanel, descBox) = CreateFormField(SdkLocalizer.Loc("Form_Description"));
        BindTwoWayText(descBox, () => _viewModel.NewProjectDescription, v => _viewModel.NewProjectDescription = v, nameof(LauncherViewModel.NewProjectDescription));
        formPanel.Children.Add(descPanel);

        // 路径选择
        var pathPanel = new StackPanel { Spacing = 4 };
        pathPanel.Children.Add(new TextBlock
        {
            Text = SdkLocalizer.Loc("Form_OutputDir"),
            FontSize = 11,
            Foreground = ThemePalette.TextMuted,
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
            Background = ThemePalette.BtnBg,
            Foreground = ThemePalette.TextHover,
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
            Content = SdkLocalizer.Loc("Action_Create"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = ThemePalette.Accent,
            Foreground = ThemePalette.TextBright,
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
            Background = ThemePalette.Border,
            Margin = new Thickness(12, 0),
        });

        // 打开按钮
        var openBtn = CreateIconButtonWithText(IconFolder, SdkLocalizer.Loc("Action_OpenProject"), Brushes.Transparent, ThemePalette.TextHover);
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
            Foreground = ThemePalette.TextMuted,
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
        var grid = new Grid { Background = ThemePalette.EditorBg };

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
            Foreground = ThemePalette.TextMuted,
        });
        emptyHint.Children.Add(new TextBlock
        {
            Text = SdkLocalizer.Loc("Empty_Hint"),
            FontSize = 14,
            Foreground = ThemePalette.TextMuted,
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
                Background = ThemePalette.PanelBg,
                CornerRadius = new CornerRadius(6),
                BorderBrush = ThemePalette.Border,
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
                        Foreground = ThemePalette.Title,
                    },
                    new TextBlock
                    {
                        Text = item?.Name ?? "",
                        FontSize = 16,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = ThemePalette.TextBright,
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
                Foreground = ThemePalette.TextMuted,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 8, 0, 0),
            };
            Grid.SetRow(pathText, 1);
            cardContent.Children.Add(pathText);

            // 底部信息行：时间 + 操作按钮
            var bottomRow = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            };
            var timeText = new TextBlock
            {
                Text = item?.LastOpened.ToString("yyyy-MM-dd HH:mm") ?? "",
                FontSize = 11,
                Foreground = ThemePalette.TextMuted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(timeText, 0);
            bottomRow.Children.Add(timeText);

            // 删除按钮（整卡片点击=打开；删除单独显式暴露）
            var deleteIcon = new TextBlock
            {
                Text = IconTrash,
                FontFamily = FontFamily.Parse("Segoe MDL2 Assets"),
                FontSize = 13,
                Foreground = ThemePalette.TextMuted,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                Margin = new Thickness(6, 0, 0, 0),
            };
            Grid.SetColumn(deleteIcon, 1);
            bottomRow.Children.Add(deleteIcon);

            // 内联删除确认面板（默认隐藏，替换底部行）
            var confirmRow = new StackPanel
            {
                Spacing = 6,
                IsVisible = false,
            };

            // 首行：提示 + 是否连同文件一起删除
            var confirmTop = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
            };
            confirmTop.Children.Add(new TextBlock
            {
                Text = SdkLocalizer.Loc("Confirm_DeleteTitle"),
                FontSize = 11,
                Foreground = ThemePalette.StopRed,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var deleteFilesCheck = new CheckBox
            {
                Content = SdkLocalizer.Loc("Confirm_DeleteFiles"),
                FontSize = 11,
                Foreground = ThemePalette.TextMuted,
                IsChecked = false,
            };
            confirmTop.Children.Add(deleteFilesCheck);
            confirmRow.Children.Add(confirmTop);

            // 次行：确认 / 取消
            var confirmBtns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
            };
            var confirmDeleteBtn = new Button
            {
                Content = SdkLocalizer.Loc("Action_Delete"),
                FontSize = 11,
                Padding = new Thickness(8, 2),
                Background = ThemePalette.StopRed,
                Foreground = ThemePalette.TextBright,
                BorderThickness = new Thickness(0),
            };
            var cancelDeleteBtn = new Button
            {
                Content = SdkLocalizer.Loc("Action_Cancel"),
                FontSize = 11,
                Padding = new Thickness(8, 2),
                Background = ThemePalette.BtnBg,
                Foreground = ThemePalette.TextHover,
                BorderThickness = new Thickness(0),
            };
            confirmBtns.Children.Add(confirmDeleteBtn);
            confirmBtns.Children.Add(cancelDeleteBtn);
            confirmRow.Children.Add(confirmBtns);

            Grid.SetRow(bottomRow, 2);
            Grid.SetRow(confirmRow, 2);
            cardContent.Children.Add(bottomRow);
            cardContent.Children.Add(confirmRow);

            card.Child = cardContent;

            // 交互
            var confirming = false;
            void ShowConfirm()
            {
                confirming = true;
                bottomRow.IsVisible = false;
                confirmRow.IsVisible = true;
            }

            deleteIcon.PointerEntered += (_, _) => deleteIcon.Foreground = ThemePalette.StopRed;
            deleteIcon.PointerExited += (_, _) => deleteIcon.Foreground = ThemePalette.TextMuted;
            deleteIcon.PointerPressed += (_, e) =>
            {
                if (e.Pointer.IsPrimary)
                {
                    e.Handled = true;
                    ShowConfirm();
                }
            };

            cancelDeleteBtn.Click += (_, _) =>
            {
                confirming = false;
                confirmRow.IsVisible = false;
                bottomRow.IsVisible = true;
            };

            confirmDeleteBtn.Click += async (_, _) =>
            {
                // 勾选"同时删除文件" → 删除真实项目 + 记录；否则仅从最近列表移除此记录
                if (deleteFilesCheck.IsChecked == true)
                {
                    await _viewModel.DeleteProjectCommand.ExecuteAsync(item?.Path);
                }
                else if (item != null)
                {
                    _viewModel.RemoveRecentCommand.Execute(item.Path);
                }
            };

            card.PointerEntered += (_, _) => card.Background = ThemePalette.PanelDeeper;
            card.PointerExited += (_, _) => card.Background = ThemePalette.PanelBg;
            card.PointerPressed += async (_, e) =>
            {
                if (e.Pointer.IsPrimary && !confirming)
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
            Foreground = ThemePalette.TextMuted,
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
            Background = ThemePalette.PanelDeeper,
            BorderBrush = ThemePalette.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 4),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 16,
                Children =
                {
                    statusText,
                    new TextBlock { Text = "|", FontSize = 11, Foreground = ThemePalette.TextMuted },
                    new TextBlock { Text = "SDK v0.1.1", FontSize = 11, Foreground = ThemePalette.TextMuted },
                    new TextBlock { Text = "|", FontSize = 11, Foreground = ThemePalette.TextMuted },
                    new TextBlock { Text = ".NET 10", FontSize = 11, Foreground = ThemePalette.TextMuted },
                }
            }
        };
    }
}
