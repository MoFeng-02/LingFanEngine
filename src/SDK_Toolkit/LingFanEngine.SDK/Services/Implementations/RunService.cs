using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LingFanEngine.SDK.Constants;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>
/// 游戏运行服务实现。
/// <para>启动：定位 <c>projectDir/publish/{RID}/</c> 下的可执行文件并运行；
/// 未构建时自动构建当前操作系统平台后再启动。</para>
/// <para>停止：杀掉从项目目录运行的游戏进程（与发布前杀进程一致，供手动中断）。</para>
/// </summary>
public class RunService : IRunService
{
    private readonly IPublishService _publishService;

    public RunService(IPublishService publishService)
    {
        _publishService = publishService;
    }

    /// <inheritdoc/>
    public async Task<RunResult> LaunchAsync(ProjectConfig project, IProgress<string>? progress = null)
    {
        var result = new RunResult();
        var projectDir = project.ProjectDirectory;

        if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
        {
            result.Message = "项目目录无效，无法启动游戏。";
            return result;
        }

        var publishRoot = Path.Combine(projectDir, project.Build.OutputPath);
        var osPrefix = GetOsFamilyPrefix();
        var currentPlatform = GetCurrentOsPlatform();

        // 1. 查找已构建的、匹配当前操作系统的发布子目录
        string? chosenDir = null;
        if (Directory.Exists(publishRoot))
        {
            var candidates = Directory.GetDirectories(publishRoot)
                .Where(d => Path.GetFileName(d).StartsWith(osPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            // 优先精确匹配当前 RID（如 win-x64），否则退而取同族第一个
            chosenDir = candidates.FirstOrDefault(d =>
                string.Equals(Path.GetFileName(d), currentPlatform.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase))
                ?? candidates.FirstOrDefault();
        }

        var exe = chosenDir != null ? FindGameExecutable(chosenDir) : null;

        // 2. 未构建 → 自动构建当前平台
        if (exe == null)
        {
            progress?.Report($"未找到已构建的游戏，开始构建 {currentPlatform.Name} 平台...");
            var build = await _publishService.BuildAsync(project, currentPlatform, progress);
            if (!build.Success)
            {
                result.Success = false;
                result.Message = $"构建失败，无法启动：{build.ErrorMessage}";
                return result;
            }

            chosenDir = build.OutputPath;
            exe = FindGameExecutable(chosenDir);
            if (exe == null)
            {
                result.Success = false;
                result.Message = $"构建成功但未找到可执行文件：{chosenDir}";
                return result;
            }
        }

        // 3. 启动进程（非 Windows 确保可执行位）
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                        UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
                catch { /* 无权限时尽力而为，apphost 通常已带 +x */ }
            }

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = chosenDir,
                UseShellExecute = false,
                CreateNoWindow = false,
            };
            Process.Start(psi);

            result.Success = true;
            result.Launched = true;
            result.ExecutablePath = exe;
            result.Message = $"已启动游戏：{Path.GetFileName(exe)}";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"启动失败：{ex.Message}";
        }

        return result;
    }

    /// <inheritdoc/>
    public Task StopAsync(ProjectConfig project)
    {
        var projectDir = project.ProjectDirectory;
        if (!string.IsNullOrEmpty(projectDir))
            KillRunningProcesses(projectDir);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 在发布目录中定位游戏主程序。
    /// <para>优先以 <c>*.deps.json</c> 的基名推导（Windows 追加 .exe，Linux/macOS 用无扩展名 apphost）；
    /// 兜底直接找 <c>*.exe</c>。</para>
    /// </summary>
    private static string? FindGameExecutable(string publishDir)
    {
        if (!Directory.Exists(publishDir)) return null;

        var deps = Directory.GetFiles(publishDir, "*.deps.json", SearchOption.TopDirectoryOnly);
        if (deps.Length > 0)
        {
            var baseName = Path.GetFileNameWithoutExtension(deps[0]);
            var winExe = Path.Combine(publishDir, baseName + ".exe");
            if (File.Exists(winExe)) return winExe;
            var elf = Path.Combine(publishDir, baseName);
            if (File.Exists(elf)) return elf;
        }

        // 兜底：Windows 直接找 exe
        var exes = Directory.GetFiles(publishDir, "*.exe", SearchOption.TopDirectoryOnly);
        return exes.Length > 0 ? exes[0] : null;
    }

    /// <summary>当前操作系统对应的平台前缀（win/linux/osx）</summary>
    private static string GetOsFamilyPrefix() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "win";

    /// <summary>当前操作系统对应的目标平台配置（macOS 按实际架构判定 arm64/x64，避免硬编码 osx-arm64）</summary>
    private static PlatformConfig GetCurrentOsPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return PlatformConfig.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return PlatformConfig.Linux;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // 不再写死 osx-arm64：Intel Mac 应构建/启动 osx-x64，Apple Silicon 用 osx-arm64
            var rid = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
            return new PlatformConfig
            {
                Name = "macOS",
                RuntimeIdentifier = rid,
                SupportsAot = true,
                OutputFormat = "app",
            };
        }
        return PlatformConfig.Windows;
    }

    /// <summary>杀掉从项目目录运行的游戏进程（防止 DLL/PDB 文件锁）</summary>
    private static void KillRunningProcesses(string projectDir)
    {
        try
        {
            var normalizedDir = Path.GetFullPath(projectDir).TrimEnd(Path.DirectorySeparatorChar);
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.Id == Environment.ProcessId) continue;
                    var path = proc.MainModule?.FileName;
                    if (path != null && path.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase))
                    {
                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(3000);
                    }
                }
                catch { /* 忽略无权限访问的进程 */ }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[RunService] 停止游戏进程失败: {ex.Message}"); }
    }
}
