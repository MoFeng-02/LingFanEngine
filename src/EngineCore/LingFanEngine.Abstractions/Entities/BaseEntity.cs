namespace LingFanEngine.Abstractions.Entities;

/// <summary>
/// 所有实体的基类
/// <para>使用 Guid v7（有序版本）生成 Id，便于按创建顺序排序。</para>
/// </summary>
public class BaseEntity
{
    /// <summary>
    /// 唯一标识（Guid v7 有序版本）
    /// </summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// 命令方法，如 "Navigate", "ShowDialog", "PlayBgm"
    /// </summary>
    public string? Command { get; set; }

    /// <summary>
    /// 命令参数，消费方自行解析
    /// </summary>
    public object? CommandValue { get; set; }
}
