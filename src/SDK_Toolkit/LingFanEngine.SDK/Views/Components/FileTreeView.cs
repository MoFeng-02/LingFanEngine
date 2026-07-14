using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace LingFanEngine.SDK.Views.Components;

/// <summary>
/// 文件树控件——展示项目 Stories/ 目录结构。
/// <para>P1-6: 右键菜单（删除/重命名/复制路径/新建子目录）。</para>
/// <para>P1-7: 未保存修改指示器。</para>
/// </summary>
public class FileTreeView : UserControl
{
    private readonly TreeView _treeView;
    private readonly ObservableCollection<FileTreeNode> _rootNodes = [];

    /// <summary>双击文件时触发，参数为文件完整路径</summary>
    public event Action<string>? FileOpenRequested;

    /// <summary>新建文件时触发，参数为完整文件路径</summary>
    public event Action<string>? CreateFileRequested;

    /// <summary>P1-6: 删除文件时触发</summary>
    public event Action<string>? FileDeleteRequested;

    /// <summary>P1-6: 重命名文件时触发</summary>
    public event Action<string, string>? FileRenameRequested;

    /// <summary>当前根目录</summary>
    public string? RootDirectory { get; private set; }

    public FileTreeView()
    {
        _treeView = new TreeView
        {
            ItemsSource = _rootNodes,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _treeView.DoubleTapped += OnTreeViewDoubleTapped;
        _treeView.KeyDown += OnTreeKeyDown;

        // 设置树形数据模板（支持子目录展开 + 图标显示）
        _treeView.ItemTemplate = new FileNodeTreeTemplate();

        // P1-6: 右键菜单
        _treeView.ContextMenu = CreateContextMenu();

        // P0-2: 移除内部标题栏，直接使用 TreeView 作为内容
        Content = _treeView;
    }

    /// <summary>P1-6: 创建右键上下文菜单</summary>
    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();

        var newFileItem = new MenuItem { Header = "新建文件" };
        newFileItem.Click += (_, _) =>
        {
            var node = _treeView.SelectedItem as FileTreeNode;
            var dir = node != null && node.IsDirectory ? node.FullPath : RootDirectory;
            if (dir != null)
                ShowNewFileDialog(dir);
        };

        var newDirItem = new MenuItem { Header = "新建子目录" };
        newDirItem.Click += (_, _) =>
        {
            var node = _treeView.SelectedItem as FileTreeNode;
            var dir = node != null && node.IsDirectory ? node.FullPath : RootDirectory;
            if (dir != null)
            {
                var newDirName = $"new_dir_{DateTime.Now:HHmmss}";
                var newDirPath = Path.Combine(dir, newDirName);
                try
                {
                    Directory.CreateDirectory(newDirPath);
                    Refresh();
                }
                catch { }
            }
        };

        var renameItem = new MenuItem { Header = "重命名 (F2)" };
        renameItem.Click += (_, _) => OnRename();

        var deleteItem = new MenuItem { Header = "删除 (Del)" };
        deleteItem.Click += (_, _) => OnDelete();

        var copyPathItem = new MenuItem { Header = "复制路径" };
        copyPathItem.Click += (_, _) =>
        {
            if (_treeView.SelectedItem is FileTreeNode node)
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                    _ = clipboard.SetTextAsync(node.FullPath);
            }
        };

        var openInExplorerItem = new MenuItem { Header = "在文件管理器中打开" };
        openInExplorerItem.Click += (_, _) =>
        {
            if (_treeView.SelectedItem is FileTreeNode node)
            {
                var path = node.IsDirectory ? node.FullPath : Path.GetDirectoryName(node.FullPath);
                if (path == null) return;
                try
                {
                    if (OperatingSystem.IsWindows())
                        System.Diagnostics.Process.Start("explorer.exe", path);
                    else if (OperatingSystem.IsMacOS())
                        System.Diagnostics.Process.Start("open", path);
                    else if (OperatingSystem.IsLinux())
                        System.Diagnostics.Process.Start("xdg-open", path);
                }
                catch { }
            }
        };

        menu.Items.Add(newFileItem);
        menu.Items.Add(newDirItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(renameItem);
        menu.Items.Add(deleteItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(copyPathItem);
        menu.Items.Add(openInExplorerItem);

        return menu;
    }

    /// <summary>键盘快捷键：F2=重命名, Delete=删除</summary>
    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (_treeView.SelectedItem is not FileTreeNode) return;

        if (e.Key == Key.F2)
        {
            OnRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            OnDelete();
            e.Handled = true;
        }
    }

    /// <summary>P1-6: 删除操作（目录直接删除，文件委托给 ViewModel）</summary>
    private void OnDelete()
    {
        if (_treeView.SelectedItem is FileTreeNode node)
        {
            if (node.IsDirectory)
            {
                try
                {
                    Directory.Delete(node.FullPath, true);
                    Refresh();
                }
                catch { }
            }
            else
            {
                // 仅通知 ViewModel 处理删除（关闭标签页等）
                // ViewModel.DeleteFile 会删除文件并触发 FileTreeNeedsRefresh 事件
                FileDeleteRequested?.Invoke(node.FullPath);
            }
        }
    }

    /// <summary>P0-2: 重命名操作——弹出输入对话框</summary>
    private void OnRename()
    {
        if (_treeView.SelectedItem is FileTreeNode node)
            ShowRenameDialog(node);
    }

    /// <summary>P0-2: 显示重命名对话框</summary>
    private void ShowRenameDialog(FileTreeNode node)
    {
        var owner = GetOwnerWindow();
        if (owner == null) return;

        var dialog = new Window
        {
            Title = "重命名",
            Width = 400,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#252526")),
            CanResize = false,
        };

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 16), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "输入新名称：",
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            FontSize = 13,
        });

        var nameBox = new TextBox
        {
            FontSize = 13,
            Text = node.Name,
        };
        // 选中文件名（不含扩展名）
        var ext = Path.GetExtension(node.Name);
        if (!string.IsNullOrEmpty(ext) && node.Name.Length > ext.Length)
        {
            nameBox.SelectionStart = 0;
            nameBox.SelectionEnd = node.Name.Length - ext.Length;
        }
        // Enter=确定, Esc=取消
        nameBox.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter)
            {
                var newName = nameBox.Text?.Trim();
                if (!string.IsNullOrEmpty(newName) && newName != node.Name)
                    FileRenameRequested?.Invoke(node.FullPath, newName);
                dialog.Close();
                ke.Handled = true;
            }
            else if (ke.Key == Key.Escape)
            {
                dialog.Close();
                ke.Handled = true;
            }
        };
        panel.Children.Add(nameBox);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        var okBtn = new Button { Content = "确定", Padding = new Thickness(16, 6) };
        var cancelBtn = new Button { Content = "取消", Padding = new Thickness(16, 6) };

        okBtn.Click += (_, _) =>
        {
            var newName = nameBox.Text?.Trim();
            if (!string.IsNullOrEmpty(newName) && newName != node.Name)
            {
                FileRenameRequested?.Invoke(node.FullPath, newName);
            }
            dialog.Close();
        };
        cancelBtn.Click += (_, _) => dialog.Close();

        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(btnPanel);

        dialog.Content = panel;
        dialog.Opened += (_, _) => nameBox.Focus();
        _ = dialog.ShowDialog(owner);
    }

    /// <summary>P1-5: 显示新建文件对话框（public 供 WorkspaceWindow 调用）</summary>
    public void ShowNewFileDialog(string directory)
    {
        var owner = GetOwnerWindow();
        if (owner == null) return;

        var dialog = new Window
        {
            Title = "新建故事文件",
            Width = 400,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#252526")),
            CanResize = false,
        };

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 16), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "输入文件名（.story 扩展名可省略）：",
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            FontSize = 13,
        });

        var nameBox = new TextBox
        {
            FontSize = 13,
            Text = "new_story.story",
        };
        // 选中全部文本
        nameBox.SelectionStart = 0;
        nameBox.SelectionEnd = nameBox.Text.Length;
        // Enter=创建, Esc=取消
        nameBox.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter)
            {
                var fileName = nameBox.Text?.Trim();
                if (!string.IsNullOrEmpty(fileName))
                {
                    if (!fileName.EndsWith(".story", StringComparison.OrdinalIgnoreCase))
                        fileName += ".story";
                    CreateFileRequested?.Invoke(Path.Combine(directory, fileName));
                }
                dialog.Close();
                ke.Handled = true;
            }
            else if (ke.Key == Key.Escape)
            {
                dialog.Close();
                ke.Handled = true;
            }
        };
        panel.Children.Add(nameBox);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        var okBtn = new Button { Content = "创建", Padding = new Thickness(16, 6) };
        var cancelBtn = new Button { Content = "取消", Padding = new Thickness(16, 6) };

        okBtn.Click += (_, _) =>
        {
            var fileName = nameBox.Text?.Trim();
            if (!string.IsNullOrEmpty(fileName))
            {
                // 自动补全 .story 扩展名
                if (!fileName.EndsWith(".story", StringComparison.OrdinalIgnoreCase))
                    fileName += ".story";
                CreateFileRequested?.Invoke(Path.Combine(directory, fileName));
            }
            dialog.Close();
        };
        cancelBtn.Click += (_, _) => dialog.Close();

        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(btnPanel);

        dialog.Content = panel;
        dialog.Opened += (_, _) => nameBox.Focus();
        _ = dialog.ShowDialog(owner);
    }

    /// <summary>获取所属窗口（兼容 VisualRoot 未就绪的情况）</summary>
    private Window? GetOwnerWindow()
    {
        if (VisualRoot is Window w) return w;
        // 遍历可视化树查找顶层窗口
        var parent = this.Parent;
        while (parent != null)
        {
            if (parent is Window pw) return pw;
            parent = parent.Parent;
        }
        return null;
    }

    /// <summary>加载目录到文件树</summary>
    public void LoadDirectory(string directory)
    {
        RootDirectory = directory;

        if (!Directory.Exists(directory))
        {
            _rootNodes.Clear();
            return;
        }

        _rootNodes.Clear();
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

    /// <summary>清空文件树（项目关闭时调用）</summary>
    public void Clear()
    {
        RootDirectory = null;
        _rootNodes.Clear();
    }

    /// <summary>P1-7: 标记文件为已修改</summary>
    public void MarkFileDirty(string filePath, bool isDirty)
    {
        MarkFileDirtyInternal(_rootNodes, filePath, isDirty);
    }

    private bool MarkFileDirtyInternal(ObservableCollection<FileTreeNode> nodes, string filePath, bool isDirty)
    {
        foreach (var node in nodes)
        {
            if (!node.IsDirectory && node.FullPath == filePath)
            {
                node.IsDirty = isDirty;
                return true;
            }
            if (node.IsDirectory && MarkFileDirtyInternal(node.Children, filePath, isDirty))
                return true;
        }
        return false;
    }

    private ObservableCollection<FileTreeNode> BuildTree(string currentDir, string rootDir)
    {
        var nodes = new ObservableCollection<FileTreeNode>();

        try
        {
            // 先加目录
            var dirs = Directory.GetDirectories(currentDir)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
            foreach (var dir in dirs)
            {
                var dirName = Path.GetFileName(dir);
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
public class FileTreeNode : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isDirty;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));

    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public ObservableCollection<FileTreeNode> Children { get; set; } = [];

    /// <summary>P1-7: 未保存修改标记</summary>
    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty != value)
            {
                _isDirty = value;
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    /// <summary>P1-7: 显示名称（含脏标记）</summary>
    public string DisplayName => IsDirty ? $"● {Name}" : Name;

    public string Icon => IsDirectory ? "\uE8B7" : "\uE7C3";  // Segoe MDL2: Folder / File

    public override string ToString() => DisplayName;
}

/// <summary>TreeView 层级数据模板——展示文件树节点（图标+名称），子项来自 Children 属性</summary>
internal sealed class FileNodeTreeTemplate : ITreeDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is FileTreeNode node)
        {
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(2),
                Children =
                {
                    new TextBlock
                    {
                        Text = node.Icon,
                        FontFamily = FontFamily.Parse("Segoe MDL2 Assets"),
                        FontSize = 14,
                        Foreground = new SolidColorBrush(
                            node.IsDirectory ? Color.Parse("#DCBC67") : Color.Parse("#75BEFF")),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = node.DisplayName,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            };
        }
        return new TextBlock { Text = param?.ToString() ?? "" };
    }

    public bool Match(object? data) => data is FileTreeNode;

    public IEnumerable? Items(object? param) => (param as FileTreeNode)?.Children;

    public IDisposable BindChildren(Avalonia.AvaloniaObject target, Avalonia.AvaloniaProperty itemsSourceProperty, object data)
    {
        var items = Items(data);
        target.SetValue(itemsSourceProperty, items);
        return EmptyDisposable.Instance; // 返回空 IDisposable，避免框架 Dispose(null) NRE
    }
}

/// <summary>空 IDisposable 实现（用于 BindChildren 返回安全值）</summary>
internal sealed class EmptyDisposable : IDisposable
{
    public static readonly EmptyDisposable Instance = new();
    public void Dispose() { }
}
