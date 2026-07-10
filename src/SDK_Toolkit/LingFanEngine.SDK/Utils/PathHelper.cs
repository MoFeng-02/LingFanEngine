using System;
using System.IO;

namespace LingFanEngine.SDK.Utils;

/// <summary>跨平台路径操作工具</summary>
public static class PathHelper
{
    /// <summary>统一路径分隔符为 /</summary>
    public static string NormalizePath(string path) => path.Replace('\\', '/');

    /// <summary>获取相对路径</summary>
    public static string GetRelativePath(string basePath, string fullPath)
    {
        return Path.GetRelativePath(basePath, fullPath).Replace('\\', '/');
    }

    /// <summary>从 .lfengine 文件路径推导项目根目录</summary>
    public static string GetProjectDirectory(string projectFilePath)
    {
        return Path.GetDirectoryName(projectFilePath) ?? "";
    }

    /// <summary>获取 SDK 应用数据目录</summary>
    public static string GetAppDataDirectory()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LingFan");
        if (OperatingSystem.IsMacOS())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "LingFan");
        // Linux + others (XDG)
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrEmpty(xdg))
            return Path.Combine(xdg, "LingFan");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "LingFan");
    }

    /// <summary>获取密钥存储目录</summary>
    public static string GetKeysDirectory()
    {
        return Path.Combine(GetAppDataDirectory(), "keys");
    }

    /// <summary>获取最近项目文件路径</summary>
    public static string GetRecentProjectsFile()
    {
        return Path.Combine(GetAppDataDirectory(), "recent.json");
    }

    /// <summary>获取模板路径（SDK 内置模板目录）</summary>
    public static string? GetTemplatePath()
    {
        // 模板在 SDK 项目相对路径中
        var baseDir = AppContext.BaseDirectory;
        // 尝试向上查找 Template/V1
        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            var templatePath = Path.Combine(dir.FullName, "src", "Template", "V1");
            if (Directory.Exists(templatePath))
                return templatePath;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>确保目录存在</summary>
    public static void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }
}
