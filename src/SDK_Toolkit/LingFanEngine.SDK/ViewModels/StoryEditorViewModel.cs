using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingFanEngine.DslCore;
using LingFanEngine.SDK.Dsl;
using LingFanEngine.SDK.Dsl.Analysis;
using LingFanEngine.SDK.Dsl.Highlight;
using LingFanEngine.SDK.Editor;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using MFToolkit.Routing.Core.Interfaces;

namespace LingFanEngine.SDK.ViewModels;

/// <summary>故事编辑器 ViewModel</summary>
public partial class StoryEditorViewModel : ViewModelBase, IQueryAttributable
{
    private readonly IDslAnalyzer _analyzer;
    private readonly IProjectSession _session;
    private CancellationTokenSource? _debounceCts;

    /// <summary>P1-7: 文件树需要刷新时触发（文件创建/删除/重命名后）</summary>
    public event Action? FileTreeNeedsRefresh;

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

    // P0-4: 引用查找结果
    [ObservableProperty]
    private ObservableCollection<ReferenceResult> _references = new();

    /// <summary>是否显示引用面板</summary>
    [ObservableProperty]
    private bool _showReferencesPanel;

    // P2-7: 文档大纲
    [ObservableProperty]
    private ObservableCollection<OutlineItem> _outlineItems = new();

    // P1-5: 多标签页
    [ObservableProperty]
    private ObservableCollection<OpenFileTab> _openFiles = new();

    /// <summary>P1-5: 当前活动标签页</summary>
    public OpenFileTab? ActiveTab { get; private set; }

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
        // 取消待执行的 debounce 分析，防止项目关闭后旧分析结果写入已清空的集合
        _debounceCts?.Cancel();

        StoriesDirectory = null;
        OpenFiles.Clear();
        ActiveTab = null;
        EditorContent = "";
        CurrentFilePath = "";
        CurrentFileName = "(未打开文件)";
        HasFileOpen = false;
        IsDirty = false;
        Diagnostics.Clear();
        Variables.Clear();
        References.Clear();
        OutlineItems.Clear();
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

        // P1-5: 同步更新活动标签页状态
        if (ActiveTab != null)
        {
            ActiveTab.Content = source;
            ActiveTab.IsDirty = true;
        }

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

    /// <summary>打开文件（P1-5: 支持多标签页）</summary>
    [RelayCommand]
    private async Task OpenFileAsync(string filePath)
    {
        if (!File.Exists(filePath)) return;

        // P1-5: 如果文件已打开，切换到对应标签
        var existing = FindTabByPath(filePath);
        if (existing != null)
        {
            SwitchToTab(existing);
            return;
        }

        try
        {
            var content = await File.ReadAllTextAsync(filePath);

            // P1-5: 创建新标签页
            var tab = new OpenFileTab
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                Content = content,
                IsDirty = false,
            };
            OpenFiles.Add(tab);

            SwitchToTab(tab);
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

            // P1-5: 同步标签页状态
            if (ActiveTab != null)
                ActiveTab.IsDirty = false;

            StatusMessage = $"已保存: {CurrentFileName}";

            // 保存后重新索引该文件
            await DefinitionIndexer.IndexFileAsync(CurrentFilePath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败: {ex.Message}";
        }
    }

    /// <summary>P2-12: 全部保存</summary>
    [RelayCommand]
    private async Task SaveAllAsync()
    {
        var dirtyTabs = OpenFiles.Where(t => t.IsDirty).ToList();
        if (dirtyTabs.Count == 0)
        {
            StatusMessage = "没有未保存的文件";
            return;
        }

        var savedCount = 0;
        foreach (var tab in dirtyTabs)
        {
            try
            {
                await File.WriteAllTextAsync(tab.FilePath, tab.Content);
                tab.IsDirty = false;
                savedCount++;
            }
            catch { }
        }

        // 如果当前活动标签也被保存了，同步状态
        if (ActiveTab != null && !ActiveTab.IsDirty)
            IsDirty = false;

        StatusMessage = $"已保存 {savedCount} 个文件";
    }

    /// <summary>P1-5: 新建 .story 文件（接收完整文件路径）</summary>
    [RelayCommand]
    private async Task CreateNewFileAsync(string filePath)
    {
        try
        {
            var template = "// 新建故事文件\nscene \"new_scene\" type=game\n";
            await File.WriteAllTextAsync(filePath, template);

            // 自动打开
            await OpenFileAsync(filePath);
            StatusMessage = $"已创建: {Path.GetFileName(filePath)}";

            // P1-7: 通知文件树刷新
            FileTreeNeedsRefresh?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"创建失败: {ex.Message}";
        }
    }

    /// <summary>P1-6: 删除文件</summary>
    [RelayCommand]
    private void DeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                StatusMessage = $"已删除: {Path.GetFileName(filePath)}";

                // P1-5: 如果文件在标签页中打开，关闭它
                var tab = FindTabByPath(filePath);
                if (tab != null)
                {
                    CloseTabInternal(tab);
                }

                // P1-7: 通知文件树刷新
                FileTreeNeedsRefresh?.Invoke();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除失败: {ex.Message}";
        }
    }

    /// <summary>重命名文件或文件夹</summary>
    [RelayCommand]
    private void RenameFile(string[] args)
    {
        // args[0] = oldPath, args[1] = newName
        if (args.Length < 2) return;
        var oldPath = args[0];
        var newName = args[1];

        try
        {
            var dir = Path.GetDirectoryName(oldPath);
            var newPath = Path.Combine(dir ?? "", newName);

            // 文件重命名
            if (File.Exists(oldPath))
            {
                File.Move(oldPath, newPath);
                StatusMessage = $"已重命名为: {newName}";

                // 更新标签页路径和名称
                var tab = FindTabByPath(oldPath);
                if (tab != null)
                {
                    tab.FilePath = newPath;
                    tab.FileName = newName;
                }

                if (oldPath == CurrentFilePath)
                {
                    CurrentFilePath = newPath;
                    CurrentFileName = newName;
                }

                FileTreeNeedsRefresh?.Invoke();
            }
            // 文件夹重命名
            else if (Directory.Exists(oldPath))
            {
                Directory.Move(oldPath, newPath);
                StatusMessage = $"已重命名文件夹为: {newName}";

                // 关闭该目录下所有已打开的标签页
                var tabsToClose = OpenFiles.Where(t =>
                    t.FilePath.StartsWith(oldPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var tab in tabsToClose)
                    CloseTabInternal(tab);

                FileTreeNeedsRefresh?.Invoke();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"重命名失败: {ex.Message}";
        }
    }

    /// <summary>手动触发分析</summary>
    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        await AnalyzeInternalAsync(EditorContent);
    }

    /// <summary>P2-2: 格式化当前文件</summary>
    [RelayCommand]
    private void FormatDocument()
    {
        if (string.IsNullOrEmpty(EditorContent)) return;

        var formatted = DslFormatter.Format(EditorContent);
        EditorContent = formatted;
        StatusMessage = "已格式化";
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
            OutlineItems.Clear();
            return;
        }

        try
        {
            // P1-1: 使用增强分析（含跨文件引用检测）
            var result = await _analyzer.AnalyzeWithIndexerAsync(source, CurrentFilePath, DefinitionIndexer);

            Diagnostics.Clear();
            foreach (var err in result.Errors)
                Diagnostics.Add(err with { Severity = DiagnosticSeverity.Error });
            foreach (var warn in result.Warnings)
                Diagnostics.Add(warn with { Severity = DiagnosticSeverity.Warning });
            foreach (var info in result.Infos)
                Diagnostics.Add(info with { Severity = DiagnosticSeverity.Info });

            Variables.Clear();
            foreach (var v in result.Variables)
                Variables.Add(v);

            // P2-7: 更新大纲
            UpdateOutline(result.Ast as List<DslStatement> ?? new List<DslStatement>());

            var errCount = result.Errors.Count;
            var warnCount = result.Warnings.Count;
            var infoCount = result.Infos.Count;
            DiagSummary = errCount == 0 && warnCount == 0
                ? (infoCount > 0 ? $"{infoCount} 个提示" : "无问题")
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

    /// <summary>P2-7: 更新文档大纲</summary>
    private void UpdateOutline(List<DslStatement> statements)
    {
        OutlineItems.Clear();
        foreach (var stmt in statements)
        {
            var item = stmt switch
            {
                SceneStmt s => new OutlineItem(s.SceneName, "场景", stmt.LineNumber + 1, "scene"),
                LabelStmt l => new OutlineItem(l.Name, "标签", stmt.LineNumber + 1, "label"),
                CharacterStmt c => new OutlineItem(c.Key, "角色", stmt.LineNumber + 1, "character"),
                StyleStmt st => new OutlineItem(st.Name, "样式", stmt.LineNumber + 1, "style"),
                SetStmt set => new OutlineItem(set.Key, "变量", stmt.LineNumber + 1, "set"),
                DefineStmt def => new OutlineItem(def.Key, "变量", stmt.LineNumber + 1, "define"),
                FuncStmt func => new OutlineItem(func.Name, "函数", stmt.LineNumber + 1, "func"),
                _ => null,
            };
            if (item != null)
                OutlineItems.Add(item);
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

    /// <summary>P0-3: Go to Definition——查找当前光标下的词并跳转</summary>
    public (string FilePath, int Line)? GoToDefinition(string word)
    {
        if (string.IsNullOrEmpty(word)) return null;

        // 按类型依次查找
        var def = DefinitionIndexer.FindDefinition(word);
        return def != null ? (def.FilePath, def.Line) : null;
    }

    /// <summary>P0-4: Find All References——查找符号的所有引用</summary>
    public async Task<List<ReferenceResult>> FindAllReferencesAsync(string word)
    {
        if (string.IsNullOrEmpty(word) || StoriesDirectory == null)
            return new List<ReferenceResult>();

        // 判断符号类型
        var kind = DetermineReferenceKind(word);

        var results = await ReferenceFinder.FindReferencesAsync(StoriesDirectory, word, kind);
        References.Clear();
        foreach (var r in results)
            References.Add(r);
        ShowReferencesPanel = results.Count > 0;
        return results;
    }

    /// <summary>根据定义索引判断引用类型</summary>
    private ReferenceKind DetermineReferenceKind(string word)
    {
        var def = DefinitionIndexer.FindDefinition(word);
        if (def != null)
        {
            return def.Type switch
            {
                DefinitionType.Scene => ReferenceKind.Scene,
                DefinitionType.Label => ReferenceKind.Label,
                DefinitionType.Character => ReferenceKind.Character,
                DefinitionType.Style => ReferenceKind.Style,
                DefinitionType.Variable => ReferenceKind.Variable,
                DefinitionType.Function => ReferenceKind.Function,
                DefinitionType.Sprite => ReferenceKind.Sprite,
                _ => ReferenceKind.Variable,
            };
        }
        // 默认当作变量
        return ReferenceKind.Variable;
    }

    /// <summary>P1-2: 获取 Hover 提示文本</summary>
    public string? GetHoverText(string word)
    {
        return DslHoverProvider.GetHoverText(word, DefinitionIndexer, new List<VariableInfo>(Variables));
    }

    // ===== P1-5: 多标签页管理 =====

    /// <summary>IQueryAttributable: 接收导航参数</summary>
    public void ApplyQueryAttributes(IDictionary<string, object?>? parameters)
    {
        if (parameters == null) return;
        if (parameters.TryGetValue("filePath", out var path) && path is string filePath)
        {
            _ = OpenFileCommand.ExecuteAsync(filePath);
        }
    }

    /// <summary>P1-5: 保存当前编辑器状态到活动标签页（切换前调用）</summary>
    public void SaveActiveTabState(int caretLine, int caretColumn, double scrollOffset)
    {
        if (ActiveTab != null)
        {
            ActiveTab.Content = EditorContent;
            ActiveTab.CaretLine = caretLine;
            ActiveTab.CaretColumn = caretColumn;
            ActiveTab.ScrollOffset = scrollOffset;
        }
    }

    /// <summary>P1-5: 切换到指定标签页</summary>
    public void SwitchToTab(OpenFileTab tab)
    {
        if (tab == null) return;

        // 取消未完成的 debounce 分析，防止旧标签的分析结果覆盖新标签的诊断
        _debounceCts?.Cancel();

        // 取消其他标签的活动状态
        foreach (var t in OpenFiles)
            t.IsActive = false;

        tab.IsActive = true;
        ActiveTab = tab;

        // 加载标签内容到编辑器
        CurrentFilePath = tab.FilePath;
        CurrentFileName = tab.FileName;
        EditorContent = tab.Content;
        IsDirty = tab.IsDirty;
        HasFileOpen = true;

        StatusMessage = $"已切换到: {tab.FileName}";
    }

    /// <summary>P1-5: 关闭标签页</summary>
    [RelayCommand]
    public void CloseTab(OpenFileTab tab)
    {
        if (tab == null) return;
        CloseTabInternal(tab);
    }

    private void CloseTabInternal(OpenFileTab tab)
    {
        // 取消未完成的 debounce 分析
        _debounceCts?.Cancel();

        var index = OpenFiles.IndexOf(tab);
        OpenFiles.Remove(tab);

        if (tab.IsActive)
        {
            // 切换到相邻标签
            if (OpenFiles.Count > 0)
            {
                var newIndex = Math.Min(index, OpenFiles.Count - 1);
                SwitchToTab(OpenFiles[newIndex]);
                _ = AnalyzeInternalAsync(EditorContent);
            }
            else
            {
                // 没有标签了
                ActiveTab = null;
                EditorContent = "";
                CurrentFilePath = "";
                CurrentFileName = "(未打开文件)";
                HasFileOpen = false;
                IsDirty = false;
                Diagnostics.Clear();
                Variables.Clear();
                OutlineItems.Clear();
                DiagSummary = "";
                StatusMessage = "就绪";
            }
        }
    }

    /// <summary>P1-5: 获取活动标签页的编辑器恢复状态</summary>
    public (int Line, int Column, double ScrollOffset) GetTabRestoreState()
    {
        if (ActiveTab == null) return (1, 1, 0);
        return (ActiveTab.CaretLine, ActiveTab.CaretColumn, ActiveTab.ScrollOffset);
    }

    /// <summary>P1-5: 按路径查找标签页</summary>
    private OpenFileTab? FindTabByPath(string filePath)
    {
        foreach (var tab in OpenFiles)
        {
            if (string.Equals(tab.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                return tab;
        }
        return null;
    }
}

/// <summary>P2-7: 文档大纲项</summary>
public record OutlineItem(string Name, string Type, int Line, string Keyword);
