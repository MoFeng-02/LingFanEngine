namespace LingFanEngine.Abstractions.Models.Saves;

/// <summary>
/// 场景状态
/// <para>记录场景内的交互状态（是否已交互）</para>
/// </summary>
public class SceneState
{
    /// <summary>
    /// 场景名称
    /// </summary>
    public required string SceneName { get; set; }

    /// <summary>
    /// 交互状态字典 Key=交互Id，Value=是否已交互
    /// </summary>
    public Dictionary<string, bool> InteractionStates { get; set; } = [];
}