using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>
/// 项目会话实现——封装 IProjectService，提供统一的项目生命周期管理。
/// </summary>
public partial class ProjectSession : ObservableObject, IProjectSession
{
    private readonly IProjectService _projectService;
    private readonly ITemplateService? _templateService;

    [ObservableProperty]
    private ProjectConfig? _currentProject;

    [ObservableProperty]
    private bool _isProjectOpen;

    public string ProjectDirectory => CurrentProject?.ProjectDirectory ?? "";
    public string StoriesDirectory => Path.Combine(ProjectDirectory, "Stories");
    public string MediaDirectory => Path.Combine(ProjectDirectory, "Media");

    /// <inheritdoc/>
    public event Action? ProjectOpened;

    /// <inheritdoc/>
    public event Action? ProjectClosed;

    public ProjectSession(IProjectService projectService, ITemplateService? templateService = null)
    {
        _projectService = projectService;
        _templateService = templateService;
    }

    /// <inheritdoc/>
    public async Task<bool> OpenAsync(string projectFilePath)
    {
        var project = await _projectService.LoadAsync(projectFilePath);
        if (project == null)
            return false;

        CurrentProject = project;
        IsProjectOpen = true;
        _projectService.CurrentProject = project;
        ProjectOpened?.Invoke();
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> CreateAndOpenAsync(string name, string title, string author, string outputDir)
    {
        // P2-1: 先从模板创建项目文件结构
        if (_templateService != null)
        {
            await _templateService.CreateProjectFromTemplateAsync(outputDir, name, title);
        }

        var project = await _projectService.CreateNewAsync(name, title, author, outputDir);
        if (project == null)
            return false;

        // 确保引擎所需目录存在
        EnsureProjectDirectories(project.ProjectDirectory);

        CurrentProject = project;
        IsProjectOpen = true;
        _projectService.CurrentProject = project;
        ProjectOpened?.Invoke();
        return true;
    }

    /// <inheritdoc/>
    public void Close()
    {
        if (!IsProjectOpen)
            return;

        CurrentProject = null;
        IsProjectOpen = false;
        _projectService.CurrentProject = null;
        ProjectClosed?.Invoke();
    }

    /// <summary>确保引擎所需的标准目录存在</summary>
    private static void EnsureProjectDirectories(string projectDir)
    {
        var dirs = new[] { "Stories", "Media", "Media/BGM", "Media/SE", "Media/Voice", "Media/Images", "Media/Video" };
        foreach (var dir in dirs)
        {
            var path = Path.Combine(projectDir, dir);
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }
    }
}
