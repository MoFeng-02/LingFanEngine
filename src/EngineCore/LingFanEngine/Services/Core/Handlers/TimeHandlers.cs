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
/// <para>从 IEventScheduler 中按 ID 移除事件。</para>
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

        var removed = ctx.EventScheduler.UnregisterEvent(cmd.Id);
        if (!removed)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[UnregisterTimeEventHandler] 事件 [{cmd.Id}] 不存在或已被移除");
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
