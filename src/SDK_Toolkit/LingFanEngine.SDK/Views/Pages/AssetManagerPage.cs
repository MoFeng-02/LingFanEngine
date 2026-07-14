using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.ViewModels;
using MFToolkit.Routing.Core.Interfaces;

namespace LingFanEngine.SDK.Views.Pages;

/// <summary>资源管理页面（纯 C# 构建）</summary>
public class AssetManagerPage : UserControl, INavigationAware
{
    public AssetManagerPage(AssetManagerViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent(viewModel);
    }

    // ===== INavigationAware =====

    public void OnNavigated(Dictionary<string, object?>? parameters)
    {
        // 导航到资源页时自动扫描
        if (DataContext is AssetManagerViewModel vm && vm.Assets.Count == 0)
        {
            vm.ScanAssetsCommand.Execute(null);
        }
    }

    public void OnNavigatingFrom() { }
    public void OnNavigatedFrom() { }

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

        // P2-5: 拖拽导入资源
        DragDrop.SetAllowDrop(listBox, true);
        listBox.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        listBox.AddHandler(DragDrop.DropEvent, OnDrop);

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

    /// <summary>P2-5: 拖拽悬停——高亮目标区域</summary>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // 只接受文件拖拽
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>P2-5: 拖拽释放——导入文件</summary>
    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        if (!e.DataTransfer.Contains(DataFormat.File)) return;

        var files = e.DataTransfer.TryGetFiles();
        if (files == null) return;

        var filePaths = files
            .Select(f => f.Path.LocalPath)
            .Where(File.Exists)
            .ToArray();

        if (filePaths.Length > 0 && DataContext is AssetManagerViewModel vm)
        {
            _ = vm.ImportFilesCommand.ExecuteAsync(filePaths);
        }
    }
}
