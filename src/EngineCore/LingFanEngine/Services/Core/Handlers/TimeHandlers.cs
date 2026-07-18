using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Events;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>
/// 时间事件注册命令处理器
/// <para>将 TimeEventEntity 注册到 IEventScheduler。</para>
/// <para>需要 EnableTimeSystem=true + IEventScheduler 可用。</para>
/// </summary>
public class TimeEventHandler : ICommandHandler<TimeEventCommand>, IDefaultCommandHandler
{
    public void Handle(TimeEventCommand cmd, ICommandContext ctx)
    {
        if (ctx.EventScheduler == null)
        {
            System.Diagnostics.Debug.WriteLine(
                "[TimeEventHandler] IEventScheduler 不可用，无法注册时间事件");
            return;
        }

        var evt = new TimeEventEntity
        {
            TriggerDay = cmd.TriggerDay,
            DaysOfWeek = cmd.DaysOfWeek,
            TriggerHour = cmd.TriggerHour,
            TriggerMinute = cmd.TriggerMinute,
            TargetPath = cmd.Target,
            IsOneShot = cmd.IsOneShot,
            Condition = cmd.Condition,
            Description = cmd.Description
        };

        ctx.EventScheduler.RegisterEvent(evt);
    }
}

/// <summary>
/// 回调驱动时间事件注册命令处理器
/// <para>将 SetTimeEventCommand 转换为 TimeEventRegistration 并注册到 IEventScheduler。</para>
/// <para>需要 EnableTimeSystem=true + IEventScheduler 可用。</para>
/// </summary>
public class SetTimeEventHandler : ICommandHandler<SetTimeEventCommand>, IDefaultCommandHandler
{
    public void Handle(SetTimeEventCommand cmd, ICommandContext ctx)
    {
        if (ctx.EventScheduler == null)
        {
            System.Diagnostics.Debug.WriteLine(
                "[SetTimeEventHandler] IEventScheduler 不可用，无法注册时间事件");
            return;
        }

        ctx.EventScheduler.RegisterEvent(cmd.ToRegistration());
    }
}

/// <summary>
/// 注销时间事件命令处理器
/// <para>从 IEventScheduler 中按 ID 移除事件，支持三模式注销。</para>
/// <para>Phase 63：按 Mode 分发——Normal/Permanent/Temporary。</para>
/// <para>需要 EnableTimeSystem=true + IEventScheduler 可用。</para>
/// </summary>
public class UnregisterTimeEventHandler : ICommandHandler<UnregisterTimeEventCommand>, IDefaultCommandHandler
{
    public void Handle(UnregisterTimeEventCommand cmd, ICommandContext ctx)
    {
        if (ctx.EventScheduler == null)
        {
            System.Diagnostics.Debug.WriteLine(
                "[UnregisterTimeEventHandler] IEventScheduler 不可用，无法注销时间事件");
            return;
        }

        var removed = ctx.EventScheduler.UnregisterEvent(cmd.Id, cmd.Mode);
        if (!removed && cmd.Mode == UnregisterMode.Normal)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[UnregisterTimeEventHandler] 事件 [{cmd.Id}] 不存在或已被移除");
        }
        else if (cmd.Mode != UnregisterMode.Normal)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[UnregisterTimeEventHandler] 事件 [{cmd.Id}] 已按 {cmd.Mode} 模式注销");
        }
    }
}

/// <summary>
/// 恢复时间事件命令处理器
/// <para>Phase 63 新增——恢复已注销的事件。</para>
/// <para>支持两种恢复场景：</para>
/// <para>1. Temporary 模式注销的事件：清除 SuspendedIds 标记后从全局注册表重新注册</para>
/// <para>2. Normal 模式注销的 C# 声明式事件：直接从全局注册表重新注册（Normal 不加标记）</para>
/// <para>EventScheduler 保持职责单一，不持有 ITimeEventRegistry 引用。</para>
/// <para>Phase 63 修复：C# 声明式事件在 RegisterScriptEntry 时已通过 RegisterDeclaration
/// 纳入全局注册表，因此 restore_time_event 统一通过 TimeEventRegistry 查找，不再限定当前场景。</para>
/// </summary>
public class RestoreTimeEventHandler : ICommandHandler<RestoreTimeEventCommand>, IDefaultCommandHandler
{
    public void Handle(RestoreTimeEventCommand cmd, ICommandContext ctx)
    {
        if (ctx.EventScheduler == null)
        {
            System.Diagnostics.Debug.WriteLine(
                "[RestoreTimeEventHandler] IEventScheduler 不可用，无法恢复时间事件");
            return;
        }

        // 1. 先尝试清除暂时销毁标记（Temporary 模式注销的事件）
        bool wasSuspended = ctx.EventScheduler.RestoreEvent(cmd.Id);

        // 2. 检查是否被永久销毁（Permanent 模式注销的事件不可恢复）
        if (ctx.EventScheduler.IsBlocked(cmd.Id))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[RestoreTimeEventHandler] 事件 [{cmd.Id}] 已被永久销毁或已触发（单次），无法恢复");
            return;
        }

        // 3. 从全局注册表查回定义并重新注册（DSL + C# 声明式事件统一）
        if (ctx.TimeEventRegistry?.TryGetRegistration(cmd.Id, out var registration) == true)
        {
            ctx.EventScheduler.RegisterEvent(registration);
            System.Diagnostics.Debug.WriteLine(
                $"[RestoreTimeEventHandler] 事件 [{cmd.Id}] 已从全局注册表恢复" +
                (wasSuspended ? "（Temporary 模式）" : "（Normal 模式）"));
            return;
        }

        // 4. 未在全局注册表中找到——可能是动态注册的事件（Run() 中 SetTimeEventAsync）
        if (wasSuspended)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[RestoreTimeEventHandler] 事件 [{cmd.Id}] 暂时销毁标记已清除，但未在全局注册表中找到定义" +
                "（动态注册的事件需 Run() 重执行恢复）");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine(
                $"[RestoreTimeEventHandler] 事件 [{cmd.Id}] 未被暂停且未在全局注册表中找到定义");
        }
    }
}

/// <summary>
/// 暂停游戏时间命令处理器
/// </summary>
public class TimePauseHandler : ICommandHandler<TimePauseCommand>, IDefaultCommandHandler
{
    public void Handle(TimePauseCommand cmd, ICommandContext ctx)
    {
        ctx.State.Set(StateKeys.GameTime.Paused, true);
    }
}

/// <summary>
/// 恢复游戏时间命令处理器
/// </summary>
public class TimeResumeHandler : ICommandHandler<TimeResumeCommand>, IDefaultCommandHandler
{
    public void Handle(TimeResumeCommand cmd, ICommandContext ctx)
    {
        ctx.State.Set(StateKeys.GameTime.Paused, false);
    }
}

/// <summary>
/// 批量跳过游戏时间命令处理器
/// <para>逐分钟 Tick，确保中间所有时间事件被检查。</para>
/// </summary>
public class SkipTimeHandler : ICommandHandler<SkipTimeCommand>, IDefaultCommandHandler
{
    public void Handle(SkipTimeCommand cmd, ICommandContext ctx)
    {
        // 通过状态键获取 GameTimeService 实例不可行（它不在 StateContainer 中），
        // 所以直接通过 ICommandContext 的 TimeService 属性访问
        ctx.TimeService?.SkipTime(cmd.Minutes);
    }
}
