using System.Text;
using Avalonia.Platform;

namespace LingFanEngine.Services.Resources;

/// <summary>
/// 资源加载器——统一封装 Avalonia AssetLoader
/// <para>支持 avares:// 内嵌资源和文件系统回退。</para>
/// <para>当前引擎仍通过物理路径访问资源，此类作为迁移桥梁。</para>
/// </summary>
public static class ResourceLoader
{
    /// <summary>
    /// 将物理路径转换为 avares:// URI
    /// <para>约定：Assets/Stories/title/title_main.story → avares://LingFanEngine/Assets/Stories/title/title_main.story</para>
    /// </summary>
    /// <param name="physicalPath">物理路径，如 "Stories/title/title_main.story"</param>
    /// <returns>可用的 avares:// URI，如果无法映射则返回原始路径</returns>
    public static string ToAvaloniaUri(string physicalPath)
    {
        // 已经是 URI 格式
        if (physicalPath.StartsWith("avares://") || physicalPath.StartsWith("http"))
            return physicalPath;

        // 统一分隔符
        var normalized = physicalPath.Replace('\\', '/');

        // 去掉前面的路径前缀（如 "../../"、"Stories/"）
        while (normalized.StartsWith("../"))
            normalized = normalized[3..];
        if (normalized.StartsWith("Stories/", StringComparison.OrdinalIgnoreCase))
            normalized = "Assets/" + normalized;

        // 构造 avares:// URI
        return $"avares://LingFanEngine/{normalized}";
    }

    /// <summary>
    /// 尝试打开内嵌资源
    /// <para>优先从 AssetLoader 读取，失败时回退文件系统。</para>
    /// </summary>
    public static Stream? Open(string path)
    {
        try
        {
            var uri = ToAvaloniaUri(path);
            if (uri.StartsWith("avares://"))
            {
                // Avalonia 12: AssetLoader 通过 AvaloniaLocator 注册
                // 运行时 AssetLoader.Open 可用，但编译时可能找不到
                // 此处用文件系统回退，运行时可换成 avares://
                Stream? assetStream = null;
                try { assetStream = AssetLoader.Open(new Uri(uri)); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ResourceLoader] AssetLoader.Open failed for '{uri}': {ex.Message}"); }
                if (assetStream != null)
                    return assetStream;
            }
        }
        catch
        {
            // AssetLoader 失败，回落文件系统
        }

        // 文件系统回退
        var resolvedDir = ResolveStoryDirectory(path);
        if (File.Exists(resolvedDir))
            return File.OpenRead(resolvedDir);

        // 回退到相对路径
        if (File.Exists(path))
            return File.OpenRead(path);

        return null;
    }

    /// <summary>
    /// 读取文本资源
    /// </summary>
    public static string? ReadText(string path)
    {
        using var stream = Open(path);
        if (stream == null) return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 检查资源是否存在
    /// </summary>
    public static bool Exists(string path)
    {
        try
        {
            var uri = ToAvaloniaUri(path);
            if (uri.StartsWith("avares://"))
            {
                try { return AssetLoader.Exists(new Uri(uri)); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ResourceLoader] AssetLoader.Exists failed for '{uri}': {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ResourceLoader] ResourceExists fallback: {ex.Message}");
        }

        var resolvedDir = ResolveStoryDirectory(path);
        if (File.Exists(resolvedDir)) return true;
        return File.Exists(path);
    }

    private static string? ResolveStoryDirectory(string path)
    {
        // 尝试从项目根目录查找
        var baseDir = AppContext.BaseDirectory;
        var alt = Path.Combine(baseDir, "..", "..", "..", "..", path);
        if (File.Exists(alt)) return alt;

        // 尝试从输出目录查找
        alt = Path.Combine(baseDir, path);
        if (File.Exists(alt)) return alt;

        return null;
    }
}
