using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.ViewModels;

/// <summary>
/// 启动器 ViewModel——管理最近项目列表、新建项目、打开项目。
/// </summary>
public partial class LauncherViewModel : ViewModelBase
{
    private readonly IProjectService _projectService;
    private readonly IProjectSession _session;

    /// <summary>项目成功打开/创建后触发（供 App 切换窗口）</summary>
    public event Action? ProjectEntered;

    [ObservableProperty]
    private ObservableCollection<RecentProject> _recentProjects = new();

    [ObservableProperty]
    private string _newProjectName = "";

    [ObservableProperty]
    private string _newProjectTitle = "";

    [ObservableProperty]
    private string _newProjectAuthor = "";

    [ObservableProperty]
    private string _newProjectVersion = "1.0.0";

    [ObservableProperty]
    private string _newProjectDescription = "";

    [ObservableProperty]
    private string _newProjectPath = "";

    [ObservableProperty]
    private string _statusMessage = "选择或创建一个项目开始";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateProjectCommand))]
    private bool _isBusy;

    /// <summary>新建项目面板是否展开</summary>
    [ObservableProperty]
    private bool _isNewProjectPanelVisible;

    public LauncherViewModel(IProjectService projectService, IProjectSession session)
    {
        _projectService = projectService;
        _session = session;
        LoadRecentProjects();
    }

    private void LoadRecentProjects()
    {
        RecentProjects.Clear();
        foreach (var recent in _projectService.GetRecentProjects())
            RecentProjects.Add(recent);
    }

    /// <summary>新建项目</summary>
    [RelayCommand(CanExecute = nameof(CanCreateProject))]
    private async Task CreateProjectAsync()
    {
        IsBusy = true;
        StatusMessage = "正在创建项目...";
        try
        {
            var outputDir = string.IsNullOrEmpty(NewProjectPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : NewProjectPath;

            var success = await _session.CreateAndOpenAsync(
                NewProjectName, NewProjectTitle, NewProjectAuthor, outputDir,
                NewProjectVersion, NewProjectDescription);
            if (success)
            {
                StatusMessage = $"项目 {NewProjectTitle} 创建成功！";
                LoadRecentProjects();
                ProjectEntered?.Invoke();
            }
            else
            {
                StatusMessage = "项目创建失败";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"创建失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCreateProject => !string.IsNullOrWhiteSpace(NewProjectName) && !IsBusy;

    /// <summary>打开指定路径的项目</summary>
    [RelayCommand]
    private async Task OpenProjectAsync(string? projectFilePath)
    {
        if (string.IsNullOrEmpty(projectFilePath))
            return;

        IsBusy = true;
        StatusMessage = "正在加载项目...";
        try
        {
            var success = await _session.OpenAsync(projectFilePath);
            if (success)
            {
                StatusMessage = $"项目 {_session.CurrentProject?.Title} 加载成功！";
                LoadRecentProjects();
                ProjectEntered?.Invoke();
            }
            else
            {
                StatusMessage = "无法加载项目文件";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>弹出文件选择对话框</summary>
    [RelayCommand]
    private async Task BrowseProjectAsync()
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            StatusMessage = "无法获取窗口";
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择灵泛引擎项目文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("灵泛引擎项目")
                {
                    Patterns = new[] { "*.lfengine" },
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = new[] { "*.*" },
                },
            },
        });

        if (files.Count > 0)
        {
            var path = files[0].Path.LocalPath;
            await OpenProjectCommand.ExecuteAsync(path);
        }
    }

    /// <summary>选择输出目录</summary>
    [RelayCommand]
    private async Task BrowseOutputDirAsync()
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择项目输出目录",
            AllowMultiple = false,
        });

        if (folders.Count > 0)
        {
            NewProjectPath = folders[0].Path.LocalPath;
        }
    }

    /// <summary>从最近列表移除（P1-10: 不删除项目文件，仅移除记录）</summary>
    [RelayCommand]
    private void RemoveRecent(string path)
    {
        _projectService.RemoveRecentAsync(path).FireAndForget();
        LoadRecentProjects();
        StatusMessage = "已移除记录";
    }

    /// <summary>切换新建项目面板可见性</summary>
    [RelayCommand]
    private void ToggleNewProjectPanel()
    {
        IsNewProjectPanelVisible = !IsNewProjectPanelVisible;
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow != null)
        {
            return desktop.MainWindow;
        }
        return null;
    }
}

/// <summary>火并忘记的 Task 扩展</summary>
internal static class TaskExtensions
{
    public static void FireAndForget(this Task task)
    {
        _ = task.ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                System.Diagnostics.Debug.WriteLine($"FireAndForget error: {t.Exception}");
            }
        }, TaskScheduler.Default);
    }
}
