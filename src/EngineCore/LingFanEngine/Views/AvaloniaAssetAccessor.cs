using System.Text;
using Avalonia.Platform;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Views;

/// <summary>
/// Avalonia 资源访问器实现——封装 AssetLoader + 文件系统回退。
/// <para>优先从 Avalonia 内嵌资源（avares://）读取，失败时回退到文件系统。</para>
/// <para>Phase 50：文件系统回退时通过 IEncryptedFileReader 自动检测 LFEN 加密并解密。</para>
/// </summary>
public class AvaloniaAssetAccessor : IAssetAccessor
{
    private readonly IEncryptedFileReader? _fileReader;

    public AvaloniaAssetAccessor(IEncryptedFileReader? fileReader = null)
    {
        _fileReader = fileReader;
    }
    /// <summary>
    /// 将物理路径转换为 avares:// URI
    /// <para>约定：Assets/Stories/title/title_main.story → avares://LingFanEngine/Assets/Stories/title/title_main.story</para>
    /// </summary>
    public static string ToAvaloniaUri(string physicalPath)
    {
        if (physicalPath.StartsWith("avares://") || physicalPath.StartsWith("http"))
            return physicalPath;

        var normalized = physicalPath.Replace('\\', '/');

        while (normalized.StartsWith("../"))
            normalized = normalized[3..];
        if (normalized.StartsWith("Stories/", StringComparison.OrdinalIgnoreCase))
            normalized = "Assets/" + normalized;

        return $"avares://LingFanEngine/{normalized}";
    }

    public Stream? Open(string path)
    {
        try
        {
            var uri = ToAvaloniaUri(path);
            if (uri.StartsWith("avares://"))
            {
                Stream? assetStream = null;
                try { assetStream = AssetLoader.Open(new Uri(uri)); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AvaloniaAssetAccessor] AssetLoader.Open failed for '{uri}': {ex.Message}"); }
                if (assetStream != null)
                    return assetStream;
            }
        }
        catch
        {
            // AssetLoader 失败，回落文件系统
        }

        var resolvedDir = ResolveStoryDirectory(path);
        var physicalPath = resolvedDir ?? (File.Exists(path) ? path : null);
        if (physicalPath != null)
        {
            // Phase 50：即解即用——EncryptedFileReader 检测 LFEN 魔数，加密则返回 MemoryStream，未加密返回原流
            if (_fileReader != null)
                return _fileReader.OpenRead(physicalPath);
            return File.OpenRead(physicalPath);
        }

        return null;
    }

    public string? ReadText(string path)
    {
        using var stream = Open(path);
        if (stream == null) return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public bool Exists(string path)
    {
        try
        {
            var uri = ToAvaloniaUri(path);
            if (uri.StartsWith("avares://"))
            {
                try { return AssetLoader.Exists(new Uri(uri)); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AvaloniaAssetAccessor] AssetLoader.Exists failed for '{uri}': {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AvaloniaAssetAccessor] Exists fallback: {ex.Message}");
        }

        var resolvedDir = ResolveStoryDirectory(path);
        if (File.Exists(resolvedDir)) return true;
        return File.Exists(path);
    }

    private static string? ResolveStoryDirectory(string path)
    {
        var baseDir = AppContext.BaseDirectory;
        var alt = Path.Combine(baseDir, "..", "..", "..", "..", path);
        if (File.Exists(alt)) return alt;

        alt = Path.Combine(baseDir, path);
        if (File.Exists(alt)) return alt;

        return null;
    }
}
