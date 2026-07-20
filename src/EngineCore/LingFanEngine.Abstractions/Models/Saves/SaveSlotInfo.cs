namespace LingFanEngine.Abstractions.Models.Saves;

/// <summary>
/// 存档槽信息
/// <para>用于存档列表展示，不包含完整存档数据</para>
/// </summary>
public class SaveSlotInfo
{
    /// <summary>
    /// 存档槽唯一标识
    /// </summary>
    public required string SlotId { get; set; }

    /// <summary>
    /// 存档名称
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTimeOffset CreateTime { get; set; }

    /// <summary>
    /// 修改时间
    /// </summary>
    public DateTimeOffset UpdateTime { get; set; }

    /// <summary>
    /// 缩略图数据（可选）
    /// </summary>
    public byte[]? Thumbnail { get; set; }

    /// <summary>
    /// 游戏版本
    /// </summary>
    public string? GameVersion { get; set; }
}