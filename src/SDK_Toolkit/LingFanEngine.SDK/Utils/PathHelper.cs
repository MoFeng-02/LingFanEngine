using System;
using System.IO;
using LingFanEngine.SDK.Constants;

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

    /// <summary>
    /// 获取 SDK 设置文件路径。
    /// <para>与 SettingsViewModel 使用的路径一致：%LOCALAPPDATA%\LingFanEngine\sdk_settings.json。</para>
    /// </summary>
    public static string GetSdkSettingsFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LingFanEngine", "sdk_settings.json");
    }

    /// <summary>
    /// 获取引擎更新工作目录（下载缓存/解压/pending 均在此下）。
    /// <para>与 SDK 设置同级：%LOCALAPPDATA%\LingFanEngine\updates。</para>
    /// </summary>
    public static string GetEngineUpdatesDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LingFanEngine", "updates");
    }

    /// <summary>
    /// 获取 SDK 引擎缓存目录（离线建项目/预览引擎的 DLL 种子源）。
    /// <para>%LOCALAPPDATA%\LingFanEngine\engine-cache。该目录由 SDK 安装包自带全部 4 个 DLL 填充，
    /// 因此离线/首次建项目时已是最新 4 个齐全状态，不存在「只 3 个」的降级形态。</para>
    /// </summary>
    public static string GetEngineCacheDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LingFanEngine", ProjectConstants.EngineCacheDir);
    }

    /// <summary>
    /// 获取模板更新缓存目录（从 Release 下载的模板 zip 解压落盘处）。
    /// <para>%LOCALAPPDATA%\LingFanEngine\template-cache。仅作为「覆盖内置嵌入模板」的源：
    /// 分发模式下若缓存版本高于内置，则建项目用缓存；否则用内置嵌入 zip。</para>
    /// </summary>
    public static string GetTemplateCacheDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LingFanEngine", TemplateDefaults.TemplateCacheDir);
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
