using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace LingFanEngine.SDK.Utils;

/// <summary>
/// 引擎 NuGet 包版本读取（2026-09 起引擎经 NuGet 分发）。
/// <para>版本真相在用户项目 csproj 的 PackageReference（LingFanEngine）——
/// 替代旧 engine.lock.json / DLL 程序集元数据读取（DLL 模式已退役）。</para>
/// <para>AOT 安全：[GeneratedRegex] 源生成，零反射。</para>
/// </summary>
public static partial class EnginePackageHelper
{
    [GeneratedRegex(
        @"<PackageReference\s+Include=""LingFanEngine""\s+Version=""([^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EnginePackageRegex();

    /// <summary>
    /// 读项目内 LingFanEngine PackageReference 的版本号；未找到/读取失败返回 null。
    /// </summary>
    /// <param name="projectRootDir">用户项目根目录（递归查找 *.csproj）。</param>
    public static string? GetEnginePackageVersion(string projectRootDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(projectRootDir) || !Directory.Exists(projectRootDir))
                return null;

            foreach (var file in Directory.GetFiles(projectRootDir, "*.csproj", SearchOption.AllDirectories))
            {
                var xml = File.ReadAllText(file);
                var match = EnginePackageRegex().Match(xml);
                if (match.Success)
                    return match.Groups[1].Value;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EnginePackageHelper] 读取失败: {ex.Message}");
        }
        return null;
    }
}
