using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Abstractions.Entities.Events;

/// <summary>
/// 时间事件注册信息（内存中，不序列化）
/// <para>回调驱动：时间到达时执行 Callback（C#）或 Commands（DSL）。</para>
/// <para>事件注册后持久存活，直到手动 UnregisterEvent(id) 或单次触发后自动移除。</para>
/// <para>不按场景自动清理——后台记录事件可跨场景存活。</para>
/// </summary>
public class TimeEventRegistration
{
    /// <summary>事件 ID（强制手写，用于去重和存档）</summary>
    public required string Id { get; init; }

    /// <summary>触发小时（0-23）</summary>
    public int Hour { get; init; }

    /// <summary>触发分钟（null=整点触发，即 minute=0 时匹配）</summary>
    public int? Minute { get; init; }

    /// <summary>
    /// 天数约束（null=不限制天数）
    /// <para>当 IsOneShot=true 时：表示在第 Day 天触发（绝对天数，对应 CurrentDay）</para>
    /// <para>当 IsOneShot=false 时：表示每隔 Day 天触发一次（间隔天数，从游戏开始算起）</para>
    /// <para>与 DaysOfWeek 正交——同时指定时两者都需满足。</para>
    /// </summary>
    public int? Day { get; init; }

    /// <summary>触发的星期几（null=每天）</summary>
    public DayOfWeek[]? DaysOfWeek { get; init; }

    /// <summary>是否只触发一次</summary>
    public bool IsOneShot { get; init; }

    /// <summary>C# 回调（与 Commands 二选一）</summary>
    public Func<Task>? Callback { get; init; }

    /// <summary>DSL 编译的命令列表（与 Callback 二选一）</summary>
    public IReadOnlyList<ICommand>? Commands { get; init; }

    /// <summary>优先级（数值越大越先执行）</summary>
    public int Priority { get; init; }

    /// <summary>条件表达式（可选）</summary>
    public string? Condition { get; init; }

    /// <summary>事件描述</summary>
    public string? Description { get; init; }

    /// <summary>
    /// 是否为旧版导航驱动事件（内部标记）
    /// <para>true = 通过 RegisterEvent(TimeEventEntity) 注册，存档时可序列化为 TimeEventEntity。</para>
    /// <para>false = 回调驱动事件（C# Callback 或 DSL Commands），存档时只持久化 ID（TimeEventSaveState），</para>
    /// <para>      场景初始化时重新注册回调，不通过 TimeEvents 序列化。</para>
    /// </summary>
    public bool IsLegacyNavigation { get; init; }
}
