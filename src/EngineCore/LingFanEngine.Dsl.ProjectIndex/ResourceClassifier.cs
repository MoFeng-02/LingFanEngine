namespace LingFanEngine.Dsl.ProjectIndex;

/// <summary>按扩展名将资源归类（AOT 友好：纯静态字典，无反射）。</summary>
internal static class ResourceClassifier
{
    private static readonly Dictionary<string, ResourceKind> s_map = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".png", ResourceKind.Image }, { ".jpg", ResourceKind.Image }, { ".jpeg", ResourceKind.Image },
        { ".gif", ResourceKind.Image }, { ".webp", ResourceKind.Image }, { ".bmp", ResourceKind.Image },
        { ".tga", ResourceKind.Image }, { ".tiff", ResourceKind.Image }, { ".ico", ResourceKind.Image },
        { ".mp3", ResourceKind.Audio }, { ".wav", ResourceKind.Audio }, { ".ogg", ResourceKind.Audio },
        { ".flac", ResourceKind.Audio }, { ".aac", ResourceKind.Audio }, { ".m4a", ResourceKind.Audio },
        { ".mp4", ResourceKind.Video }, { ".webm", ResourceKind.Video }, { ".mov", ResourceKind.Video },
        { ".avi", ResourceKind.Video }, { ".mkv", ResourceKind.Video },
        { ".ttf", ResourceKind.Font }, { ".otf", ResourceKind.Font }, { ".ttc", ResourceKind.Font },
        { ".woff", ResourceKind.Font }, { ".woff2", ResourceKind.Font },
    };

    public static ResourceKind? Classify(string extension) =>
        s_map.TryGetValue(extension, out var k) ? k : null;

    public static bool IsResourceExtension(string extension) => s_map.ContainsKey(extension);
}
