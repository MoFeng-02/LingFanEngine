namespace LingFanEngine.Abstractions.Models;

/// <summary>
/// CG鉴赏条目（对标 Ren'Py Gallery Action）
/// <para>每次 gallery unlock 命令执行时记录一条，UI 层通过 StateKeys.Gallery.Unlocked 读取列表渲染鉴赏界面。</para>
/// </summary>
public class GalleryEntry
{
    /// <summary>CG 唯一标识符</summary>
    public string Id { get; set; } = "";

    /// <summary>CG 图片路径（相对资源路径）</summary>
    public string ImagePath { get; set; } = "";

    /// <summary>CG 标题（可选，用于鉴赏界面展示）</summary>
    public string? Title { get; set; }

    /// <summary>关联的场景名（可选，用于回想去该场景）</summary>
    public string? SceneName { get; set; }

    /// <summary>解锁时间戳</summary>
    public DateTimeOffset UnlockedAt { get; set; } = DateTimeOffset.UtcNow;
}
