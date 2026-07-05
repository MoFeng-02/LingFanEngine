﻿﻿namespace LingFanEngine.Abstractions.Entities.Events;

/// <summary>
/// 由时间驱动的自动触发事件，与“每秒一分钟”时间系统配合。
/// </summary>
public class TimeEventEntity : BaseEntity
{
    /// <summary>
    /// 触发的游戏天数（从第0天开始计数）
    /// </summary>
    public int TriggerDay { get; set; }
    /// <summary>
    /// 触发的具体小时（可选，默认为 0）
    /// </summary>
    public int? TriggerHour { get; set; }
    /// <summary>
    /// 触发的具体分钟（可选，默认为 0）
    /// </summary>
    public int? TriggerMinute { get; set; }
    /// <summary>
    /// 触发时导航到的路由路径，可为空
    /// </summary>
    public string? TargetPath { get; set; }
    /// <summary>
    /// 是否只触发一次，默认 true，注意一旦为false那么TriggerDay和TriggerHour就是每几天的具体小时触发一次
    /// </summary>
    public bool IsOneShot { get; set; } = true;
    /// <summary>
    /// 同一天多个事件时的优先级，数值越大越先执行
    /// </summary>
    public int Priority { get; set; }
    /// <summary>
    /// 事件描述，用于日志/调试
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 触发条件表达式（可选）。
    /// <para>非空时，时间条件满足后还会通过表达式引擎求值此条件。</para>
    /// <para>例如 "gold >= 100 && has_met_npc == true"。</para>
    /// <para>为空则仅按时间触发。</para>
    /// </summary>
    public string? Condition { get; set; }
}
