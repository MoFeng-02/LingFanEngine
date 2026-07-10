using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace LingFanEngine.SDK.Views.Components;

/// <summary>
/// 文件树控件——展示项目 Stories/ 目录结构，支持双击打开文件。
/// </summary>
public class FileTreeView : UserControl
{
    private readonly TreeView _treeView;
    private readonly ObservableCollection<FileTreeNode> _rootNodes = [];

    /// <summary>双击文件时触发，参数为文件完整路径</summary>
    public event Action<string>? FileOpenRequested;

    /// <summary>右键新建文件时触发，参数为目录路径</summary>
    public event Action<string>? CreateFileRequested;

    /// <summary>当前根目录</summary>
    public string? RootDirectory { get; private set; }

    public FileTreeView()
    {
        // 标题栏
        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(8, 6, 8, 4),
        };
        headerPanel.Children.Add(new TextBlock
        {
            Text = "文件",
            FontWeight = FontWeight.Bold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });

        // 新建文件按钮
        var newFileBtn = new Button
        {
            Content = "+",
            FontSize = 14,
            Padding = new Thickness(6, 0),
            MinWidth = 24,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        newFileBtn.Click += (_, _) =>
        {
            if (RootDirectory != null)
                CreateFileRequested?.Invoke(RootDirectory);
        };
        headerPanel.Children.Add(newFileBtn);

        _treeView = new TreeView
        {
            ItemsSource = _rootNodes,
            Margin = new Thickness(0, 0, 0, 0),
        };
        _treeView.DoubleTapped += OnTreeViewDoubleTapped;

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };
        panel.Children.Add(headerPanel);
        panel.Children.Add(_treeView);

        Content = panel;
    }

    /// <summary>加载目录到文件树</summary>
    public void LoadDirectory(string directory)
    {
        RootDirectory = directory;
        _rootNodes.Clear();

        if (!Directory.Exists(directory))
            return;

        var nodes = BuildTree(directory, directory);
        foreach (var node in nodes)
            _rootNodes.Add(node);
    }

    /// <summary>刷新文件树</summary>
    public void Refresh()
    {
        if (RootDirectory != null)
            LoadDirectory(RootDirectory);
    }

    private List<FileTreeNode> BuildTree(string currentDir, string rootDir)
    {
        var nodes = new List<FileTreeNode>();

        try
        {
            // 先加目录
            var dirs = Directory.GetDirectories(currentDir)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
            foreach (var dir in dirs)
            {
                var dirName = Path.GetFileName(dir);
                // 跳过隐藏目录
                if (dirName.StartsWith('.')) continue;

                var node = new FileTreeNode
                {
                    Name = dirName,
                    FullPath = dir,
                    IsDirectory = true,
                    RelativePath = Path.GetRelativePath(rootDir, dir),
                };
                node.Children = BuildTree(dir, rootDir);
                nodes.Add(node);
            }

            // 再加 .story 文件
            var files = Directory.GetFiles(currentDir, "*.story")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var node = new FileTreeNode
                {
                    Name = fileName,
                    FullPath = file,
                    IsDirectory = false,
                    RelativePath = Path.GetRelativePath(rootDir, file),
                };
                nodes.Add(node);
            }
        }
        catch
        {
            // 目录访问失败——忽略
        }

        return nodes;
    }

    private void OnTreeViewDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_treeView.SelectedItem is FileTreeNode node && !node.IsDirectory)
        {
            FileOpenRequested?.Invoke(node.FullPath);
        }
    }
}

/// <summary>文件树节点</summary>
public class FileTreeNode
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public List<FileTreeNode> Children { get; set; } = [];

    public string Icon => IsDirectory ? "\uE8B7" : "\uE7C3";  // Segoe MDL2: Folder / File

    public override string ToString() => Name;
}
