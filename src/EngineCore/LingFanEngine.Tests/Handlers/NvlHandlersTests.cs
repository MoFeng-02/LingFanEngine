using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// NvlHandler 集成测试：进入 / 清空 / 退出 NVL 模式。
/// </summary>
public class NvlHandlersTests
{
    [Fact]
    public void NvlHandler_Enter_ActivatesNvlMode()
    {
        var ctx = new FakeCommandContext();
        new NvlHandler().Handle(new NvlCommand(), ctx);
        ctx.State.Get<bool>(StateKeys.Nvl.Active).Should().BeTrue();
    }

    [Fact]
    public void NvlHandler_Clear_KeepsActiveButEmptiesText()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Nvl.Active, true);
        ctx.State.Set(StateKeys.Nvl.Text, "累积文本");
        new NvlHandler().Handle(new NvlCommand { IsClear = true }, ctx);
        ctx.State.Get<bool>(StateKeys.Nvl.Active).Should().BeTrue();
        ctx.State.Get<string>(StateKeys.Nvl.Text).Should().BeEmpty();
    }

    [Fact]
    public void NvlHandler_Exit_DeactivatesAndClears()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Nvl.Active, true);
        ctx.State.Set(StateKeys.Nvl.Text, "累积文本");
        new NvlHandler().Handle(new NvlCommand { IsExit = true }, ctx);
        ctx.State.Get<bool>(StateKeys.Nvl.Active).Should().BeFalse();
        ctx.State.Get<string>(StateKeys.Nvl.Text).Should().BeEmpty();
    }
}
