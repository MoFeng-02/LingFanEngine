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
}
