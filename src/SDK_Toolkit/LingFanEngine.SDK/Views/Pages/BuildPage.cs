using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.ViewModels;
using MFToolkit.Routing.Core.Interfaces;

namespace LingFanEngine.SDK.Views.Pages;

/// <summary>
/// 构建发布页面——完整配置 + 构建日志。
/// <para>加密配置分层展开：总开关 → 资源加密开关 → 文件类型勾选。</para>
/// </summary>
public class BuildPage : UserControl, INavigationAware
{
    private readonly BuildViewModel _viewModel;
    private ContentControl? _encryptionDetailPanel;
    private ContentControl? _fileTypesPanel;
    private TextBlock? _engineVersionText;
    private TextBlock? _engineUpdateMsgText;
    private TextBlock? _launchMsgText;

    public BuildPage(BuildViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent(viewModel);
    }

    // ===== INavigationAware =====

    public void OnNavigated(Dictionary<string, object?>? parameters) { }
    public void OnNavigatingFrom() { }
    public void OnNavigatedFrom() { }

    private void InitializeComponent(BuildViewModel viewModel)
    {
        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var grid = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,Auto,Auto,Auto,*,Auto"),
            Margin = new Thickness(16),
        };

        // ===== 标题 =====
        grid.Children.Add(new TextBlock
        {
            Text = "构建发布",
            Classes = { "page-title" },
        });

        // ===== 第一行：平台 + AOT =====
        var topPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 24,
            Margin = new Thickness(0, 8, 0, 16),
        };
        Grid.SetRow(topPanel, 1);

        // 平台选择
        var platformPanel = new StackPanel { Spacing = 4 };
        platformPanel.Children.Add(new TextBlock
        {
            Text = "目标平台",
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
        });

        var winCheck = CreateCheckbox("Windows", viewModel.TargetWindows);
        winCheck.IsCheckedChanged += (_, _) => viewModel.TargetWindows = winCheck.IsChecked ?? false;
        platformPanel.Children.Add(winCheck);

        var linuxCheck = CreateCheckbox("Linux", viewModel.TargetLinux);
        linuxCheck.IsCheckedChanged += (_, _) => viewModel.TargetLinux = linuxCheck.IsChecked ?? false;
        platformPanel.Children.Add(linuxCheck);

        var macArmCheck = CreateCheckbox("macOS (osx-arm64)", viewModel.TargetMacOSArm64);
        macArmCheck.IsCheckedChanged += (_, _) => viewModel.TargetMacOSArm64 = macArmCheck.IsChecked ?? false;
        platformPanel.Children.Add(macArmCheck);

        var macX64Check = CreateCheckbox("macOS (osx-x64)", viewModel.TargetMacOSX64);
        macX64Check.IsCheckedChanged += (_, _) => viewModel.TargetMacOSX64 = macX64Check.IsChecked ?? false;
        platformPanel.Children.Add(macX64Check);

        topPanel.Children.Add(platformPanel);

        // 编译选项
        var buildPanel = new StackPanel { Spacing = 4 };
        buildPanel.Children.Add(new TextBlock
        {
            Text = "编译选项",
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
        });

        var aotCheck = CreateCheckbox("AOT 发布", viewModel.PublishAot);
        aotCheck.IsCheckedChanged += (_, _) => viewModel.PublishAot = aotCheck.IsChecked ?? false;
        buildPanel.Children.Add(aotCheck);

        topPanel.Children.Add(buildPanel);

        // 构建按钮
        var buildBtn = new Button
        {
            Content = "开始构建",
            Command = viewModel.BuildCommand,
            FontSize = 14,
            Padding = new Thickness(24, 8),
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.Parse("#0E639C")),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
        };
        topPanel.Children.Add(buildBtn);

        // 启动游戏按钮（若已构建则直接运行，未构建自动构建当前平台）
        var launchBtn = new Button
        {
            Content = "启动游戏",
            Command = viewModel.LaunchGameCommand,
            FontSize = 14,
            Padding = new Thickness(24, 8),
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.Parse("#2D9D78")),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
        };
        topPanel.Children.Add(launchBtn);

        // 停止游戏按钮（杀掉运行中的游戏进程，便于重建/更新 DLL）
        var stopBtn = new Button
        {
            Content = "停止游戏",
            Command = viewModel.StopGameCommand,
            FontSize = 14,
            Padding = new Thickness(24, 8),
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.Parse("#C0392B")),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
        };
        topPanel.Children.Add(stopBtn);

        grid.Children.Add(topPanel);

        // ===== 第二行：加密配置（分层展开） =====
        var encPanel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 16) };
        Grid.SetRow(encPanel, 2);

        encPanel.Children.Add(new TextBlock
        {
            Text = "加密配置",
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
        });

        // 第一层：启用加密（总开关）
        var encEnableCheck = CreateCheckbox("启用加密", viewModel.EnableEncryption);
        encEnableCheck.IsCheckedChanged += (_, _) =>
        {
            viewModel.EnableEncryption = encEnableCheck.IsChecked ?? false;
            UpdateEncryptionVisibility();
        };
        encPanel.Children.Add(encEnableCheck);

        // 第二层+第三层容器（仅当启用加密时可见）
        _encryptionDetailPanel = new ContentControl();
        encPanel.Children.Add(_encryptionDetailPanel);

        var detailStack = new StackPanel { Spacing = 6, Margin = new Thickness(24, 0, 0, 0) };

        // 第二层：加密资源文件
        var encResCheck = CreateCheckbox("加密资源文件", viewModel.EncryptResources);
        encResCheck.IsCheckedChanged += (_, _) =>
        {
            viewModel.EncryptResources = encResCheck.IsChecked ?? false;
            UpdateFileTypesVisibility();
        };
        detailStack.Children.Add(encResCheck);

        // 第三层：文件类型勾选 + 密钥分片数
        _fileTypesPanel = new ContentControl();
        detailStack.Children.Add(_fileTypesPanel);

        var fileTypesStack = new StackPanel { Spacing = 4, Margin = new Thickness(24, 4, 0, 0) };

        // 文件类型勾选列表
        fileTypesStack.Children.Add(new TextBlock
        {
            Text = "选择要加密的文件类型：",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#888888")),
        });

        // 用 WrapPanel 放 checkbox
        var wrapPanel = new WrapPanel { Orientation = Orientation.Horizontal };

        // 初始填充 + 订阅 CollectionChanged（项目可能在页面创建后才打开）
        void RebuildFileTypes()
        {
            wrapPanel.Children.Clear();
            foreach (var item in viewModel.EncryptFileTypes)
            {
                var cb = CreateCheckbox($"{item.DisplayName} ({item.Extension})", item.IsChecked);
                cb.IsCheckedChanged += (_, _) => item.IsChecked = cb.IsChecked ?? false;
                wrapPanel.Children.Add(cb);
            }
        }
        RebuildFileTypes();
        viewModel.EncryptFileTypes.CollectionChanged += (_, _) => RebuildFileTypes();

        fileTypesStack.Children.Add(wrapPanel);

        // 全选/全不选
        var selectButtonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var selectAllBtn = new Button { Content = "全选", Padding = new Thickness(12, 2), FontSize = 11, Classes = { "transparent" } };
        selectAllBtn.Click += (_, _) =>
        {
            foreach (var item in viewModel.EncryptFileTypes)
                item.IsChecked = true;
            RebuildFileTypes();
        };

        var selectNoneBtn = new Button { Content = "全不选", Padding = new Thickness(12, 2), FontSize = 11, Classes = { "transparent" } };
        selectNoneBtn.Click += (_, _) =>
        {
            foreach (var item in viewModel.EncryptFileTypes)
                item.IsChecked = false;
            RebuildFileTypes();
        };

        selectButtonsPanel.Children.Add(selectAllBtn);
        selectButtonsPanel.Children.Add(selectNoneBtn);
        fileTypesStack.Children.Add(selectButtonsPanel);

        // 密钥分片数
        var shardPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
        };
        shardPanel.Children.Add(new TextBlock
        {
            Text = "密钥分片数：",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var shardBox = new TextBox
        {
            Text = viewModel.KeyShardCount.ToString(),
            Width = 40,
            FontSize = 13,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsReadOnly = true,
            Background = new SolidColorBrush(Color.Parse("#2D2D30")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3F3F46")),
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
        };

        var minusBtn = new Button
        {
            Content = "−",
            Width = 28,
            Height = 28,
            FontSize = 14,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Color.Parse("#3C3C3C")),
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse("#3F3F46")),
        };
        minusBtn.Click += (_, _) =>
        {
            if (viewModel.KeyShardCount > 2)
            {
                viewModel.KeyShardCount--;
                shardBox.Text = viewModel.KeyShardCount.ToString();
            }
        };

        var plusBtn = new Button
        {
            Content = "+",
            Width = 28,
            Height = 28,
            FontSize = 14,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Color.Parse("#3C3C3C")),
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse("#3F3F46")),
        };
        plusBtn.Click += (_, _) =>
        {
            if (viewModel.KeyShardCount < 16)
            {
                viewModel.KeyShardCount++;
                shardBox.Text = viewModel.KeyShardCount.ToString();
            }
        };

        shardPanel.Children.Add(minusBtn);
        shardPanel.Children.Add(shardBox);
        shardPanel.Children.Add(plusBtn);
        fileTypesStack.Children.Add(shardPanel);

        _fileTypesPanel.Content = fileTypesStack;
        _encryptionDetailPanel.Content = detailStack;

        grid.Children.Add(encPanel);

        // ===== 引擎依赖（项目独立 DLL 版本隔离） =====
        var enginePanel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 16) };
        Grid.SetRow(enginePanel, 3);

        enginePanel.Children.Add(new TextBlock
        {
            Text = "引擎依赖",
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
        });

        var engineRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Margin = new Thickness(0, 4, 0, 0),
        };

        _engineVersionText = new TextBlock
        {
            Text = $"当前版本：{viewModel.EngineDependencyVersion}",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
        };
        engineRow.Children.Add(_engineVersionText);

        var updateEngineBtn = new Button
        {
            Content = "检查并更新项目引擎",
            Command = viewModel.UpdateProjectEngineCommand,
            FontSize = 13,
            Padding = new Thickness(16, 6),
            Background = new SolidColorBrush(Color.Parse("#0E639C")),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
        };
        engineRow.Children.Add(updateEngineBtn);

        enginePanel.Children.Add(engineRow);

        _engineUpdateMsgText = new TextBlock
        {
            Text = viewModel.ProjectEngineUpdateMessage,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#88C0D0")),
            TextWrapping = TextWrapping.Wrap,
        };
        enginePanel.Children.Add(_engineUpdateMsgText);

        enginePanel.Children.Add(new TextBlock
        {
            Text = "提示：更新仅替换项目 DLL/ 内引擎依赖，需重新构建发布后生效。",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#888888")),
        });

        // 启动/停止游戏状态文案
        _launchMsgText = new TextBlock
        {
            Text = viewModel.LaunchMessage,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#88C0D0")),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        enginePanel.Children.Add(_launchMsgText);

        grid.Children.Add(enginePanel);

        // ===== 第三行：构建日志 =====
        var logPanel = new StackPanel();
        Grid.SetRow(logPanel, 4);

        logPanel.Children.Add(new TextBlock
        {
            Text = "构建日志",
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
        });

        var logBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = FontFamily.Parse("Consolas,Menlo,Monospace"),
            FontSize = 12,
            Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
            BorderBrush = new SolidColorBrush(Color.Parse("#333333")),
        };
        logBox.Text = viewModel.BuildLog;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BuildViewModel.BuildLog))
                logBox.Text = viewModel.BuildLog;
            if (e.PropertyName == nameof(BuildViewModel.EngineDependencyVersion) && _engineVersionText != null)
                _engineVersionText.Text = $"当前版本：{viewModel.EngineDependencyVersion}";
            if (e.PropertyName == nameof(BuildViewModel.ProjectEngineUpdateMessage) && _engineUpdateMsgText != null)
                _engineUpdateMsgText.Text = viewModel.ProjectEngineUpdateMessage;
            if (e.PropertyName == nameof(BuildViewModel.LaunchMessage) && _launchMsgText != null)
                _launchMsgText.Text = viewModel.LaunchMessage;
        };

        logPanel.Children.Add(logBox);
        grid.Children.Add(logPanel);

        // ===== 第四行：进度条 =====
        var progress = new ProgressBar
        {
            Height = 4,
            Margin = new Thickness(0, 8, 0, 0),
            Value = viewModel.BuildProgress,
        };
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BuildViewModel.BuildProgress))
                progress.Value = viewModel.BuildProgress;
        };
        Grid.SetRow(progress, 5);
        grid.Children.Add(progress);

        scrollViewer.Content = grid;
        Content = scrollViewer;

        // 初始化可见性
        UpdateEncryptionVisibility();
        UpdateFileTypesVisibility();
    }

    private void UpdateEncryptionVisibility()
    {
        if (_encryptionDetailPanel != null)
            _encryptionDetailPanel.IsVisible = _viewModel.EnableEncryption;
    }

    private void UpdateFileTypesVisibility()
    {
        if (_fileTypesPanel != null)
            _fileTypesPanel.IsVisible = _viewModel.EncryptResources;
    }

    private static CheckBox CreateCheckbox(string text, bool isChecked)
    {
        return new CheckBox
        {
            Content = text,
            IsChecked = isChecked,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            Margin = new Thickness(0, 2, 16, 2),
        };
    }
}
