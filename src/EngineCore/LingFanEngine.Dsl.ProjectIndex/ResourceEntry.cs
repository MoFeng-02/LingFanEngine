namespace LingFanEngine.Dsl.ProjectIndex;

/// <summary>单个资源文件的索引条目。</summary>
public sealed class ResourceEntry
{
    public string AbsolutePath { get; }
    public string RelativePath { get; }
    public string Extension { get; }
    public ResourceKind Kind { get; }
    public long Size { get; }

    public ResourceEntry(string absolutePath, string relativePath, string extension, ResourceKind kind, long size)
    {
        AbsolutePath = absolutePath;
        RelativePath = relativePath;
        Extension = extension;
        Kind = kind;
        Size = size;
    }

    /// <summary>人类可读的体积（B/KB/MB/GB），供补全 Detail 与悬停展示。</summary>
    public string FormattedSize
    {
        get
        {
            if (Size < 1024) return $"{Size} B";
            if (Size < 1024 * 1024) return $"{Size / 1024.0:F1} KB";
            if (Size < 1024L * 1024 * 1024) return $"{Size / (1024.0 * 1024):F1} MB";
            return $"{Size / (1024.0 * 1024 * 1024):F1} GB";
        }
    }
}
