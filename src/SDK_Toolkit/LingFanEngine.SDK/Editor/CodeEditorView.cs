using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Search;
using LingFanEngine.SDK.Dsl.Highlight;
using LingFanEngine.SDK.Models;
using DslDiagnostic = LingFanEngine.SDK.Models.DslDiagnostic;

namespace LingFanEngine.SDK.Editor;

/// <summary>
/// 基于 AvaloniaEdit 的 DSL 代码编辑器控件封装。
/// <para>提供语法高亮、行号、光标追踪、文本变更通知等能力。</para>
/// </summary>
public class CodeEditorView : UserControl
{
    private readonly TextEditor _textEditor;
    private readonly DslHighlightingTransformer _highlighter;
    private DslTextMarkerService? _markerService;
    private CompletionWindow? _completionWindow;
    private DslCompletionProvider? _completionProvider;
    private string _filePath = "";
    private bool _isDirty;
    private string _lastSavedText = "";

    // 补全数据源（由 ViewModel 设置）
    private List<VariableInfo> _variables = new();
    private List<string> _scenes = new();
    private List<string> _labels = new();
    private List<string> _characters = new();

    /// <summary>文本变更时触发（用于 debounce 触发分析）</summary>
    public event Action<string>? SourceChanged;

    /// <summary>光标位置变更时触发（用于状态栏行列号）</summary>
    public event Action<(int Line, int Column)>? CaretMoved;

    /// <summary>保存快捷键 Ctrl+S 时触发</summary>
    public event Action? SaveRequested;

    /// <summary>当前文件路径</summary>
    public string FilePath
    {
        get => _filePath;
        set
        {
            _filePath = value;
            _isDirty = false;
            _lastSavedText = _textEditor.Document.Text;
        }
    }

    /// <summary>是否有未保存修改</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty != value)
            {
                _isDirty = value;
            }
        }
    }

    /// <summary>编辑器文本内容</summary>
    public string Source
    {
        get => _textEditor.Document.Text;
        set
        {
            _textEditor.Document.Text = value ?? "";
            _lastSavedText = _textEditor.Document.Text;
            _isDirty = false;
            _highlighter.SetSource(value ?? "");
            _highlighter.Invalidate();
        }
    }

    /// <summary>底层 AvaloniaEdit TextEditor（供高级扩展使用）</summary>
    public TextEditor InnerEditor => _textEditor;

    public CodeEditorView()
    {
        // 创建 TextEditor
        _textEditor = new TextEditor
        {
            FontFamily = new FontFamily("Consolas,Menlo,Monospace,Courier New"),
            FontSize = 14,
            ShowLineNumbers = true,
            WordWrap = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
            Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")),
        };

        // 安装高亮转换器
        _highlighter = new DslHighlightingTransformer();
        _textEditor.TextArea.TextView.LineTransformers.Add(_highlighter);

        // 安装文本标记服务（波浪线）
        _markerService = new DslTextMarkerService(_textEditor.TextArea.TextView);
        _textEditor.TextArea.TextView.BackgroundRenderers.Add(_markerService);

        // 文本变更 → 通知 + 标记脏
        _textEditor.Document.TextChanged += (_, _) =>
        {
            IsDirty = _textEditor.Document.Text != _lastSavedText;
            _highlighter.SetSource(_textEditor.Document.Text);
            SourceChanged?.Invoke(_textEditor.Document.Text);
        };

        // 光标移动 → 通知
        _textEditor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            CaretMoved?.Invoke((_textEditor.TextArea.Caret.Line, _textEditor.TextArea.Caret.Column));
        };

        // Ctrl+S → 保存
        _textEditor.TextArea.KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                SaveRequested?.Invoke();
            }
        };

        // 创建补全窗口
        _completionWindow = new CompletionWindow(_textEditor.TextArea)
        {
            MinWidth = 200,
            MaxHeight = 300,
        };
        _completionWindow.Closed += (_, _) => { };

        // 文本输入 → 触发补全
        _textEditor.TextArea.TextEntered += OnTextEntered;
        _textEditor.TextArea.TextEntering += OnTextEntering;

        // F12 → Go to Definition
        _textEditor.TextArea.KeyDown += (_, e) =>
        {
            if (e.Key == Key.F12)
            {
                e.Handled = true;
                GoToDefinitionRequested?.Invoke();
            }
        };

        // 安装搜索面板（Ctrl+F）
        SearchPanel.Install(_textEditor);

        Content = _textEditor;
    }

    /// <summary>加载文件内容到编辑器</summary>
    public void LoadFile(string path, string content)
    {
        _filePath = path;
        _textEditor.Document.Text = content;
        _lastSavedText = content;
        _isDirty = false;
        _highlighter.SetSource(content);
        _highlighter.Invalidate();
    }

    /// <summary>标记为已保存</summary>
    public void MarkSaved()
    {
        _lastSavedText = _textEditor.Document.Text;
        _isDirty = false;
    }

    /// <summary>更新高亮 token（由 ViewModel 调用）</summary>
    public void UpdateHighlights(string source)
    {
        _highlighter.SetSource(source);
        _textEditor.TextArea.TextView.Redraw();
    }

    /// <summary>更新诊断标记（波浪线）</summary>
    public void UpdateDiagnostics(List<DslDiagnostic> errors, List<DslDiagnostic> warnings)
    {
        _markerService?.UpdateMarkers(errors, warnings, _textEditor.Document);
        _textEditor.TextArea.TextView.Redraw();
    }

    /// <summary>滚动到指定行</summary>
    public void ScrollToLine(int line)
    {
        _textEditor.ScrollToLine(line);
        _textEditor.TextArea.Caret.Line = line;
    }

    /// <summary>Go to Definition 请求</summary>
    public event Action? GoToDefinitionRequested;

    /// <summary>更新补全数据源</summary>
    public void UpdateCompletionData(
        List<VariableInfo> variables,
        List<string> scenes,
        List<string> labels,
        List<string> characters)
    {
        _variables = variables;
        _scenes = scenes;
        _labels = labels;
        _characters = characters;
        _completionProvider ??= new DslCompletionProvider();
    }

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        // 补全窗口选择项时按空格/回车确认
        if (e.Text is { Length: 1 } && (e.Text[0] == ' ' || e.Text[0] == '\n' || e.Text[0] == '\t'))
        {
            if (_completionWindow?.IsVisible == true)
            {
                // 让 CompletionWindow 处理
            }
        }
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (_completionProvider == null) return;

        var offset = _textEditor.CaretOffset;
        var completions = _completionProvider.GetCompletions(
            _textEditor.Document, offset, _variables, _scenes, _labels, _characters);

        var list = new List<ICompletionData>(completions);
        if (list.Count == 0) return;

        // 计算当前正在输入的单词范围
        var wordStart = offset;
        while (wordStart > 0 && IsWordChar(_textEditor.Document.GetCharAt(wordStart - 1)))
            wordStart--;

        _completionWindow ??= new CompletionWindow(_textEditor.TextArea);
        _completionWindow.Show();
        _completionWindow.CompletionList.CompletionData.Clear();
        foreach (var item in list)
            _completionWindow.CompletionList.CompletionData.Add(item);
        _completionWindow.CompletionList.SelectItem(_textEditor.Document.GetText(wordStart, offset - wordStart));
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '-';
}
