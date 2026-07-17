﻿namespace LingFanEngine.Abstractions.Entities.Events;

/// <summary>
/// 由时间驱动的自动触发事件，与时间系统配合。
/// </summary>
public class TimeEventEntity : BaseEntity
{
    /// <summary>
    /// 触发的游戏天数（与 CurrentDay 同基准，默认 1-based）
    /// <para>仅当 DaysOfWeek 为空时生效。设为 0 表示不按天数过滤（每日触发）。</para>
    /// </summary>
    public int TriggerDay { get; set; }

    /// <summary>
    /// 触发的星期几（null 或空 = 不按星期过滤）
    /// <para>非空时优先于 TriggerDay：仅在指定的星期几触发。</para>
    /// <para>游戏第 1 天 = Monday，7 天一循环。</para>
    /// </summary>
    public DayOfWeek[]? DaysOfWeek { get; set; }

    /// <summary>
    /// 触发的具体小时（null = 任意小时）
    /// </summary>
    public int? TriggerHour { get; set; }

    /// <summary>
    /// 触发的具体分钟（null = 任意分钟）
    /// </summary>
    public int? TriggerMinute { get; set; }

    /// <summary>
    /// 触发时导航到的路由路径，可为空
    /// </summary>
    public string? TargetPath { get; set; }

    /// <summary>
    /// 是否只触发一次（默认 true）
    /// <para>true = 单次：触发后自动移除（如悬赏截止日）。</para>
    /// <para>false = 重复：每次时间条件满足都触发（如 NPC 每日事件、商店每周补货）。</para>
    /// <para>与 DaysOfWeek 组合可实现：单次星期事件（下个周一触发一次）、每周重复事件（每周一触发）。</para>
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
