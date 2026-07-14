namespace LingFanEngine.Abstractions.Models;

/// <summary>
/// 成就条目（对标 Ren'Py Achievement 系统）
/// <para>每次 achievement unlock 命令执行时记录一条，UI 层通过 StateKeys.Achievements.Unlocked 读取列表渲染成就界面。</para>
/// </summary>
public class AchievementEntry
{
    /// <summary>成就唯一标识符</summary>
    public string Id { get; set; } = "";

    /// <summary>成就显示名称（可选）</summary>
    public string? Name { get; set; }

    /// <summary>解锁时间戳</summary>
    public DateTimeOffset UnlockedAt { get; set; } = DateTimeOffset.UtcNow;
}
