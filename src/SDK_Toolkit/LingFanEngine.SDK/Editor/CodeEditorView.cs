using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Search;
using LingFanEngine.SDK.Models;
using DslDiagnostic = LingFanEngine.SDK.Models.DslDiagnostic;

namespace LingFanEngine.SDK.Editor;

/// <summary>
/// 基于 AvaloniaEdit 的 DSL 代码编辑器控件封装。
/// <para>P0-3: Ctrl+Click Go to Definition。</para>
/// <para>P0-4: Shift+F12 Find All References。</para>
/// <para>P1-2: Hover 提示。</para>
/// <para>P1-3: 括号/引号匹配高亮。</para>
/// <para>P1-4: 自动缩进。</para>
/// <para>P2-1: 代码折叠。</para>
/// <para>P2-2: Ctrl+Shift+F 格式化。</para>
/// </summary>
public class CodeEditorView : UserControl
{
    private readonly TextEditor _textEditor;
    private readonly DslHighlightingTransformer _highlighter;
    private DslTextMarkerService? _markerService;
    private CompletionWindow? _completionWindow;
    private DslCompletionProvider _completionProvider = new();
    private FoldingManager? _foldingManager;
    private readonly DslFoldingStrategy _foldingStrategy = new();
    private string _filePath = "";
    private bool _isDirty;
    private string _lastSavedText = "";

    // 标记模板是否已应用（TextArea.TextView 在模板应用后才可用）
    private bool _isTemplateApplied;

    /// <summary>模板是否已应用（TextView 可用，外部可据此做守卫判断）</summary>
    public bool IsTemplateApplied => _isTemplateApplied;

    // 补全数据源（由 ViewModel 设置）
    private List<VariableInfo> _variables = new();
    private List<string> _scenes = new();
    private List<string> _labels = new();
    private List<string> _characters = new();

    // P0-4: Enter 键标记，供 OnTextEntered 检测
    private bool _isEnterKey;

    // P1-3: 括号匹配状态
    private bool _bracketHighlightActive;

    /// <summary>文本变更时触发（用于 debounce 触发分析）</summary>
    public event Action<string>? SourceChanged;

    /// <summary>光标位置变更时触发（用于状态栏行列号）</summary>
    public event Action<(int Line, int Column)>? CaretMoved;

    /// <summary>保存快捷键 Ctrl+S 时触发</summary>
    public event Action? SaveRequested;

    /// <summary>Go to Definition 请求</summary>
    public event Action? GoToDefinitionRequested;

    /// <summary>Find All References 请求（P0-4）</summary>
    public event Action? FindReferencesRequested;

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
                DirtyChanged?.Invoke(_isDirty);
            }
        }
    }

    /// <summary>IsDirty 变化通知（P1-7）</summary>
    public event Action<bool>? DirtyChanged;

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
        Padding = new Thickness(0);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

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

        _highlighter = new DslHighlightingTransformer();

        // Document 级别事件——Document 在构造函数中创建，模板应用前即可安全访问
        _textEditor.Document.TextChanged += (_, _) =>
        {
            IsDirty = _textEditor.Document.Text != _lastSavedText;
            _highlighter.SetSource(_textEditor.Document.Text);
            SourceChanged?.Invoke(_textEditor.Document.Text);
        };

        // TextArea 级别事件——TextArea 在构造函数中创建，但 TextView 是模板部件
        // 延迟到模板应用后再安装所有依赖 TextView 的组件
        _textEditor.TemplateApplied += OnTextEditorTemplateApplied;

        Content = _textEditor;
    }

    /// <summary>
    /// TextEditor 模板应用后初始化所有依赖 TextArea.TextView 的组件。
    /// TextView 是模板部件（TemplatePart），在控件加入可视化树并应用模板后才可用。
    /// </summary>
    private void OnTextEditorTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        // 防止重复初始化
        _textEditor.TemplateApplied -= OnTextEditorTemplateApplied;
        _isTemplateApplied = true;

        var textView = _textEditor.TextArea.TextView;

        // 安装高亮转换器
        textView.LineTransformers.Add(_highlighter);

        // 安装文本标记服务（波浪线 + 括号高亮）
        _markerService = new DslTextMarkerService(textView);
        textView.BackgroundRenderers.Add(_markerService);

        // 光标移动 → 通知 + 括号匹配
        _textEditor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            CaretMoved?.Invoke((_textEditor.TextArea.Caret.Line, _textEditor.TextArea.Caret.Column));
            UpdateBracketMatch();
        };

        // Hover 提示（P1-2）
        textView.PointerHover += OnPointerHover;

        // Ctrl+Click Go to Definition（P0-3）
        _textEditor.TextArea.PointerPressed += OnPointerPressed;

        // 快捷键——用 handledEventsToo 确保不被 AvaloniaEdit 内部拦截
        _textEditor.TextArea.AddHandler(InputElement.KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);

        // 文本输入 → 触发补全
        _textEditor.TextArea.TextEntered += OnTextEntered;
        _textEditor.TextArea.TextEntering += OnTextEntering;

        // 安装搜索面板（Ctrl+F）
        SearchPanel.Install(_textEditor);

        // 安装折叠管理器（P2-1）
        _foldingManager = FoldingManager.Install(_textEditor.TextArea);

        // 如果在模板应用前已有内容加载，触发一次高亮和折叠更新
        if (!string.IsNullOrEmpty(_textEditor.Document.Text))
        {
            _highlighter.SetSource(_textEditor.Document.Text);
            _highlighter.Invalidate();
            textView.Redraw();
            UpdateFoldings();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+S → 保存
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            SaveRequested?.Invoke();
            return;
        }

        // F12 → Go to Definition
        if (e.Key == Key.F12 && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            GoToDefinitionRequested?.Invoke();
            return;
        }

        // Shift+F12 → Find All References（P0-4）
        if (e.Key == Key.F12 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            FindReferencesRequested?.Invoke();
            return;
        }

        // Alt+Shift+F → 格式化文档（智能缩进 + key=value 间距修正）
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            var formatted = Dsl.DslFormatter.Format(_textEditor.Document.Text);
            FormatDocument(formatted);
            // 同步到 ViewModel
            if (_textEditor.Document.Text != formatted)
            {
                _textEditor.Document.Text = formatted;
            }
            return;
        }

        // Tab → 补全选择（如果补全窗口可见）
        if (e.Key == Key.Tab && _completionWindow?.IsVisible == true)
        {
            // 让 CompletionWindow 自己处理 Tab 补全
            // AvaloniaEdit 的 CompletionList 会自动拦截 Tab
        }

        // Enter → 自动缩进（P1-4）
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // 如果补全窗口可见，让补全优先处理 Enter
            if (_completionWindow?.IsVisible == true) return;
            _isEnterKey = true;
        }
    }

    /// <summary>P1-4: 自动缩进——在 TextEntered 后插入缩进</summary>
    private void HandleAutoIndent()
    {
        var editor = _textEditor;
        var document = editor.Document;
        var caretOffset = editor.CaretOffset;

        var currentLine = document.GetLineByOffset(caretOffset);
        var lineText = document.GetText(currentLine);

        if (!string.IsNullOrEmpty(lineText.Trim()))
            return;

        if (currentLine.LineNumber <= 1) return;
        var prevLine = document.GetLineByNumber(currentLine.LineNumber - 1);
        var prevText = document.GetText(prevLine);
        var prevTrimmed = prevText.Trim();

        var indent = "";
        foreach (var c in prevText)
        {
            if (c == ' ' || c == '\t')
                indent += c;
            else
                break;
        }

        var firstWord = GetFirstWord(prevTrimmed);
        var blockStarters = new HashSet<string>
        {
            "scene", "if", "while", "for", "func", "switch", "foreach",
        };

        if (blockStarters.Contains(firstWord))
        {
            indent += "    ";
        }

        if (!string.IsNullOrEmpty(indent))
        {
            document.Insert(caretOffset, indent);
        }
    }

    /// <summary>P1-3: 更新括号/引号匹配高亮</summary>
    private void UpdateBracketMatch()
    {
        if (_markerService == null || !_isTemplateApplied) return;

        var document = _textEditor.Document;
        var caretOffset = _textEditor.CaretOffset;

        var checkOffset = caretOffset > 0 ? caretOffset - 1 : caretOffset;
        if (checkOffset < 0 || checkOffset >= document.TextLength)
        {
            if (_bracketHighlightActive)
            {
                _markerService.ClearBracketHighlight();
                _textEditor.TextArea.TextView.Redraw();
                _bracketHighlightActive = false;
            }
            return;
        }

        var ch = document.GetCharAt(checkOffset);
        char? matchChar = ch switch
        {
            '{' => '}',
            '}' => '{',
            '(' => ')',
            ')' => '(',
            '[' => ']',
            ']' => '[',
            '"' => '"',
            _ => null,
        };

        if (matchChar == null)
        {
            if (caretOffset < document.TextLength)
            {
                ch = document.GetCharAt(caretOffset);
                matchChar = ch switch
                {
                    '{' => '}',
                    '}' => '{',
                    '(' => ')',
                    ')' => '(',
                    '[' => ']',
                    ']' => '[',
                    '"' => '"',
                    _ => null,
                };
                if (matchChar != null)
                    checkOffset = caretOffset;
            }
        }

        if (matchChar == null)
        {
            if (_bracketHighlightActive)
            {
                _markerService.ClearBracketHighlight();
                _textEditor.TextArea.TextView.Redraw();
                _bracketHighlightActive = false;
            }
            return;
        }

        var matchOffset = FindMatchingBracket(document, checkOffset, ch, matchChar.Value);
        if (matchOffset < 0)
        {
            if (_bracketHighlightActive)
            {
                _markerService.ClearBracketHighlight();
                _textEditor.TextArea.TextView.Redraw();
                _bracketHighlightActive = false;
            }
            return;
        }

        var line1 = document.GetLineByOffset(checkOffset);
        var line2 = document.GetLineByOffset(matchOffset);
        var col1 = checkOffset - line1.Offset + 1;
        var col2 = matchOffset - line2.Offset + 1;

        _markerService.SetBracketHighlight(
            line1.LineNumber, col1, 1,
            line2.LineNumber, col2, 1);
        _textEditor.TextArea.TextView.Redraw();
        _bracketHighlightActive = true;
    }

    /// <summary>搜索匹配的括号/引号（考虑嵌套）</summary>
    private static int FindMatchingBracket(AvaloniaEdit.Document.TextDocument document, int startOffset, char openChar, char closeChar)
    {
        if (openChar == closeChar)
        {
            for (var i = startOffset + 1; i < document.TextLength; i++)
            {
                if (document.GetCharAt(i) == closeChar)
                    return i;
            }
            return -1;
        }

        var isForward = openChar is '{' or '(' or '[';
        if (isForward)
        {
            var depth = 1;
            for (var i = startOffset + 1; i < document.TextLength; i++)
            {
                var c = document.GetCharAt(i);
                if (c == openChar) depth++;
                else if (c == closeChar)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
        }
        else
        {
            var depth = 1;
            for (var i = startOffset - 1; i >= 0; i--)
            {
                var c = document.GetCharAt(i);
                if (c == openChar) depth++;
                else if (c == closeChar)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
        }
        return -1;
    }

    /// <summary>P0-3: Ctrl+Click Go to Definition</summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            GoToDefinitionRequested?.Invoke();
        }
    }

    /// <summary>P1-2: Hover 提示</summary>
    private void OnPointerHover(object? sender, PointerEventArgs e)
    {
        HoverRequested?.Invoke();
    }

    /// <summary>Hover 请求事件（P1-2，由 ViewModel 处理查找和显示）</summary>
    public event Action? HoverRequested;

    /// <summary>加载文件内容到编辑器</summary>
    public void LoadFile(string path, string content)
    {
        _filePath = path;
        _textEditor.Document.Text = content;
        _lastSavedText = content;
        _isDirty = false;
        _highlighter.SetSource(content);
        if (_isTemplateApplied)
        {
            _highlighter.Invalidate();
            _textEditor.TextArea.TextView.Redraw();
        }
        UpdateFoldings();
    }

    /// <summary>标记为已保存</summary>
    public void MarkSaved()
    {
        _lastSavedText = _textEditor.Document.Text;
        IsDirty = false;
    }

    /// <summary>更新高亮 token（由 ViewModel 调用）</summary>
    public void UpdateHighlights(string source)
    {
        _highlighter.SetSource(source);
        if (_isTemplateApplied)
            _textEditor.TextArea.TextView.Redraw();
        UpdateFoldings();
    }

    /// <summary>更新诊断标记（波浪线）</summary>
    public void UpdateDiagnostics(List<DslDiagnostic> errors, List<DslDiagnostic> warnings)
    {
        if (!_isTemplateApplied) return;
        _markerService?.UpdateMarkers(errors, warnings, _textEditor.Document);
        _textEditor.TextArea.TextView.Redraw();
    }

    /// <summary>更新诊断标记（含 Info 级别，P1-1）</summary>
    public void UpdateDiagnosticsWithInfo(List<DslDiagnostic> errors, List<DslDiagnostic> warnings, List<DslDiagnostic> infos)
    {
        if (!_isTemplateApplied) return;
        _markerService?.UpdateMarkersWithInfo(errors, warnings, infos, _textEditor.Document);
        _textEditor.TextArea.TextView.Redraw();
    }

    /// <summary>滚动到指定行</summary>
    public void ScrollToLine(int line)
    {
        _textEditor.ScrollToLine(line);
        _textEditor.TextArea.Caret.Line = line;
    }

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
    }

    /// <summary>获取光标下的单词（P0-3/P0-4 共用）</summary>
    public string GetWordAtCaret()
    {
        var editor = _textEditor;
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

    /// <summary>P2-1: 更新折叠区段</summary>
    private void UpdateFoldings()
    {
        if (_foldingManager == null) return;
        var foldings = _foldingStrategy.CreateNewFoldings(_textEditor.Document, out var firstError);
        _foldingManager.UpdateFoldings(foldings, firstError);
    }

    /// <summary>P2-2: 格式化当前文档</summary>
    public void FormatDocument(string formattedText)
    {
        var caretLine = _textEditor.TextArea.Caret.Line;
        var caretColumn = _textEditor.TextArea.Caret.Column;
        _textEditor.Document.Text = formattedText;
        _highlighter.SetSource(formattedText);
        _highlighter.Invalidate();
        try
        {
            _textEditor.TextArea.Caret.Line = caretLine;
            _textEditor.TextArea.Caret.Column = caretColumn;
        }
        catch { }
        UpdateFoldings();
    }

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
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
        if (_isEnterKey)
        {
            _isEnterKey = false;
            HandleAutoIndent();
        }

        // 空格 → 尝试 snippet 展开；不匹配则关闭补全窗口，不弹新补全
        // （补全只在输入单词字符时触发，空格后等用户打字再弹）
        if (e.Text == " ")
        {
            if (TryExpandSnippet())
                return;
            _completionWindow?.Close();
            return;
        }

        // 引号/花括号/逗号/注释符关闭 → 关闭补全窗口，不弹新补全
        if (e.Text is "\"" or "}" or "," or "#" or "/")
        {
            _completionWindow?.Close();
            return;
        }

        // 补全（仅单词字符触发）
        ShowCompletions();
    }

    /// <summary>快捷模板——输入关键字+空格自动展开</summary>
    private bool TryExpandSnippet()
    {
        // 字符串内不展开 snippet（防止对话文本中的关键字误触发）
        if (IsInsideStringAtCaret())
            return false;

        var doc = _textEditor.Document;
        var offset = _textEditor.CaretOffset;
        // 获取当前行
        var line = doc.GetLineByOffset(offset);
        var textLen = offset - line.Offset - 1; // -1 去掉刚输入的空格
        if (textLen < 0) return false; // 行首输入空格，无内容可展开
        var lineText = doc.GetText(line.Offset, textLen);
        var trimmed = lineText.TrimStart();
        if (string.IsNullOrEmpty(trimmed)) return false; // 空行或纯空白
        var indent = lineText[..^trimmed.Length]; // 保留缩进

        // snippet 是关键字之后的内容（不含前导空格，空格由替换逻辑补充）
        var snippet = trimmed switch
        {
            "say" => "\"\" speaker=\"\"",
            "scene" => "\"\" type=game\n" + indent + "    ",
            "label" => "",
            "if" => "{true}\n" + indent + "    ",
            "while" => "{true}\n" + indent + "    ",
            "for" => "\"i\" in {0..10}\n" + indent + "    ",
            "menu" => "\"选择\" {\n" + indent + "    \"选项1\" -> label1,\n" + indent + "    \"选项2\" -> label2,\n" + indent + "}",
            "character" => "\"key\" name=\"名字\" color=\"#FFFFFF\"",
            "style" => "\"name\" color=#FFFFFF size=18",
            "navigate" => "\"scene_name\"",
            "background" => "\"path/to/bg.png\"",
            "bgm" => "\"path/to/music.ogg\" volume=0.8",
            "transition" => "\"fade\" duration=1.0",
            "show" => "\"target\"",
            "hide" => "\"target\"",
            "animate" => "\"target\" property=\"x\" target=100 duration=1.0",
            "sprite" => "\"id\" src=\"path.png\"",
            _ => null,
        };
        if (snippet == null) return false;

        // 删除已输入的关键字+空格，插入 关键字+空格+snippet
        var wordStart = offset - trimmed.Length - 1; // -1 for space
        var replacement = trimmed + " " + snippet;
        doc.Replace(wordStart, offset - wordStart, replacement);

        // 定位光标
        if (trimmed == "say")
            // say "" speaker="" → 光标在第一个 " 内（say 后空格+引号 = trimmed.Length + 2）
            _textEditor.CaretOffset = wordStart + trimmed.Length + 2;
        else
            // 其他 → 光标在 snippet 末尾
            _textEditor.CaretOffset = wordStart + replacement.Length;

        return true;
    }

    private void ShowCompletions()
    {
        try
        {
            var offset = _textEditor.CaretOffset;
            var completions = _completionProvider.GetCompletions(
                _textEditor.Document, offset, _variables, _scenes, _labels, _characters);

            var list = new List<ICompletionData>(completions);
            if (list.Count == 0)
            {
                if (_completionWindow?.IsVisible == true)
                    _completionWindow.Close();
                return;
            }

            var wordStart = offset;
            while (wordStart > 0 && IsWordChar(_textEditor.Document.GetCharAt(wordStart - 1)))
                wordStart--;

            // 每次创建新窗口——复用旧窗口在 Avalonia 12.x 中会出 NRE
            // 因为 CompletionWindow.Close() 后内部状态不一致
            _completionWindow?.Close();
            _completionWindow = new CompletionWindow(_textEditor.TextArea)
            {
                MinWidth = 200,
                MaxHeight = 300,
            };

            // 关键修复：设置补全区域为当前单词范围，使 Complete() 替换而非追加
            // CompletionWindowBase 构造函数默认 StartOffset=EndOffset=CaretOffset（零长度），
            // 导致选中补全项后文本被追加而非替换（如输入 s 选 say 变成 ssay）
            _completionWindow.StartOffset = wordStart;
            _completionWindow.EndOffset = offset;

            // 填充数据后再 Show
            if (_completionWindow.CompletionList == null) return;

            _completionWindow.CompletionList.CompletionData.Clear();
            foreach (var item in list)
                _completionWindow.CompletionList.CompletionData.Add(item);

            // 设置初始过滤词
            var filterWord = _textEditor.Document.GetText(wordStart, offset - wordStart);
            _completionWindow.CompletionList.SelectItem(filterWord);

            // 最后 Show
            _completionWindow.Show();
        }
        catch { }
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '-';

    /// <summary>检测光标是否在未闭合的字符串内（同一行内的引号配对检测）</summary>
    private bool IsInsideStringAtCaret()
    {
        var doc = _textEditor.Document;
        var offset = _textEditor.CaretOffset;
        var line = doc.GetLineByOffset(offset);
        var lineText = doc.GetText(line);
        var column = offset - line.Offset;

        bool inString = false;
        for (int i = 0; i < column && i < lineText.Length; i++)
        {
            if (lineText[i] == '"' && (i == 0 || lineText[i - 1] != '\\'))
                inString = !inString;
        }
        return inString;
    }

    private static string GetFirstWord(string trimmedLine)
    {
        var spaceIdx = trimmedLine.IndexOf(' ');
        if (spaceIdx < 0)
            return trimmedLine;
        return trimmedLine[..spaceIdx];
    }
}
