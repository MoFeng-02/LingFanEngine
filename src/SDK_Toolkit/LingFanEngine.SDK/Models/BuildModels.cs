namespace LingFanEngine.SDK.Models;

/// <summary>构建结果</summary>
public class BuildResult
{
    public bool Success { get; set; }
    public string OutputPath { get; set; } = "";
    public string Platform { get; set; } = "";
    public List<string> Logs { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

/// <summary>资源条目</summary>
public record AssetEntry(string Path, string RelativePath, string FileName, AssetType Type, long SizeBytes)
{
    /// <summary>展示用大小（KB）</summary>
    public double SizeKb => SizeBytes / 1024.0;
}

/// <summary>资源类型</summary>
public enum AssetType
{
    Image,
    Audio,
    Video,
    Story,
    Json,
    Other
}

/// <summary>资源预览信息</summary>
public record AssetPreview(string Path, AssetType Type, string? ThumbnailPath, string? Info);
