using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using LingFanEngine.SDK.Constants;
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

    /// <summary>
    /// 核心项目目录（ProjectDirectory/项目名）。
    /// <para>包含 .csproj 和 C# 源码（App.cs/Views/UI/Extensions 等）。</para>
    /// <para>共享资源（Stories/Media 等）不在此时录内，而在 ProjectDirectory/Resources/。</para>
    /// </summary>
    public string CoreProjectDirectory => CurrentProject != null
        ? Path.Combine(ProjectDirectory, CurrentProject.Name)
        : "";

    /// <summary>
    /// 共享资源目录（ProjectDirectory/Resources）。
    /// <para>所有资源（Stories/Media/Images/Audio/Video/Lang/Live2D）集中在此目录下。</para>
    /// <para>SDK 编辑器和引擎运行时均通过此目录定位资源。</para>
    /// </summary>
    public string ResourcesDirectory => Path.Combine(ProjectDirectory, ProjectConstants.ResourcesDir);

    public string StoriesDirectory => Path.Combine(ResourcesDirectory, ProjectConstants.StoriesDir);
    public string MediaDirectory => Path.Combine(ResourcesDirectory, ProjectConstants.MediaDir);

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

        // 确保引擎所需目录存在（在 Resources 目录内）
        EnsureProjectDirectories(project.ProjectDirectory);

        CurrentProject = project;
        IsProjectOpen = true;
        _projectService.CurrentProject = project;
        ProjectOpened?.Invoke();
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> CreateAndOpenAsync(
        string name, string title, string author, string outputDir,
        string version = "1.0.0", string description = "")
    {
        // 从模板创建项目文件结构（含版本/作者/描述替换）
        if (_templateService != null)
        {
            await _templateService.CreateProjectFromTemplateAsync(outputDir, name, title, version, author, description);
        }

        var project = await _projectService.CreateNewAsync(name, title, author, outputDir, version, description);
        if (project == null)
            return false;

        // 确保引擎所需目录存在（在 Resources 目录内）
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

    /// <inheritdoc/>
    public async Task SaveCurrentProjectAsync()
    {
        if (CurrentProject == null) return;
        await _projectService.SaveAsync(CurrentProject);
    }

    /// <summary>确保引擎所需的标准目录存在（在 Resources 目录内）</summary>
    private static void EnsureProjectDirectories(string projectDir)
    {
        var resourcesDir = Path.Combine(projectDir, ProjectConstants.ResourcesDir);
        if (!Directory.Exists(resourcesDir))
            Directory.CreateDirectory(resourcesDir);

        foreach (var dir in ProjectConstants.StandardSubDirs)
        {
            var path = Path.Combine(resourcesDir, dir);
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }
    }
}
