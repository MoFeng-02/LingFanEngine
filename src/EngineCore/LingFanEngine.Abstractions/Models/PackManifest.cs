namespace LingFanEngine.Abstractions.Models;

/// <summary>
/// 资源包清单——.lfpack 文件的元数据
/// <para>存储在 .lfpack 文件头部，包含包名、版本、文件列表等元信息。</para>
/// <para>序列化使用 LfJsonContext（AOT 友好）。</para>
/// </summary>
public class PackManifest
{
    /// <summary>包名称（如 "chapter1"）</summary>
    public string Name { get; set; } = "";

    /// <summary>包版本号</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>创建时间戳</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>包内文件相对路径列表</summary>
    public List<string> Files { get; set; } = [];

    /// <summary>引擎版本（用于兼容性检查）</summary>
    public string EngineVersion { get; set; } = "";

    /// <summary>包描述（可选）</summary>
    public string? Description { get; set; }
}
