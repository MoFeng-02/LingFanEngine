namespace LingFanEngine.Abstractions.Models;

/// <summary>
/// 章节条目（对标 Ren'Py 章节解锁系统）
/// <para>每次 chapter unlock 命令执行时记录一条，UI 层通过 StateKeys.Chapters.Unlocked 读取列表渲染章节选择界面。</para>
/// </summary>
public class ChapterEntry
{
    /// <summary>章节唯一标识符</summary>
    public string Id { get; set; } = "";

    /// <summary>章节显示名称（可选）</summary>
    public string? Name { get; set; }

    /// <summary>是否已解锁</summary>
    public bool Unlocked { get; set; } = true;

    /// <summary>解锁时间戳</summary>
    public DateTimeOffset UnlockedAt { get; set; } = DateTimeOffset.UtcNow;
}
