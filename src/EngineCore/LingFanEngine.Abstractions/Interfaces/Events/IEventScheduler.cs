using LingFanEngine.Abstractions.Entities.Events;

namespace LingFanEngine.Abstractions.Interfaces.Events;

/// <summary>
/// 事件注销模式（Phase 63 三模式注销系统）
/// </summary>
public enum UnregisterMode
{
    /// <summary>
    /// 正常注销：从 _events 移除，不加标记
    /// <para>DSL 事件由 Run() 重执行恢复，C# 声明式事件由 restore_time_event 或读档恢复</para>
    /// <para>已入队的待执行事件仍会执行（软注销语义）</para>
    /// </summary>
    Normal,

    /// <summary>
    /// 永久销毁：加入 DestroyedIds，永不注册（即使代码再次执行 set_time_event）
    /// </summary>
    Permanent,

    /// <summary>
    /// 暂时销毁：加入 SuspendedIds，不重注册，但可通过 restore_time_event 恢复
    /// </summary>
    Temporary
}

/// <summary>
/// 事件调度器接口
/// <para>监听 IGameTimeService 的 OnTimeAdvanced 事件，匹配时间条件后排队等待 DslExecutor 执行。</para>
/// <para>回调驱动：支持 C# Func&lt;Task&gt; 和 DSL ICommand[] 两种回调形式。</para>
/// <para>事件持久存活——不自动清理，只能通过 UnregisterEvent(id) 手动删除或单次触发后自动移除。</para>
/// <para>Phase 63：三模式注销（Normal/Permanent/Temporary）+ RestoreEvent + IsBlocked。</para>
/// </summary>
public interface IEventScheduler : IDisposable
{
    // ========== 注册（回调驱动） ==========

    /// <summary>
    /// 注册回调驱动的时间事件
    /// <para>Phase 63：四层检查顺序——Destroyed > Suspended > FiredOneShot > TryAdd</para>
    /// </summary>
    /// <param name="registration">事件注册信息</param>
    /// <returns>true=注册成功，false=被阻止（已销毁/已挂起/已触发/已存在）</returns>
    bool RegisterEvent(TimeEventRegistration registration);

    /// <summary>
    /// 按 ID 移除事件（正常注销模式）
    /// </summary>
    bool UnregisterEvent(string id);

    /// <summary>
    /// 按 ID 移除事件（指定注销模式）
    /// <para>Phase 63 新增——支持三模式注销。</para>
    /// </summary>
    /// <param name="id">事件 ID</param>
    /// <param name="mode">注销模式</param>
    bool UnregisterEvent(string id, UnregisterMode mode);

    /// <summary>
    /// 恢复暂时销毁的事件（只清除标记，不查表注册）
    /// <para>Phase 63 新增——实际重新注册由 RestoreTimeEventHandler 查 ITimeEventRegistry 完成。</para>
    /// <para>EventScheduler 保持职责单一，不持有 ITimeEventRegistry 引用。</para>
    /// <para>返回值：true=之前处于暂时销毁状态（已清除标记），false=未处于暂时销毁状态。</para>
    /// <para>注意：返回 false 不代表恢复失败——Normal 模式注销的事件没有标记，但仍可恢复。</para>
    /// </summary>
    /// <param name="id">事件 ID</param>
    /// <returns>true=清除成功（之前处于暂时销毁状态），false=未处于暂时销毁状态</returns>
    bool RestoreEvent(string id);

    /// <summary>
    /// 检查事件是否被阻止注册
    /// <para>Phase 63 新增——检查 DestroyedIds / SuspendedIds / FiredOneShotIds。</para>
    /// </summary>
    bool IsBlocked(string id);

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
    /// 获取存档状态（registeredIds + firedOneShotIds + destroyedIds + suspendedIds）
    /// </summary>
    TimeEventSaveState GetSaveState();

    /// <summary>
    /// 应用存档状态
    /// <para>清空当前事件，恢复 firedOneShotIds / destroyedIds / suspendedIds。</para>
    /// <para>RegisteredIds 不直接恢复——场景初始化时重新注册回调。</para>
    /// <para>未重新注册的 ID 自然成为垃圾被丢弃（不在 _events 中即不存在）。</para>
    /// </summary>
    void ApplySaveState(TimeEventSaveState? state);
}
