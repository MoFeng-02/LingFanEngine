using System;
using System.Collections.Generic;

namespace LingFanEngine.SDK.Models;

/// <summary>灵泛项目配置</summary>
public class ProjectConfig
{
    /// <summary>项目名称（C# 标识符，用于命名空间和目录名）</summary>
    public string Name { get; set; } = "";

    /// <summary>游戏名称（面向玩家的显示标题）</summary>
    public string Title { get; set; } = "";

    /// <summary>项目描述（用于 MSBuild Description 属性）</summary>
    public string Description { get; set; } = "";

    /// <summary>作者</summary>
    public string Author { get; set; } = "";

    /// <summary>版本号</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>.lfengine 项目文件路径</summary>
    public string ProjectPath { get; set; } = "";

    /// <summary>项目根目录（ProjectPath 所在目录）</summary>
    public string ProjectDirectory => string.IsNullOrEmpty(ProjectPath)
        ? ""
        : System.IO.Path.GetDirectoryName(ProjectPath) ?? "";

    /// <summary>目标平台列表</summary>
    public List<PlatformConfig> TargetPlatforms { get; set; } = new();

    /// <summary>加密配置</summary>
    public EncryptionConfig? Encryption { get; set; }

    /// <summary>构建配置</summary>
    public BuildConfig Build { get; set; } = new();

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>最后修改时间</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>最近项目记录</summary>
public record RecentProject(string Path, string Name, DateTime LastOpened);
