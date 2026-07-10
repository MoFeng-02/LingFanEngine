using System;
using System.Collections.ObjectModel;
using System.IO;
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

/// <summary>项目管理 ViewModel（保留兼容旧 ProjectPage 路由）</summary>
public partial class ProjectViewModel : ViewModelBase
{
    private readonly IProjectService _projectService;
    private readonly IProjectSession _session;

    /// <summary>项目创建/打开成功时触发（供页面导航到编辑器）</summary>
    public event Action? ProjectOpened;

    [ObservableProperty]
    private ObservableCollection<RecentProject> _recentProjects = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateProjectCommand))]
    private string _newProjectName = "";

    [ObservableProperty]
    private string _newProjectTitle = "";

    [ObservableProperty]
    private string _newProjectAuthor = "";

    [ObservableProperty]
    private string _newProjectPath = "";

    [ObservableProperty]
    private ProjectConfig? _currentProject;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    public ProjectViewModel(IProjectService projectService, IProjectSession session)
    {
        _projectService = projectService;
        _session = session;
        LoadRecentProjects();
    }

    private void LoadRecentProjects()
    {
        RecentProjects.Clear();
        foreach (var recent in _projectService.GetRecentProjects())
        {
            RecentProjects.Add(recent);
        }
    }

    /// <summary>创建新项目</summary>
    [RelayCommand(CanExecute = nameof(CanCreateProject))]
    private async Task CreateProjectAsync()
    {
        try
        {
            StatusMessage = "正在创建项目...";
            var success = await _session.CreateAndOpenAsync(
                NewProjectName, NewProjectTitle, NewProjectAuthor,
                string.IsNullOrEmpty(NewProjectPath) ? Environment.CurrentDirectory : NewProjectPath);

            if (success)
            {
                CurrentProject = _session.CurrentProject;
                LoadRecentProjects();
                StatusMessage = $"项目 {CurrentProject?.Title} 创建成功！";
                ProjectOpened?.Invoke();
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
    }

    private bool CanCreateProject => !string.IsNullOrWhiteSpace(NewProjectName);

    /// <summary>通过文件路径打开项目</summary>
    [RelayCommand]
    private async Task OpenProjectAsync(string projectFilePath)
    {
        try
        {
            StatusMessage = "正在加载项目...";
            var success = await _session.OpenAsync(projectFilePath);
            if (success)
            {
                CurrentProject = _session.CurrentProject;
                LoadRecentProjects();
                StatusMessage = $"项目 {CurrentProject?.Title} 加载成功！";
                ProjectOpened?.Invoke();
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
    }

    /// <summary>弹出文件选择对话框选择 .lfengine 项目文件</summary>
    [RelayCommand]
    private async Task OpenProjectFileDialogAsync()
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
