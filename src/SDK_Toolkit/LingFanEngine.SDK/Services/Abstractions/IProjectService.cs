using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Services.Abstractions;

/// <summary>项目管理服务</summary>
public interface IProjectService : INotifyPropertyChanged
{
    /// <summary>创建新项目</summary>
    Task<ProjectConfig> CreateNewAsync(
        string name, string title, string author, string outputDir,
        string version = "1.0.0", string description = "");

    /// <summary>加载项目</summary>
    Task<ProjectConfig?> LoadAsync(string projectFilePath);

    /// <summary>保存项目</summary>
    Task SaveAsync(ProjectConfig project);

    /// <summary>删除项目（含项目文件）</summary>
    Task DeleteAsync(string projectFilePath);

    /// <summary>仅从最近列表移除记录（不删除项目文件）</summary>
    Task RemoveRecentAsync(string projectFilePath);

    /// <summary>获取最近项目列表</summary>
    IReadOnlyList<RecentProject> GetRecentProjects();

    /// <summary>添加最近项目</summary>
    void AddRecentProject(string path, string name);

    /// <summary>当前打开的项目</summary>
    ProjectConfig? CurrentProject { get; set; }
}
