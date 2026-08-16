namespace LingFanEngine.DslCore;

/// <summary>
/// 文件扩展名 / 路径判定数据——用于把字符串字面量识别为路径（资源引用）。
/// <para>原 SDK 在 Highlighter.IsPathValue 中硬编码扩展名列表，现收归 DslCore 单一真相源。</para>
/// </summary>
public static class DslFileExtensions
{
    private static readonly HashSet<string> _extensions = new()
    {
        ".story", ".json", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
        ".mp3", ".ogg", ".wav", ".m4a", ".flac",
        ".mp4", ".webm", ".mkv", ".avi", ".mov",
    };

    /// <summary>所有被识别为资源引用的文件扩展名（小写，含点）。</summary>
    public static IReadOnlySet<string> All => _extensions;

    /// <summary>判断给定字符串是否像路径 / 资源引用（以已知扩展名结尾，或包含路径分隔符）。</summary>
    public static bool IsPathValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var lower = value.ToLowerInvariant();
        foreach (var ext in _extensions)
        {
            if (lower.EndsWith(ext)) return true;
        }
        return value.Contains('/') || value.Contains('\\');
    }
}
