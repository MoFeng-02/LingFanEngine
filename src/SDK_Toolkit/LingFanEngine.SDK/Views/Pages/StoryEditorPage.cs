using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using LingFanEngine.SDK.Editor;
using LingFanEngine.SDK.ViewModels;
using LingFanEngine.SDK.Views.Components;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Views.Pages;

/// <summary>故事编辑器页面——三栏 IDE 布局</summary>
public class StoryEditorPage : UserControl
{
    private readonly StoryEditorViewModel _viewModel;
    private readonly CodeEditorView _editor;
    private readonly FileTreeView _fileTree;
    private readonly TextBlock _statusBar;

    public StoryEditorPage(StoryEditorViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        // 创建组件
        _editor = new CodeEditorView();
        _fileTree = new FileTreeView();
        _statusBar = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#888888")),
            Margin = new Thickness(8, 4),
        };

        InitializeComponent();

        // 如果已有项目，加载文件树
        if (_viewModel.StoriesDirectory != null)
        {
            _fileTree.LoadDirectory(_viewModel.StoriesDirectory);
        }

        // 监听 StoriesDirectory 变化
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StoryEditorViewModel.StoriesDirectory)
                && _viewModel.StoriesDirectory != null)
            {
                _fileTree.LoadDirectory(_viewModel.StoriesDirectory);
            }
        };
    }

    private void InitializeComponent()
    {
        // ===== 事件绑定 =====

        // 文件树双击 → 打开文件
        _fileTree.FileOpenRequested += async path => await _viewModel.OpenFileCommand.ExecuteAsync(path);

        // 文件树新建文件
        _fileTree.CreateFileRequested += async dir => await _viewModel.CreateNewFileCommand.ExecuteAsync(dir);

        // 编辑器文本变更 → debounce 分析
        _editor.SourceChanged += async source =>
        {
            await _viewModel.OnSourceChangedAsync(source);
            _editor.UpdateHighlights(source);
        };

        // 编辑器光标移动 → 状态栏
        _editor.CaretMoved += pos => _viewModel.UpdateCaretInfo(pos.Line, pos.Column);

        // 编辑器保存
        _editor.SaveRequested += async () => await _viewModel.SaveFileCommand.ExecuteAsync(null);

        // 编辑器 Go to Definition
        _editor.GoToDefinitionRequested += async () =>
        {
            var word = GetWordAtCaret();
            if (string.IsNullOrEmpty(word)) return;
            var result = _viewModel.GoToDefinition(word);
            if (result != null)
            {
                // 如果是当前文件，直接滚动；否则打开目标文件
                if (result.Value.FilePath == _viewModel.CurrentFilePath)
                {
                    _editor.ScrollToLine(result.Value.Line);
                }
                else
                {
                    await _viewModel.OpenFileCommand.ExecuteAsync(result.Value.FilePath);
                    _editor.ScrollToLine(result.Value.Line);
                }
            }
        };

        // ViewModel 属性变化 → 更新编辑器
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StoryEditorViewModel.EditorContent))
            {
                // 文件加载时同步编辑器
                if (_editor.Source != _viewModel.EditorContent)
                {
                    _editor.LoadFile(_viewModel.CurrentFilePath, _viewModel.EditorContent);
                    _editor.UpdateHighlights(_viewModel.EditorContent);
                }
            }
            else if (e.PropertyName == nameof(StoryEditorViewModel.Diagnostics))
            {
                // P1-9: 根据 Severity 区分 errors 和 warnings
                var errors = new System.Collections.Generic.List<DslDiagnostic>();
                var warnings = new System.Collections.Generic.List<DslDiagnostic>();
                foreach (var d in _viewModel.Diagnostics)
                {
                    if (d.Severity == DiagnosticSeverity.Warning)
                        warnings.Add(d);
                    else
                        errors.Add(d);
                }
                _editor.UpdateDiagnostics(errors, warnings);
            }
            else if (e.PropertyName == nameof(StoryEditorViewModel.Variables))
            {
                // 更新补全数据源
                var (vars, scenes, labels, chars) = _viewModel.GetCompletionData();
                _editor.UpdateCompletionData(vars, scenes, labels, chars);
            }
        };

        // ===== 布局 =====
        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("240,Auto,*,Auto,300"),
            RowDefinitions = RowDefinitions.Parse("*,Auto"),
            Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
        };

        // ===== 左栏：文件树 =====
        var fileTreePanel = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#252526")),
            BorderBrush = new SolidColorBrush(Color.Parse("#333333")),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = _fileTree,
        };
        grid.Children.Add(fileTreePanel);

        // ===== 分隔条 =====
        var splitter1 = new GridSplitter
        {
            Width = 4,
            Background = new SolidColorBrush(Color.Parse("#333333")),
            ResizeDirection = GridResizeDirection.Columns,
        };
        Grid.SetColumn(splitter1, 1);
        grid.Children.Add(splitter1);

        // ===== 中栏：代码编辑器 =====
        _editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        _editor.VerticalAlignment = VerticalAlignment.Stretch;
        Grid.SetColumn(_editor, 2);
        grid.Children.Add(_editor);

        // ===== 分隔条 =====
        var splitter2 = new GridSplitter
        {
            Width = 4,
            Background = new SolidColorBrush(Color.Parse("#333333")),
            ResizeDirection = GridResizeDirection.Columns,
        };
        Grid.SetColumn(splitter2, 3);
        grid.Children.Add(splitter2);

        // ===== 右栏：诊断 + 变量面板 =====
        var sidePanel = CreateSidePanel();
        Grid.SetColumn(sidePanel, 4);
        grid.Children.Add(sidePanel);

        // ===== 底部状态栏 =====
        var statusBarBorder = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#007ACC")),
            BorderBrush = new SolidColorBrush(Color.Parse("#1E1E1E")),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 16,
                Margin = new Thickness(8, 2),
                Children =
                {
                    CreateStatusBinding(nameof(StoryEditorViewModel.CurrentFileName), Brushes.White),
                    CreateStatusBinding(nameof(StoryEditorViewModel.CaretInfo), Brushes.White),
                    CreateStatusBinding(nameof(StoryEditorViewModel.DiagSummary), Brushes.White),
                    CreateStatusBinding(nameof(StoryEditorViewModel.StatusMessage), new SolidColorBrush(Color.Parse("#CCCCCC"))),
                },
            },
        };
        Grid.SetRow(statusBarBorder, 1);
        Grid.SetColumnSpan(statusBarBorder, 5);
        grid.Children.Add(statusBarBorder);

        Content = grid;
    }

    /// <summary>创建右侧面板：诊断 + 变量</summary>
    private Control CreateSidePanel()
    {
        var panel = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("*,*"),
            Background = new SolidColorBrush(Color.Parse("#252526")),
        };

        // === 诊断列表 ===
        var diagPanel = new StackPanel { Margin = new Thickness(4) };

        diagPanel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#333333")),
            Padding = new Thickness(8, 4),
            Child = new TextBlock
            {
                Text = "问题",
                FontWeight = FontWeight.Bold,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            },
        });

        var diagList = new ListBox
        {
            FontFamily = FontFamily.Parse("Consolas,Menlo,Monospace"),
            FontSize = 12,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 4, 0, 0),
        };
        var diagTemplate = new FuncDataTemplate<DslDiagnostic>((item, _) =>
            new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(4, 2),
                Children =
                {
                    new TextBlock
                    {
                        Text = $"行 {item?.Line}:{item?.Column}",
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(Color.Parse("#F44747")),
                        FontSize = 11,
                    },
                    new TextBlock
                    {
                        Text = item?.Message ?? "",
                        Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                    },
                }
            });
        diagList.ItemTemplate = diagTemplate;
        diagList.ItemsSource = _viewModel.Diagnostics;
        diagPanel.Children.Add(diagList);

        panel.Children.Add(diagPanel);

        // === 变量列表 ===
        var varPanel = new StackPanel
        {
            Margin = new Thickness(4),
        };
        Grid.SetRow(varPanel, 1);

        varPanel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#333333")),
            Padding = new Thickness(8, 4),
            Child = new TextBlock
            {
                Text = "变量",
                FontWeight = FontWeight.Bold,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            },
        });

        var varList = new ListBox
        {
            FontSize = 12,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 4, 0, 0),
        };
        var varTemplate = new FuncDataTemplate<VariableInfo>((item, _) =>
            new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(4, 2),
                Children =
                {
                    new TextBlock
                    {
                        Text = item?.Name ?? "",
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(Color.Parse("#4EC9B0")),
                        FontSize = 12,
                    },
                    new TextBlock
                    {
                        Text = $"值: {item?.Value ?? "—"}  定义行: {item?.DefinitionLine}",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.Parse("#888888")),
                    },
                }
            });
        varList.ItemTemplate = varTemplate;
        varList.ItemsSource = _viewModel.Variables;
        varPanel.Children.Add(varList);

        panel.Children.Add(varPanel);

        return panel;
    }

    /// <summary>创建绑定到 ViewModel 属性的状态栏文本（AOT 安全：手动监听 PropertyChanged）</summary>
    private TextBlock CreateStatusBinding(string propertyName, IBrush foreground)
    {
        var tb = new TextBlock
        {
            FontSize = 12,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // AOT 安全：手动订阅 PropertyChanged
        tb.Text = GetPropertyValue(propertyName) ?? "";
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == propertyName)
                tb.Text = GetPropertyValue(propertyName) ?? "";
        };
        return tb;
    }

    /// <summary>按属性名获取 ViewModel 属性值（AOT 安全：显式 switch）</summary>
    private string? GetPropertyValue(string propertyName) => propertyName switch
    {
        nameof(StoryEditorViewModel.CurrentFileName) => _viewModel.CurrentFileName,
        nameof(StoryEditorViewModel.CaretInfo) => _viewModel.CaretInfo,
        nameof(StoryEditorViewModel.DiagSummary) => _viewModel.DiagSummary,
        nameof(StoryEditorViewModel.StatusMessage) => _viewModel.StatusMessage,
        _ => null,
    };

    /// <summary>获取光标下的单词</summary>
    private string GetWordAtCaret()
    {
        var editor = _editor.InnerEditor;
        var offset = editor.CaretOffset;
        if (offset < 0 || offset > editor.Document.TextLength) return "";

        var start = offset;
        while (start > 0 && IsWordChar(editor.Document.GetCharAt(start - 1)))
            start--;

        var end = offset;
        while (end < editor.Document.TextLength && IsWordChar(editor.Document.GetCharAt(end)))
            end++;

        return editor.Document.GetText(start, end - start);
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '-';
}
