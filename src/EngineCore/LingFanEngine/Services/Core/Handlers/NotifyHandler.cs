using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>
/// 通知命令处理器
/// <para>将通知加入队列（StateKeys.Notify.Queue），由 OverlayRenderer 排队显示。</para>
/// <para>如果当前没有正在显示的通知，同时写入 StateKeys.Notify.Text 触发立即显示。</para>
/// </summary>
public class NotifyHandler : ICommandHandler<NotifyCommand>, IDefaultCommandHandler
{
    public void Handle(NotifyCommand cmd, ICommandContext ctx)
    {
        var item = new NotificationItem
        {
            Text = cmd.Text,
            Type = cmd.Type,
            Duration = cmd.Duration > 0 ? cmd.Duration : 3.0
        };

        // 加入队列
        var queue = ctx.State.Get<List<NotificationItem>>(StateKeys.Notify.Queue) ?? [];

        // 检查当前是否有正在显示的通知
        var currentText = ctx.State.Get<string>(StateKeys.Notify.Text);
        if (string.IsNullOrEmpty(currentText))
        {
            // 没有正在显示的通知——立即显示
            ctx.State.Set(StateKeys.Notify.Text, item.Text);
            ctx.State.Set(StateKeys.Notify.Type, item.Type);
            ctx.State.Set(StateKeys.Notify.Duration, item.Duration);
        }
        else
        {
            // 有正在显示的通知——排队等待
            queue.Add(item);
            ctx.State.Set(StateKeys.Notify.Queue, queue);
        }
    }
}
