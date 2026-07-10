using System;
using System.ComponentModel;
using System.Threading.Tasks;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Services.Abstractions;

/// <summary>
/// 项目会话——统一的项目上下文，所有 ViewModel 通过此接口感知项目开关。
/// </summary>
public interface IProjectSession : INotifyPropertyChanged
{
    /// <summary>当前打开的项目配置（null = 未打开）</summary>
    ProjectConfig? CurrentProject { get; }

    /// <summary>项目根目录（未打开时为空字符串）</summary>
    string ProjectDirectory { get; }

    /// <summary>Stories 目录路径</summary>
    string StoriesDirectory { get; }

    /// <summary>Media 目录路径</summary>
    string MediaDirectory { get; }

    /// <summary>是否已打开项目</summary>
    bool IsProjectOpen { get; }

    /// <summary>项目打开事件</summary>
    event Action? ProjectOpened;

    /// <summary>项目关闭事件</summary>
    event Action? ProjectClosed;

    /// <summary>打开项目</summary>
    /// <returns>成功返回 true，失败返回 false</returns>
    Task<bool> OpenAsync(string projectFilePath);

    /// <summary>创建并打开新项目</summary>
    Task<bool> CreateAndOpenAsync(string name, string title, string author, string outputDir);

    /// <summary>关闭当前项目</summary>
    void Close();
}
