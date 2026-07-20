using CommunityToolkit.Mvvm.ComponentModel;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Utils;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>项目管理服务实现</summary>
public partial class ProjectService : ObservableObject, IProjectService
{
    private const string ProjectFileExtension = ".lfengine";

    private List<RecentProject>? _recentProjectsCache;

    [ObservableProperty]
    private ProjectConfig? _currentProject;

    // 显式实现接口属性（映射到 ObservableProperty 生成的属性）
    ProjectConfig? IProjectService.CurrentProject
    {
        get => CurrentProject;
        set => CurrentProject = value;
    }

    /// <inheritdoc/>
    public async Task<ProjectConfig> CreateNewAsync(
        string name, string title, string author, string outputDir,
        string version = "1.0.0", string description = "")
    {
        var projectDir = Path.Combine(outputDir, name);
        PathHelper.EnsureDirectory(projectDir);

        var project = new ProjectConfig
        {
            Name = name,
            Title = title,
            Description = string.IsNullOrWhiteSpace(description) ? title : description,
            Author = author,
            Version = string.IsNullOrWhiteSpace(version) ? "1.0.0" : version,
            ProjectPath = Path.Combine(projectDir, name + ProjectFileExtension),
            TargetPlatforms = [.. PlatformConfig.DesktopPlatforms],
            Encryption = new EncryptionConfig(),
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
        };

        await JsonHelper.SerializeToFileAsync(project, project.ProjectPath, SdkJsonContext.Default.ProjectConfig);

        AddRecentProject(project.ProjectPath, title);

        return project;
    }

    /// <inheritdoc/>
    public async Task<ProjectConfig?> LoadAsync(string projectFilePath)
    {
        if (!FileHelper.FileExists(projectFilePath))
            return null;

        var project = await JsonHelper.DeserializeFromFileAsync(projectFilePath, SdkJsonContext.Default.ProjectConfig);
        if (project != null)
        {
            project.ProjectPath = projectFilePath;
            AddRecentProject(projectFilePath, project.Title);
        }

        return project;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(ProjectConfig project)
    {
        project.UpdatedAt = DateTime.Now;
        await JsonHelper.SerializeToFileAsync(project, project.ProjectPath, SdkJsonContext.Default.ProjectConfig);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string projectFilePath)
    {
        if (FileHelper.FileExists(projectFilePath))
            File.Delete(projectFilePath);

        // 从最近列表移除
        var recents = GetRecentProjects();
        _recentProjectsCache = recents.Where(r => r.Path != projectFilePath).ToList();
        SaveRecentProjects();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RemoveRecentAsync(string projectFilePath)
    {
        var recents = GetRecentProjects();
        _recentProjectsCache = recents.Where(r => r.Path != projectFilePath).ToList();
        SaveRecentProjects();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public IReadOnlyList<RecentProject> GetRecentProjects()
    {
        if (_recentProjectsCache != null)
            return _recentProjectsCache;

        var recentFile = PathHelper.GetRecentProjectsFile();
        if (!FileHelper.FileExists(recentFile))
        {
            _recentProjectsCache = [];
            return _recentProjectsCache;
        }

        try
        {
            // 直接同步读取——小 JSON 文件，避免 sync-over-async 死锁
            // （File.ReadAllTextAsync 在 UI 线程 .GetAwaiter().GetResult() 会捕获
            //   DispatcherSynchronizationContext 导致经典死锁）
            var json = File.ReadAllText(recentFile);
            _recentProjectsCache = JsonHelper.Deserialize(json, SdkJsonContext.Default.ListRecentProject) ?? [];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProjectService] 最近项目缓存加载失败: {ex.Message}");
            _recentProjectsCache = [];
        }

        return _recentProjectsCache;
    }

    /// <inheritdoc/>
    public void AddRecentProject(string path, string name)
    {
        var recents = new List<RecentProject>(GetRecentProjects());

        // 移除已有的同路径记录
        recents.RemoveAll(r => r.Path == path);

        // 添加到最前
        recents.Insert(0, new RecentProject(path, name, DateTime.Now));

        // 限制最多 20 条
        if (recents.Count > 20)
            recents = recents.GetRange(0, 20);

        _recentProjectsCache = recents;
        SaveRecentProjects();
    }

    private void SaveRecentProjects()
    {
        if (_recentProjectsCache == null)
            return;

        var recentFile = PathHelper.GetRecentProjectsFile();
        var dir = Path.GetDirectoryName(recentFile);
        if (!string.IsNullOrEmpty(dir))
            PathHelper.EnsureDirectory(dir);

        // 直接同步写入——小 JSON 文件，避免 sync-over-async 死锁
        var json = JsonHelper.Serialize(_recentProjectsCache, SdkJsonContext.Default.ListRecentProject);
        File.WriteAllText(recentFile, json);
    }
}
