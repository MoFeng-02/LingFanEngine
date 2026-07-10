using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.Desktop.Services;

/// <summary>macOS 平台服务</summary>
public class MacOSPlatformService : IPlatformService
{
    /// <inheritdoc/>
    public string GetDefaultProjectDirectory()
    {
        return Path.Combine(System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.UserProfile), "LingFanProjects");
    }

    /// <inheritdoc/>
    public string GetAppDataDirectory()
    {
        return Path.Combine(System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "LingFan");
    }

    /// <inheritdoc/>
    public void OpenInFileExplorer(string path)
    {
        Process.Start("open", path);
    }

    /// <inheritdoc/>
    public void OpenInTerminal(string path)
    {
        Process.Start("open", $"-a Terminal \"{path}\"");
    }

    /// <inheritdoc/>
    public List<string> GetSystemFonts()
    {
        var fonts = new List<string>();
        var fontDirs = new[]
        {
            "/System/Library/Fonts",
            "/Library/Fonts",
            Path.Combine(System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.UserProfile), "Library", "Fonts"),
        };

        foreach (var dir in fontDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(name) && !fonts.Contains(name))
                    fonts.Add(name);
            }
        }

        return fonts;
    }
}
