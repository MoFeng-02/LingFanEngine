namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 只读游戏状态接口
/// <para>DLC/Mod 只能通过此接口获取只读数据快照，不能修改状态容器。</para>
/// <para>这是引擎的安全边界——防止第三方 DLC 污染运行时状态。</para>
/// </summary>
public interface IReadOnlyGameState
{
    /// <summary>
    /// 获取指定键的值，不存在返回 default(T)
    /// </summary>
    T? Get<T>(string key);

    /// <summary>
    /// 尝试获取指定键的值
    /// </summary>
    bool TryGet<T>(string key, out T? value);

    /// <summary>
    /// 检查键是否存在
    /// </summary>
    bool ContainsKey(string key);

    /// <summary>
    /// 获取所有键（只读快照）
    /// </summary>
    IEnumerable<string> Keys { get; }

    /// <summary>
    /// 获取当前完整快照的只读字典
    /// </summary>
    IReadOnlyDictionary<string, object?> GetSnapshot();

    /// <summary>
    /// 获取当前游戏时间总分钟数
    /// </summary>
    long GameTotalMinutes { get; }

    /// <summary>
    /// 当前路由路径
    /// </summary>
    string? CurrentPath { get; }

    /// <summary>
    /// 当前场景名称
    /// </summary>
    string? CurrentSceneName { get; }
}
