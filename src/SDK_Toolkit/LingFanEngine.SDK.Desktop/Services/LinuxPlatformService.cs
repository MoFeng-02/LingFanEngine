using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.Desktop.Services;

/// <summary>Linux 平台服务</summary>
public class LinuxPlatformService : IPlatformService
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
        var xdg = System.Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrEmpty(xdg))
            return Path.Combine(xdg, "LingFan");
        return Path.Combine(System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.UserProfile), ".local", "share", "LingFan");
    }

    /// <inheritdoc/>
    public void OpenInFileExplorer(string path)
    {
        Process.Start("xdg-open", path);
    }

    /// <inheritdoc/>
    public void OpenInTerminal(string path)
    {
        // 尝试常见终端
        var terminals = new[] { "gnome-terminal", "konsole", "xfce4-terminal", "xterm" };
        foreach (var term in terminals)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = term,
                    Arguments = $"--working-directory=\"{path}\"",
                    UseShellExecute = false,
                });
                return;
            }
            catch { /* 继续尝试下一个 */ }
        }
    }

    /// <inheritdoc/>
    public List<string> GetSystemFonts()
    {
        var fonts = new List<string>();
        var fontDirs = new[]
        {
            "/usr/share/fonts",
            Path.Combine(System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.UserProfile), ".local", "share", "fonts"),
        };

        foreach (var dir in fontDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(name) && !fonts.Contains(name))
                    fonts.Add(name);
            }
        }

        return fonts;
    }
}
