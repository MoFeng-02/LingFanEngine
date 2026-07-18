namespace LingFanEngine.Abstractions.Entities.Events;

/// <summary>
/// 时间事件存档状态（序列化到存档）
/// <para>只持久化 ID 集合，回调本身在场景初始化时重新注册。</para>
/// <para>Phase 63 新增 DestroyedIds / SuspendedIds 支持三模式注销。</para>
/// </summary>
public class TimeEventSaveState
{
    /// <summary>已注册的事件 ID 集合（存档时已注册的，读档时重注册过滤用）</summary>
    public HashSet<string> RegisteredIds { get; set; } = new();

    /// <summary>已触发的单次事件 ID 集合（读档后不再注册）</summary>
    public HashSet<string> FiredOneShotIds { get; set; } = new();

    /// <summary>永久销毁的事件 ID 集合（读档后永不注册，即使代码再次执行 set_time_event）</summary>
    public HashSet<string> DestroyedIds { get; set; } = new();

    /// <summary>暂时销毁的事件 ID 集合（读档后不重注册，但可通过 restore_time_event 恢复）</summary>
    public HashSet<string> SuspendedIds { get; set; } = new();
}
