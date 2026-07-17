using LingFanEngine.Abstractions.Entities.Events;

namespace LingFanEngine.Abstractions.Interfaces.Events;

/// <summary>
/// 事件调度器接口
/// <para>监听 IGameTimeService 的 OnTimeAdvanced 事件，匹配时间条件后排队等待 DslExecutor 执行。</para>
/// <para>回调驱动：支持 C# Func&lt;Task&gt; 和 DSL ICommand[] 两种回调形式。</para>
/// <para>事件持久存活——不自动清理，只能通过 UnregisterEvent(id) 手动删除或单次触发后自动移除。</para>
/// </summary>
public interface IEventScheduler : IDisposable
{
    // ========== 注册（回调驱动） ==========

    /// <summary>
    /// 注册回调驱动的时间事件
    /// </summary>
    /// <param name="registration">事件注册信息</param>
    /// <returns>true=注册成功，false=ID 已存在或已在已触发单次列表中</returns>
    bool RegisterEvent(TimeEventRegistration registration);

    /// <summary>
    /// 按 ID 移除事件
    /// </summary>
    bool UnregisterEvent(string id);

    // ========== 兼容旧 API（导航驱动） ==========

    /// <summary>
    /// 注册导航驱动的时间事件（Phase 60 兼容，底层转换为回调）
    /// </summary>
    void RegisterEvent(TimeEventEntity evt);

    /// <summary>批量注册导航驱动的时间事件</summary>
    void RegisterEvents(IEnumerable<TimeEventEntity> events);

    // ========== 清理 ==========

    /// <summary>清除所有时间事件</summary>
    void ClearEvents();

    // ========== 出队（DslExecutor 调用） ==========

    /// <summary>
    /// 尝试出队一个待处理的时间事件
    /// </summary>
    /// <param name="evt">出队的事件注册信息</param>
    /// <returns>true=有待处理事件</returns>
    bool TryDequeuePendingEvent(out TimeEventRegistration? evt);

    /// <summary>
    /// 标记单次事件已触发（自动移除并记录到已触发列表）
    /// </summary>
    void MarkFired(string eventId);

    // ========== 查询 ==========

    /// <summary>当前已注册事件数量</summary>
    int EventCount { get; }

    /// <summary>
    /// 获取当前已注册事件的只读快照（用于调试/显示）
    /// </summary>
    IReadOnlyList<TimeEventEntity> GetRegisteredEvents();

    // ========== 存档 ==========

    /// <summary>
    /// 获取存档状态（registeredIds + firedOneShotIds）
    /// </summary>
    TimeEventSaveState GetSaveState();

    /// <summary>
    /// 应用存档状态
    /// <para>清空当前事件，恢复 firedOneShotIds。</para>
    /// <para>RegisteredIds 不直接恢复——场景初始化时重新注册回调。</para>
    /// <para>未重新注册的 ID 自然成为垃圾被丢弃（不在 _events 中即不存在）。</para>
    /// </summary>
    void ApplySaveState(TimeEventSaveState? state);
}
