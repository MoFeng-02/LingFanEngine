using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace LingFanEngine.SDK.Utils;

/// <summary>调用外部进程（dotnet 命令）</summary>
public static class ProcessHelper
{
    /// <summary>异步执行 dotnet 命令，实时输出</summary>
    public static async Task<int> RunDotNetAsync(
        string args,
        string? workingDir = null,
        IProgress<string>? output = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // 强制 dotnet CLI 输出英文，避免中文 Windows 乱码
        psi.EnvironmentVariables["DOTNET_CLI_UI_LANGUAGE"] = "en";

        if (!string.IsNullOrEmpty(workingDir))
            psi.WorkingDirectory = workingDir;

        using var process = new Process { StartInfo = psi };
        var lines = new List<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lines.Add(e.Data);
                output?.Report(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lines.Add($"[ERROR] {e.Data}");
                output?.Report($"[ERROR] {e.Data}");
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        return process.ExitCode;
    }

    /// <summary>检查 dotnet 是否安装</summary>
    public static bool CheckDotNetInstalled()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProcessHelper] 进程执行失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>获取 dotnet 版本</summary>
    public static string? GetDotNetVersion()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process == null) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output.Trim();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProcessHelper] 进程输出读取失败: {ex.Message}");
            return null;
        }
    }
}
