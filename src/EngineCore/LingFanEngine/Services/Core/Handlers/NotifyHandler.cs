using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>
/// 通知命令处理器
/// <para>将通知加入队列（StateKeys.Notify.Queue），由 OverlayRenderer 排队显示。</para>
/// <para>当前无 Toast 显示中（Text 空且 Active=false）时，写入 Text 触发立即显示。</para>
/// <para>2026-09 修复：排队判定加 Active 标志——仅凭 Text 非空的窗口只有一帧
/// （renderer 消费即置空），Toast 显示中的后续通知曾全部互相打断、队列永不生效。</para>
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

        // 无 Toast 显示中（Text 已被消费置空 且 renderer 未在显示/淡出）——立即显示
        var showing = !string.IsNullOrEmpty(ctx.State.Get<string>(StateKeys.Notify.Text))
            || ctx.State.Get<bool>(StateKeys.Notify.Active);
        if (!showing)
        {
            ctx.State.Set(StateKeys.Notify.Text, item.Text);
            ctx.State.Set(StateKeys.Notify.Type, item.Type);
            ctx.State.Set(StateKeys.Notify.Duration, item.Duration);
        }
        else
        {
            // 显示中——排队等待（copy-on-write：新 List 实例替换，
            // 防与 UI 线程 DequeueNextNotify 的 RemoveAt 就地竞争丢更新）
            var queue = new List<NotificationItem>(ctx.State.Get<List<NotificationItem>>(StateKeys.Notify.Queue) ?? []);
            queue.Add(item);
            ctx.State.Set(StateKeys.Notify.Queue, queue);
        }
    }
}
