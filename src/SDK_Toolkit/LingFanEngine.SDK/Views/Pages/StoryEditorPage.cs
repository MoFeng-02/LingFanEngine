using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.SDK.Editor;
using LingFanEngine.SDK.ViewModels;
using LingFanEngine.SDK.Views.Components;
using LingFanEngine.SDK.Models;
using MFToolkit.Routing.Core.Interfaces;

namespace LingFanEngine.SDK.Views.Pages;

/// <summary>
/// 故事编辑器页面——编辑区 + 右侧面板（诊断/变量/引用/大纲）。
/// <para>实现 INavigationAware：接收导航参数（filePath）、离开时保存编辑器状态。</para>
/// </summary>
public class StoryEditorPage : UserControl, INavigationAware
{
    private readonly StoryEditorViewModel _viewModel;
    private readonly CodeEditorView _editor;
    private readonly EditorTabBar _tabBar;
    private readonly ListBox _referencesList;
    private readonly ListBox _outlineList;
    private FileTreeView? _externalFileTree;

    public StoryEditorPage(StoryEditorViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        // 创建组件
        _editor = new CodeEditorView();
        _tabBar = new EditorTabBar();
        _referencesList = new ListBox
        {
            FontFamily = FontFamily.Parse("Consolas,Menlo,Monospace"),
            FontSize = 12,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        _outlineList = new ListBox
        {
            FontSize = 12,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };

        InitializeComponent();

        // P1-5: 绑定标签页栏
        _tabBar.TabSwitched += OnTabSwitched;
        _tabBar.TabCloseRequested += OnTabCloseRequested;
    }

    /// <summary>设置外部文件树引用（由 WorkspaceWindow 调用）</summary>
    public void SetExternalFileTree(FileTreeView fileTree)
    {
        _externalFileTree = fileTree;
    }

    // ===== INavigationAware 实现 =====

    /// <summary>页面被导航到时调用——接收导航参数</summary>
    public void OnNavigated(Dictionary<string, object?>? parameters)
    {
        // 接收 filePath 参数，自动打开文件
        if (parameters != null && parameters.TryGetValue("filePath", out var path) && path is string filePath)
        {
            _ = _viewModel.OpenFileCommand.ExecuteAsync(filePath);
        }
    }

    /// <summary>即将离开页面时调用——保存编辑器状态</summary>
    public void OnNavigatingFrom()
    {
        SaveCurrentEditorState();
    }

    /// <summary>已离开页面时调用</summary>
    public void OnNavigatedFrom()
    {
    }

    // ===== 安全的编辑器状态保存 =====

    /// <summary>保存当前编辑器状态到 ViewModel（模板未应用时安全跳过）</summary>
    private void SaveCurrentEditorState()
    {
        if (!_editor.IsTemplateApplied) return;
        try
        {
            var caret = _editor.InnerEditor.TextArea.Caret;
            var scrollOffset = _editor.InnerEditor.VerticalOffset;
            _viewModel.SaveActiveTabState(caret.Line, caret.Column, scrollOffset);
        }
        catch { }
    }

    /// <summary>同步编辑器内容到当前活动标签页</summary>
    private void SyncEditorToActiveTab()
    {
        if (_viewModel.ActiveTab != null)
        {
            _viewModel.ActiveTab.Content = _editor.Source;
        }
    }

    private void InitializeComponent()
    {
        // ===== 事件绑定 =====

        // 编辑器文本变更 → debounce 分析 + 实时脏标记
        _editor.SourceChanged += async source =>
        {
            await _viewModel.OnSourceChangedAsync(source);
            // 不在此处调用 UpdateHighlights——避免在快速输入时与 TextView 渲染竞争
            // 高亮更新由 DslHighlightingTransformer 的 SetSource + 下次渲染自动完成
            UpdateFileTreeDirtyStates();
        };

        // 编辑器光标移动 → 状态栏
        _editor.CaretMoved += pos => _viewModel.UpdateCaretInfo(pos.Line, pos.Column);

        // 编辑器保存
        _editor.SaveRequested += async () =>
        {
            await _viewModel.SaveFileCommand.ExecuteAsync(null);
            UpdateFileTreeDirtyStates();
        };

        // 编辑器 Go to Definition
        _editor.GoToDefinitionRequested += async () =>
        {
            var word = _editor.GetWordAtCaret();
            if (string.IsNullOrEmpty(word)) return;
            var result = _viewModel.GoToDefinition(word);
            if (result != null)
            {
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

        // Find All References
        _editor.FindReferencesRequested += async () =>
        {
            var word = _editor.GetWordAtCaret();
            if (string.IsNullOrEmpty(word)) return;
            await _viewModel.FindAllReferencesAsync(word);
        };

        // Hover 提示
        _editor.HoverRequested += () =>
        {
            var word = _editor.GetWordAtCaret();
            if (string.IsNullOrEmpty(word)) return;
            var hoverText = _viewModel.GetHoverText(word);
            if (!string.IsNullOrEmpty(hoverText))
            {
                ToolTip.SetTip(_editor.InnerEditor, hoverText);
                ToolTip.SetIsOpen(_editor.InnerEditor, true);
            }
        };

        // ViewModel 属性变化 → 更新编辑器
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StoryEditorViewModel.EditorContent))
            {
                if (_editor.Source != _viewModel.EditorContent)
                {
                    _editor.LoadFile(_viewModel.CurrentFilePath, _viewModel.EditorContent);
                    _editor.UpdateHighlights(_viewModel.EditorContent);

                    // 恢复光标位置和滚动位置
                    var (line, col, scrollOffset) = _viewModel.GetTabRestoreState();
                    try
                    {
                        _editor.InnerEditor.TextArea.Caret.Line = line;
                        _editor.InnerEditor.TextArea.Caret.Column = col;
                        if (scrollOffset > 0)
                            _editor.InnerEditor.ScrollToVerticalOffset(scrollOffset);
                    }
                    catch { }
                }
            }
        };

        // 诊断标记通过 CollectionChanged 实时更新
        _viewModel.Diagnostics.CollectionChanged += OnDiagnosticsChanged;
        _viewModel.Variables.CollectionChanged += OnVariablesChanged;

        // 引用列表点击 → 跳转
        _referencesList.SelectionChanged += async (_, _) =>
        {
            if (_referencesList.SelectedItem is ReferenceResult refResult)
            {
                if (refResult.FilePath == _viewModel.CurrentFilePath)
                {
                    _editor.ScrollToLine(refResult.Line);
                }
                else
                {
                    await _viewModel.OpenFileCommand.ExecuteAsync(refResult.FilePath);
                    _editor.ScrollToLine(refResult.Line);
                }
            }
        };

        // 大纲列表点击 → 跳转
        _outlineList.SelectionChanged += (_, _) =>
        {
            if (_outlineList.SelectedItem is OutlineItem item)
            {
                _editor.ScrollToLine(item.Line);
            }
        };

        // ===== 布局 =====
        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,300"),
            RowDefinitions = RowDefinitions.Parse("*,Auto"),
            Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        // ===== 中栏：标签页栏 + 代码编辑器 =====
        var editorPanel = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        _tabBar.MinHeight = 28;
        Grid.SetRow(_tabBar, 0);
        editorPanel.Children.Add(_tabBar);

        // 同步 ViewModel.OpenFiles 到 TabBar.Tabs
        SyncTabBarTabs();
        _viewModel.OpenFiles.CollectionChanged += (_, _) => SyncTabBarTabs();

        Grid.SetRow(_editor, 1);
        editorPanel.Children.Add(_editor);

        grid.Children.Add(editorPanel);

        // ===== 分隔条 =====
        var splitter2 = new GridSplitter
        {
            Width = 4,
            Background = new SolidColorBrush(Color.Parse("#333333")),
            ResizeDirection = GridResizeDirection.Columns,
        };
        Grid.SetColumn(splitter2, 1);
        grid.Children.Add(splitter2);

        // ===== 右栏：诊断 + 变量 + 引用 + 大纲 =====
        var sidePanel = CreateSidePanel();
        Grid.SetColumn(sidePanel, 2);
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
        Grid.SetColumnSpan(statusBarBorder, 3);
        grid.Children.Add(statusBarBorder);

        Content = grid;
    }

    /// <summary>同步 TabBar 标签列表</summary>
    private void SyncTabBarTabs()
    {
        _tabBar.Tabs.Clear();
        foreach (var t in _viewModel.OpenFiles)
            _tabBar.Tabs.Add(t);
    }

    /// <summary>诊断集合变化时更新编辑器波浪线</summary>
    private void OnDiagnosticsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var errors = new List<DslDiagnostic>();
        var warnings = new List<DslDiagnostic>();
        var infos = new List<DslDiagnostic>();
        foreach (var d in _viewModel.Diagnostics)
        {
            if (d.Severity == DiagnosticSeverity.Warning)
                warnings.Add(d);
            else if (d.Severity == DiagnosticSeverity.Info)
                infos.Add(d);
            else
                errors.Add(d);
        }
        _editor.UpdateDiagnosticsWithInfo(errors, warnings, infos);
    }

    /// <summary>变量集合变化时更新补全数据</summary>
    private void OnVariablesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var (vars, scenes, labels, chars) = _viewModel.GetCompletionData();
        _editor.UpdateCompletionData(vars, scenes, labels, chars);
    }

    /// <summary>创建右侧面板：诊断 + 变量 + 引用 + 大纲</summary>
    private Control CreateSidePanel()
    {
        var panel = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("*,*,*,*"),
            Background = new SolidColorBrush(Color.Parse("#252526")),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
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
                        Foreground = new SolidColorBrush(
                            item?.Severity == DiagnosticSeverity.Warning ? Color.Parse("#CCA700") :
                            item?.Severity == DiagnosticSeverity.Info ? Color.Parse("#75BEFF") :
                            Color.Parse("#F44747")),
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

        // === 引用列表 ===
        var refPanel = new StackPanel
        {
            Margin = new Thickness(4),
        };
        Grid.SetRow(refPanel, 2);

        refPanel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#333333")),
            Padding = new Thickness(8, 4),
            Child = new TextBlock
            {
                Text = "引用",
                FontWeight = FontWeight.Bold,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            },
        });

        _referencesList.ItemTemplate = new FuncDataTemplate<ReferenceResult>((item, _) =>
            new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(4, 2),
                Children =
                {
                    new TextBlock
                    {
                        Text = $"{Path.GetFileName(item?.FilePath ?? "")}:{item?.Line}",
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(Color.Parse("#DCDCAA")),
                        FontSize = 11,
                    },
                    new TextBlock
                    {
                        Text = item?.LineText ?? "",
                        Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                    },
                }
            });
        _referencesList.ItemsSource = _viewModel.References;
        refPanel.Children.Add(_referencesList);

        panel.Children.Add(refPanel);

        // === 大纲列表 ===
        var outlinePanel = new StackPanel
        {
            Margin = new Thickness(4),
        };
        Grid.SetRow(outlinePanel, 3);

        outlinePanel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#333333")),
            Padding = new Thickness(8, 4),
            Child = new TextBlock
            {
                Text = "大纲",
                FontWeight = FontWeight.Bold,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            },
        });

        _outlineList.ItemTemplate = new FuncDataTemplate<OutlineItem>((item, _) =>
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(4, 2),
                Children =
                {
                    new TextBlock
                    {
                        Text = item?.Type ?? "",
                        Foreground = new SolidColorBrush(Color.Parse("#569CD6")),
                        FontSize = 11,
                    },
                    new TextBlock
                    {
                        Text = item?.Name ?? "",
                        Foreground = new SolidColorBrush(Color.Parse("#DCDCAA")),
                        FontSize = 11,
                    },
                }
            });
        _outlineList.ItemsSource = _viewModel.OutlineItems;
        outlinePanel.Children.Add(_outlineList);

        panel.Children.Add(outlinePanel);

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

    // ===== 标签页事件处理（NRE 安全） =====

    /// <summary>标签页切换处理——保存当前编辑器状态，加载新标签内容</summary>
    private void OnTabSwitched(OpenFileTab tab)
    {
        if (tab == null || tab.IsActive) return;

        // 保存当前编辑器状态（模板未应用时安全跳过）
        SaveCurrentEditorState();

        // 切换标签（触发 EditorContent 变化 → 编辑器加载新内容）
        _viewModel.SwitchToTab(tab);
    }

    /// <summary>标签页关闭处理</summary>
    private void OnTabCloseRequested(OpenFileTab tab)
    {
        if (tab == null) return;

        // 如果标签有未保存修改，弹出确认对话框
        if (tab.IsDirty)
        {
            var dialog = new Window
            {
                Title = "保存确认",
                Width = 360,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.Parse("#252526")),
                CanResize = false,
            };

            var owner = this.VisualRoot as Window;
            var panel = new StackPanel
            {
                Margin = new Thickness(24, 20, 24, 16),
                Spacing = 16,
            };
            panel.Children.Add(new TextBlock
            {
                Text = $"文件 \"{tab.FileName}\" 有未保存的修改。\n是否在关闭前保存？",
                Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
            });

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
            };

            var saveBtn = new Button { Content = "保存", Padding = new Thickness(16, 6) };
            var noSaveBtn = new Button { Content = "不保存", Padding = new Thickness(16, 6) };
            var cancelBtn = new Button { Content = "取消", Padding = new Thickness(16, 6) };

            saveBtn.Click += async (_, _) =>
            {
                dialog.Close();
                // 先切换到要关闭的标签，保存内容
                if (tab.IsActive)
                {
                    SaveCurrentEditorState();
                }
                else
                {
                    _viewModel.SwitchToTab(tab);
                }
                SyncEditorToActiveTab();
                await _viewModel.SaveFileCommand.ExecuteAsync(null);
                _viewModel.CloseTabCommand.Execute(tab);
                UpdateFileTreeDirtyStates();
            };

            noSaveBtn.Click += (_, _) =>
            {
                dialog.Close();
                if (tab.IsActive)
                {
                    SaveCurrentEditorState();
                }
                _viewModel.CloseTabCommand.Execute(tab);
                UpdateFileTreeDirtyStates();
            };

            cancelBtn.Click += (_, _) => dialog.Close();

            btnPanel.Children.Add(saveBtn);
            btnPanel.Children.Add(noSaveBtn);
            btnPanel.Children.Add(cancelBtn);
            panel.Children.Add(btnPanel);

            dialog.Content = panel;

            if (owner != null)
                dialog.ShowDialog(owner);
            else
                dialog.Show();
            return;
        }

        // 如果关闭的是活动标签，先保存编辑器状态
        if (tab.IsActive)
        {
            SaveCurrentEditorState();
        }

        _viewModel.CloseTabCommand.Execute(tab);
        UpdateFileTreeDirtyStates();
    }

    /// <summary>更新文件树中的脏标记</summary>
    private void UpdateFileTreeDirtyStates()
    {
        if (_externalFileTree == null) return;
        foreach (var tab in _viewModel.OpenFiles)
        {
            _externalFileTree.MarkFileDirty(tab.FilePath, tab.IsDirty);
        }
    }
}
