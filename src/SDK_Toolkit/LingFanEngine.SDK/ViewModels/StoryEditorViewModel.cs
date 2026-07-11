using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingFanEngine.SDK.Dsl.Highlight;
using LingFanEngine.SDK.Editor;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.ViewModels;

/// <summary>故事编辑器 ViewModel</summary>
public partial class StoryEditorViewModel : ViewModelBase
{
    private readonly IDslAnalyzer _analyzer;
    private readonly IProjectSession _session;
    private CancellationTokenSource? _debounceCts;

    /// <summary>跨文件定义索引器</summary>
    public DslDefinitionIndexer DefinitionIndexer { get; } = new();

    [ObservableProperty]
    private string _editorContent = "";

    [ObservableProperty]
    private string _currentFilePath = "";

    [ObservableProperty]
    private string _currentFileName = "(未打开文件)";

    [ObservableProperty]
    private ObservableCollection<DslDiagnostic> _diagnostics = new();

    [ObservableProperty]
    private ObservableCollection<VariableInfo> _variables = new();

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private string _caretInfo = "Ln 1, Col 1";

    [ObservableProperty]
    private string _diagSummary = "";

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _hasFileOpen;

    /// <summary>项目 Stories 目录路径</summary>
    [ObservableProperty]
    private string? _storiesDirectory;

    public StoryEditorViewModel(IDslAnalyzer analyzer, IProjectSession session)
    {
        _analyzer = analyzer;
        _session = session;

        // 监听项目会话
        _session.ProjectOpened += OnProjectOpened;
        _session.ProjectClosed += OnProjectClosed;

        // 初始检查
        if (_session.IsProjectOpen)
        {
            OnProjectOpened();
        }
    }

    private void OnProjectOpened()
    {
        var storiesDir = _session.StoriesDirectory;
        if (Directory.Exists(storiesDir))
        {
            StoriesDirectory = storiesDir;
            _ = DefinitionIndexer.ReindexAsync(storiesDir);
        }
    }

    private void OnProjectClosed()
    {
        StoriesDirectory = null;
        EditorContent = "";
        CurrentFilePath = "";
        CurrentFileName = "(未打开文件)";
        HasFileOpen = false;
        Diagnostics.Clear();
        Variables.Clear();
    }

    /// <summary>Stories 目录变化时重建索引</summary>
    partial void OnStoriesDirectoryChanged(string? oldValue, string? newValue)
    {
        if (newValue != null && Directory.Exists(newValue))
        {
            _ = DefinitionIndexer.ReindexAsync(newValue);
        }
    }

    /// <summary>编辑器文本变更时调用（debounce 后自动分析）</summary>
    public async Task OnSourceChangedAsync(string source)
    {
        EditorContent = source;
        IsDirty = true;

        // debounce 300ms
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, _debounceCts.Token);
            await AnalyzeInternalAsync(source);
        }
        catch (TaskCanceledException)
        {
            // debounce 被取消——正常行为
        }
    }

    /// <summary>打开文件</summary>
    [RelayCommand]
    private async Task OpenFileAsync(string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            CurrentFilePath = filePath;
            CurrentFileName = Path.GetFileName(filePath);
            EditorContent = content;
            IsDirty = false;
            HasFileOpen = true;
            StatusMessage = $"已打开: {Path.GetFileName(filePath)}";

            await AnalyzeInternalAsync(content);
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开失败: {ex.Message}";
        }
    }

    /// <summary>保存当前文件</summary>
    [RelayCommand]
    private async Task SaveFileAsync()
    {
        if (!HasFileOpen || string.IsNullOrEmpty(CurrentFilePath)) return;

        try
        {
            await File.WriteAllTextAsync(CurrentFilePath, EditorContent);
            IsDirty = false;
            StatusMessage = $"已保存: {CurrentFileName}";

            // 保存后重新索引该文件
            await DefinitionIndexer.IndexFileAsync(CurrentFilePath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败: {ex.Message}";
        }
    }

    /// <summary>新建 .story 文件</summary>
    [RelayCommand]
    private async Task CreateNewFileAsync(string directoryPath)
    {
        var fileName = $"new_story_{DateTime.Now:HHmmss}.story";
        var filePath = Path.Combine(directoryPath, fileName);

        try
        {
            var template = "// 新建故事文件\nscene \"new_scene\" type=game\n";
            await File.WriteAllTextAsync(filePath, template);

            // 自动打开
            await OpenFileAsync(filePath);
            StatusMessage = $"已创建: {fileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"创建失败: {ex.Message}";
        }
    }

    /// <summary>手动触发分析（兼容旧 UI）</summary>
    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        await AnalyzeInternalAsync(EditorContent);
    }

    /// <summary>获取高亮 token</summary>
    public List<HighlightToken> GetHighlights()
    {
        return string.IsNullOrEmpty(EditorContent)
            ? []
            : _analyzer.GetHighlights(EditorContent);
    }

    private async Task AnalyzeInternalAsync(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            Diagnostics.Clear();
            Variables.Clear();
            DiagSummary = "";
            StatusMessage = "就绪";
            return;
        }

        try
        {
            var result = await _analyzer.AnalyzeAsync(source, CurrentFilePath);

            Diagnostics.Clear();
            foreach (var err in result.Errors)
                Diagnostics.Add(err with { Severity = DiagnosticSeverity.Error });
            foreach (var warn in result.Warnings)
                Diagnostics.Add(warn with { Severity = DiagnosticSeverity.Warning });

            Variables.Clear();
            foreach (var v in result.Variables)
                Variables.Add(v);

            var errCount = result.Errors.Count;
            var warnCount = result.Warnings.Count;
            DiagSummary = errCount == 0 && warnCount == 0
                ? "无问题"
                : $"{errCount} 个错误, {warnCount} 个警告";

            StatusMessage = result.Success
                ? $"分析完成（{result.Elapsed.TotalMilliseconds:F0}ms）— 无错误"
                : $"分析完成 — {errCount} 个错误";
        }
        catch (Exception ex)
        {
            StatusMessage = $"分析失败: {ex.Message}";
        }
    }

    /// <summary>更新光标信息</summary>
    public void UpdateCaretInfo(int line, int column)
    {
        CaretInfo = $"Ln {line}, Col {column}";
    }

    /// <summary>获取补全数据源快照</summary>
    public (List<VariableInfo> Variables, List<string> Scenes, List<string> Labels, List<string> Characters)
        GetCompletionData()
    {
        return (
            new List<VariableInfo>(Variables),
            DefinitionIndexer.SceneNames,
            DefinitionIndexer.LabelNames,
            DefinitionIndexer.CharacterKeys);
    }

    /// <summary>Go to Definition——查找当前光标下的词并跳转</summary>
    public (string FilePath, int Line)? GoToDefinition(string word)
    {
        if (string.IsNullOrEmpty(word)) return null;

        var def = DefinitionIndexer.FindDefinition(word);
        return def != null ? (def.FilePath, def.Line) : null;
    }
}
