using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.ViewModels;

namespace LingFanEngine.SDK.Views.Pages;

/// <summary>资源管理页面（纯 C# 构建）</summary>
public class AssetManagerPage : UserControl
{
    public AssetManagerPage(AssetManagerViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent(viewModel);
    }

    private void InitializeComponent(AssetManagerViewModel viewModel)
    {
        var grid = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"),
            Margin = new Thickness(16),
        };

        // 标题 + 刷新按钮
        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        headerPanel.Children.Add(new TextBlock
        {
            Text = "资源管理",
            Classes = { "page-title" },
        });

        var scanBtn = new Button
        {
            Content = "扫描资源",
            Command = viewModel.ScanAssetsCommand,
        };
        headerPanel.Children.Add(scanBtn);
        grid.Children.Add(headerPanel);

        // 资源列表
        var listBox = new ListBox();
        var itemTemplate = new FuncDataTemplate<AssetEntry>((item, _) =>
            new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock { Text = item?.FileName ?? "", FontWeight = FontWeight.Bold },
                            new TextBlock
                            {
                                Text = item?.Type.ToString() ?? "",
                                Foreground = Brushes.Gray,
                                FontSize = 11,
                            },
                        }
                    },
                    new TextBlock
                    {
                        Text = item?.RelativePath ?? "",
                        FontSize = 11,
                        Foreground = Brushes.Gray,
                    },
                    new TextBlock
                    {
                        Text = $"{(item?.SizeBytes ?? 0) / 1024.0:F1} KB",
                        FontSize = 11,
                        Foreground = Brushes.Gray,
                    },
                }
            });
        listBox.ItemTemplate = itemTemplate;
        listBox.ItemsSource = viewModel.Assets;
        listBox.SelectionChanged += (_, _) => viewModel.SelectedAsset = listBox.SelectedItem as AssetEntry;
        Grid.SetRow(listBox, 1);
        grid.Children.Add(listBox);

        // 状态栏
        var statusText = new TextBlock
        {
            FontSize = 12,
            Foreground = Brushes.Gray,
        };
        // AOT 安全：手动监听 StatusMessage
        statusText.Text = viewModel.StatusMessage;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AssetManagerViewModel.StatusMessage))
                statusText.Text = viewModel.StatusMessage;
        };
        Grid.SetRow(statusText, 2);
        grid.Children.Add(statusText);

        Content = grid;
    }
}
