using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.SDK.ViewModels;

namespace LingFanEngine.SDK.Views.Pages;

/// <summary>设置页面（纯 C# 构建）</summary>
public class SettingsPage : UserControl
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent(viewModel);
    }

    private void InitializeComponent(SettingsViewModel viewModel)
    {
        var grid = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,Auto,Auto,Auto,*"),
            Margin = new Thickness(16),
        };

        // 标题
        grid.Children.Add(new TextBlock
        {
            Text = "设置",
            Classes = { "page-title" },
        });

        // 版本信息
        var infoPanel = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 16),
        };
        Grid.SetRow(infoPanel, 1);

        infoPanel.Children.Add(CreateInfoRow("SDK 版本", viewModel.SdkVersion));
        infoPanel.Children.Add(CreateInfoRow(".NET 版本", viewModel.DotNetVersion));
        infoPanel.Children.Add(CreateInfoRow("应用数据目录", viewModel.AppDataDirectory));

        var openDataBtn = new Button
        {
            Content = "打开数据目录",
            Command = viewModel.OpenAppDataCommand,
            Margin = new Thickness(0, 4, 0, 0),
        };
        infoPanel.Children.Add(openDataBtn);

        grid.Children.Add(infoPanel);

        // dotnet 检查
        var checkPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        Grid.SetRow(checkPanel, 2);

        var checkBtn = new Button
        {
            Content = "检查 dotnet",
            Command = viewModel.CheckDotNetCommand,
        };
        checkPanel.Children.Add(checkBtn);

        var statusText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Foreground = Brushes.Gray,
        };
        // AOT 安全：手动监听 StatusMessage
        statusText.Text = viewModel.StatusMessage;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.StatusMessage))
                statusText.Text = viewModel.StatusMessage;
        };
        checkPanel.Children.Add(statusText);

        grid.Children.Add(checkPanel);

        Content = grid;
    }

    private static StackPanel CreateInfoRow(string label, string value)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = label + ":",
                    FontWeight = FontWeight.Bold,
                    Width = 120,
                },
                new TextBlock { Text = value },
            }
        };
    }
}
