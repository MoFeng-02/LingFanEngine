namespace LingFanEngine.Abstractions.Models.Saves;

/// <summary>
/// 场景堆栈完整快照——唯一 ID + 场景名 + 全量用户状态
/// </summary>
public class SceneSnapshot
{
    /// <summary>唯一 ID（用于存档比较和堆栈操作）</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>场景名</summary>
    public required string SceneName { get; set; }

    /// <summary>全量用户状态快照（不含 __ 系统变量）</summary>
    public Dictionary<string, object?> State { get; set; } = new();
}
