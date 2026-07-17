namespace LingFanEngine.Abstractions.Entities.Events;

/// <summary>
/// 时间事件存档状态（序列化到存档）
/// <para>只持久化 ID 集合，回调本身在场景初始化时重新注册。</para>
/// </summary>
public class TimeEventSaveState
{
    /// <summary>已注册的事件 ID 集合</summary>
    public HashSet<string> RegisteredIds { get; set; } = new();

    /// <summary>已触发的单次事件 ID 集合（读档后不再注册）</summary>
    public HashSet<string> FiredOneShotIds { get; set; } = new();
}
