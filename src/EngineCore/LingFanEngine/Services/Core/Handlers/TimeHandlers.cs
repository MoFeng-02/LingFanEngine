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
