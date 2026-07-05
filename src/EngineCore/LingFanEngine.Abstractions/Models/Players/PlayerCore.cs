namespace LingFanEngine.Abstractions.Models.Players;

/// <summary>
/// 玩家核心，核心无数值，只有提供了名称和性别，但是可以为空，毕竟神是可以没有性别的哈哈哈哈哈
/// </summary>
public class PlayerCore : BaseModel
{
    /// <summary>
    /// 角色名称
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// 性别，不绑定类型，因为，看逻辑层用户自己怎么用
    /// </summary>
    public object? Gender { get; set; }
    /// <summary>
    /// 主角说明
    /// </summary>
    public string? Description { get; set; }
}
