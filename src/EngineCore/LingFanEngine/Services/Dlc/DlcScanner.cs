using System.IO.Compression;
using System.Text.Json;
using LingFanEngine.Abstractions.Serialization;

namespace LingFanEngine.Services.Dlc;

/// <summary>
/// DLC 扫描器
/// <para>启动时扫描指定目录下的 ZIP 包，解压到缓存目录，解析 manifest.json，返回 DlcPackage 列表。</para>
/// </summary>
public class DlcScanner
{
    private readonly string _modsDirectory;
    private readonly string _cacheDirectory;

    /// <summary>
    /// 扫描到的 DLC 包信息
    /// </summary>
    public record DlcPackage(
        string FilePath,
        DlcManifest Manifest,
        string ExtractPath
    );

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="modsDirectory">Mods 目录路径（默认 "./Mods"）</param>
    /// <param name="cacheDirectory">解压缓存目录（默认 "./Mods/.cache"）</param>
    public DlcScanner(string modsDirectory = "./Mods", string? cacheDirectory = null)
    {
        _modsDirectory = modsDirectory;
        _cacheDirectory = cacheDirectory ?? Path.Combine(modsDirectory, ".cache");
    }

    /// <summary>
    /// 扫描 Mods 目录，返回所有有效的 DLC 包
    /// <para>ZIP 包会被解压到缓存目录，JSON 直接模式不解压。</para>
    /// </summary>
    public List<DlcPackage> Scan()
    {
        var result = new List<DlcPackage>();

        if (!Directory.Exists(_modsDirectory))
        {
            Directory.CreateDirectory(_modsDirectory);
            return result;
        }

        var zipFiles = Directory.GetFiles(_modsDirectory, "*.zip", SearchOption.TopDirectoryOnly);
        var jsonFiles = Directory.GetFiles(_modsDirectory, "*.json", SearchOption.TopDirectoryOnly);

        // 扫描 ZIP 包
        foreach (var zipPath in zipFiles)
        {
            try
            {
                var manifest = ReadManifestFromZip(zipPath);
                if (manifest != null)
                {
                    var extractPath = ExtractZipIfNeeded(zipPath, manifest.Id);
                    result.Add(new DlcPackage(zipPath, manifest, extractPath));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DlcScanner] Failed to scan ZIP '{zipPath}': {ex.Message}");
            }
        }

        // 扫描直接 JSON（开发模式，未打包）
        foreach (var jsonPath in jsonFiles)
        {
            if (Path.GetFileName(jsonPath) == "manifest.json")
            {
                try
                {
                    var manifest = ReadManifestFromJson(jsonPath);
                    if (manifest != null)
                    {
                        var dir = Path.GetDirectoryName(jsonPath)!;
                        result.Add(new DlcPackage(jsonPath, manifest, dir));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DlcScanner] Failed to scan JSON '{jsonPath}': {ex.Message}");
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 解压 ZIP 到缓存目录（如果尚未解压或 ZIP 更新）
    /// </summary>
    private string ExtractZipIfNeeded(string zipPath, string dlcId)
    {
        var extractPath = Path.Combine(_cacheDirectory, dlcId);
        var zipLastWrite = File.GetLastWriteTimeUtc(zipPath);

        // 检查是否已解压且是最新版本
        var markerFile = Path.Combine(extractPath, ".extracted");
        if (File.Exists(markerFile))
        {
            var markerContent = File.ReadAllText(markerFile).Trim();
            if (DateTime.TryParse(markerContent, out var extractedTime) && extractedTime >= zipLastWrite)
            {
                // 已是最新版本，无需重新解压
                return extractPath;
            }
        }

        // 解压 ZIP
        Directory.CreateDirectory(_cacheDirectory);
        if (Directory.Exists(extractPath))
            Directory.Delete(extractPath, recursive: true);

        ZipFile.ExtractToDirectory(zipPath, extractPath);

        // 写入解压标记
        File.WriteAllText(markerFile, DateTime.UtcNow.ToString("O"));

        return extractPath;
    }

    /// <summary>
    /// 从 ZIP 包中读取 manifest.json
    /// </summary>
    private static DlcManifest? ReadManifestFromZip(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.GetEntry("manifest.json");
        if (entry == null) return null;

        using var stream = entry.Open();
        var buffer = new byte[entry.Length];
        stream.ReadExactly(buffer);

        var json = System.Text.Encoding.UTF8.GetString(buffer);
        return JsonSerializer.Deserialize(json, LfJsonContext.Default.DlcManifest);
    }

    /// <summary>
    /// 从 JSON 文件读取 DLC manifest
    /// </summary>
    private static DlcManifest? ReadManifestFromJson(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        return JsonSerializer.Deserialize(json, LfJsonContext.Default.DlcManifest);
    }
}
