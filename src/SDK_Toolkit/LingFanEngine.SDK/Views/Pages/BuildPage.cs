using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.SDK.ViewModels;

namespace LingFanEngine.SDK.Views.Pages;

/// <summary>构建打包页面（纯 C# 构建）</summary>
public class BuildPage : UserControl
{
    public BuildPage(BuildViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent(viewModel);
    }

    private void InitializeComponent(BuildViewModel viewModel)
    {
        var grid = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,Auto,*,Auto"),
            Margin = new Thickness(16),
        };

        // 标题
        grid.Children.Add(new TextBlock
        {
            Text = "构建发布",
            Classes = { "page-title" },
        });

        // ===== 平台选择 + 加密配置 =====
        var configPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Margin = new Thickness(0, 0, 0, 16),
        };
        Grid.SetRow(configPanel, 1);

        // 平台选择
        var platformPanel = new StackPanel { Spacing = 4 };
        platformPanel.Children.Add(new TextBlock
        {
            Text = "目标平台",
            FontWeight = FontWeight.Bold,
        });

        var winCheck = new CheckBox { Content = "Windows" };
        winCheck.IsChecked = viewModel.TargetWindows;
        winCheck.IsCheckedChanged += (_, _) => viewModel.TargetWindows = winCheck.IsChecked ?? false;
        platformPanel.Children.Add(winCheck);

        var linuxCheck = new CheckBox { Content = "Linux" };
        linuxCheck.IsChecked = viewModel.TargetLinux;
        linuxCheck.IsCheckedChanged += (_, _) => viewModel.TargetLinux = linuxCheck.IsChecked ?? false;
        platformPanel.Children.Add(linuxCheck);

        var macCheck = new CheckBox { Content = "macOS" };
        macCheck.IsChecked = viewModel.TargetMacOS;
        macCheck.IsCheckedChanged += (_, _) => viewModel.TargetMacOS = macCheck.IsChecked ?? false;
        platformPanel.Children.Add(macCheck);

        configPanel.Children.Add(platformPanel);

        // 加密配置
        var encPanel = new StackPanel { Spacing = 4 };
        encPanel.Children.Add(new TextBlock
        {
            Text = "加密配置",
            FontWeight = FontWeight.Bold,
        });

        var encCheck = new CheckBox { Content = "启用加密" };
        encCheck.IsChecked = viewModel.EnableEncryption;
        encCheck.IsCheckedChanged += (_, _) => viewModel.EnableEncryption = encCheck.IsChecked ?? false;
        encPanel.Children.Add(encCheck);

        var aotCheck = new CheckBox { Content = "AOT 发布" };
        aotCheck.IsChecked = viewModel.PublishAot;
        aotCheck.IsCheckedChanged += (_, _) => viewModel.PublishAot = aotCheck.IsChecked ?? false;
        encPanel.Children.Add(aotCheck);

        configPanel.Children.Add(encPanel);

        // 构建按钮
        var buildBtn = new Button
        {
            Content = "开始构建",
            Command = viewModel.BuildCommand,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        configPanel.Children.Add(buildBtn);

        grid.Children.Add(configPanel);

        // ===== 构建日志 =====
        var logLabel = new TextBlock
        {
            Text = "构建日志",
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        var logBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = FontFamily.Parse("Consolas,Menlo,Monospace"),
            FontSize = 12,
        };
        // AOT 安全：手动监听 BuildLog
        logBox.Text = viewModel.BuildLog;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BuildViewModel.BuildLog))
                logBox.Text = viewModel.BuildLog;
        };

        var logPanel = new StackPanel();
        Grid.SetRow(logPanel, 2);
        logPanel.Children.Add(logLabel);
        logPanel.Children.Add(logBox);
        grid.Children.Add(logPanel);

        // 进度条
        var progress = new ProgressBar
        {
            Height = 4,
            Margin = new Thickness(0, 8, 0, 0),
            Value = viewModel.BuildProgress,
        };
        // AOT 安全：手动监听 BuildProgress
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BuildViewModel.BuildProgress))
                progress.Value = viewModel.BuildProgress;
        };
        Grid.SetRow(progress, 3);
        grid.Children.Add(progress);

        Content = grid;
    }
}
