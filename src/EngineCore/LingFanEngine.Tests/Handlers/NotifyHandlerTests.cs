using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// NotifyHandler 集成测试：单条立即显示 + 多条排队。
/// </summary>
public class NotifyHandlerTests
{
    [Fact]
    public void NotifyHandler_FirstNotification_ShowsImmediately()
    {
        var ctx = new FakeCommandContext();
        new NotifyHandler().Handle(new NotifyCommand { Text = "保存成功", Type = "info" }, ctx);
        ctx.State.Get<string>(StateKeys.Notify.Text).Should().Be("保存成功");
        ctx.State.Get<string>(StateKeys.Notify.Type).Should().Be("info");
    }

    [Fact]
    public void NotifyHandler_SecondWhileShowing_GetsQueued()
    {
        var ctx = new FakeCommandContext();
        new NotifyHandler().Handle(new NotifyCommand { Text = "第一条" }, ctx);
        new NotifyHandler().Handle(new NotifyCommand { Text = "第二条" }, ctx);
        // 第一条仍显示中，第二条进入队列
        ctx.State.Get<string>(StateKeys.Notify.Text).Should().Be("第一条");
        ctx.State.Get<List<NotificationItem>>(StateKeys.Notify.Queue)
            .Should().ContainSingle(i => i.Text == "第二条");
    }

    [Fact]
    public void NotifyHandler_WhileToastActive_TextConsumed_GetsQueued()
    {
        // 回归（2026-09 通知互相打断 BUG）：真实时序中 OverlayRenderer 消费 Text 的同一帧即置空，
        // Toast 显示的 3 秒里 Text 恒为 null——旧排队判定（仅 Text 非空）在窗口外全部走
        // 「立即显示」分支，后一条直接移除正在显示的 Toast（互相打断、队列永不生效）。
        // 修复后：renderer 显示期间维护 Notify.Active=true，handler 据此排队。
        var ctx = new FakeCommandContext();
        new NotifyHandler().Handle(new NotifyCommand { Text = "第一条" }, ctx);

        // 模拟 renderer 已消费并显示：Text/Type 置空（同帧），Active=true（显示中）
        ctx.State.Set(StateKeys.Notify.Text, (string?)null);
        ctx.State.Set(StateKeys.Notify.Active, true);

        new NotifyHandler().Handle(new NotifyCommand { Text = "第二条" }, ctx);

        // 第二条必须排队（而非写入 Text 触发打断）
        ctx.State.Get<string>(StateKeys.Notify.Text).Should().BeNull("Text 已被消费，第二条不得打断显示中的 Toast");
        ctx.State.Get<List<NotificationItem>>(StateKeys.Notify.Queue)
            .Should().ContainSingle(i => i.Text == "第二条");

        // Toast 完全移除后（renderer 清 Active），队列中的下一条才被触发
        ctx.State.Set(StateKeys.Notify.Active, false);
        new NotifyHandler().Handle(new NotifyCommand { Text = "第三条" }, ctx);
        ctx.State.Get<string>(StateKeys.Notify.Text).Should().Be("第三条", "无 Toast 显示时新通知立即显示");
    }
}
