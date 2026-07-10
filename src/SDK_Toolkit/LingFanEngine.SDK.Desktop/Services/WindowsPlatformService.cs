using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.Desktop.Services;

/// <summary>Windows 平台服务</summary>
public class WindowsPlatformService : IPlatformService
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
            System.Environment.SpecialFolder.ApplicationData), "LingFan");
    }

    /// <inheritdoc/>
    public void OpenInFileExplorer(string path)
    {
        if (Directory.Exists(path))
            Process.Start("explorer.exe", path);
        else if (File.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    /// <inheritdoc/>
    public void OpenInTerminal(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            WorkingDirectory = path,
            UseShellExecute = true,
        });
    }

    /// <inheritdoc/>
    public List<string> GetSystemFonts()
    {
        var fonts = new List<string>();
        var fontsDir = @"C:\Windows\Fonts";
        if (Directory.Exists(fontsDir))
        {
            foreach (var file in Directory.GetFiles(fontsDir))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(name) && !fonts.Contains(name))
                    fonts.Add(name);
            }
        }
        return fonts;
    }
}
