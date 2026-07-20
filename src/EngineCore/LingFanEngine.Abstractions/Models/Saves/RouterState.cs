using System.Diagnostics.CodeAnalysis;

namespace LingFanEngine.Abstractions.Models.Saves;

/// <summary>
/// 路由状态
/// <para>记录当前路由及其场景状态</para>
/// </summary>
[method: SetsRequiredMembers()]
public class RouterState()
{
    /// <summary>
    /// 路由路径
    /// </summary>
    public required string Path { get; set; } = null!;

    /// <summary>
    /// 当前场景在路由中的索引
    /// </summary>
    public int CurrentSceneIndex { get; set; }

    /// <summary>
    /// 该路由下各场景的状态
    /// </summary>
    public List<SceneState> SceneStates { get; set; } = [];

}