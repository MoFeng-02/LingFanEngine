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

    [Fact]
    public void NvlHandler_Auto_ActivatesNvlAndScopedAuto()
    {
        var ctx = new FakeCommandContext();
        // 先处于 skip 模式，验证 nvl auto 会互斥关闭 skip
        ctx.State.Set(StateKeys.Playback.SkipActive, true);
        new NvlHandler().Handle(new NvlCommand { IsAuto = true }, ctx);
        ctx.State.Get<bool>(StateKeys.Nvl.Active).Should().BeTrue();
        ctx.State.Get<bool>(StateKeys.Playback.AutoActive).Should().BeTrue();
        ctx.State.Get<bool>(StateKeys.Nvl.AutoScoped).Should().BeTrue();
        ctx.State.Get<bool>(StateKeys.Playback.SkipActive).Should().BeFalse();
    }

    [Fact]
    public void NvlHandler_AutoExit_ClearsScopedAutoButNotExternalAuto()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Nvl.Active, true);
        ctx.State.Set(StateKeys.Nvl.Text, "累积文本");
        // 由 nvl auto 开启的作用域自动
        ctx.State.Set(StateKeys.Playback.AutoActive, true);
        ctx.State.Set(StateKeys.Nvl.AutoScoped, true);
        new NvlHandler().Handle(new NvlCommand { IsExit = true }, ctx);
        ctx.State.Get<bool>(StateKeys.Nvl.Active).Should().BeFalse();
        // 作用域收尾：自动模式被关闭、标记清除
        ctx.State.Get<bool>(StateKeys.Playback.AutoActive).Should().BeFalse();
        ctx.State.Get<bool>(StateKeys.Nvl.AutoScoped).Should().BeFalse();
    }

    [Fact]
    public void NvlHandler_Exit_WithoutScopedAuto_LeavesExternalAutoUntouched()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Nvl.Active, true);
        // 用户用全局 auto 开启的自动（非 nvl auto），不应被普通 nvl exit 关闭
        ctx.State.Set(StateKeys.Playback.AutoActive, true);
        ctx.State.Set(StateKeys.Nvl.AutoScoped, false);
        new NvlHandler().Handle(new NvlCommand { IsExit = true }, ctx);
        ctx.State.Get<bool>(StateKeys.Playback.AutoActive).Should().BeTrue();
    }

    [Fact]
    public void NvlHandler_Clear_DoesNotTouchAutoScope()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Nvl.Active, true);
        ctx.State.Set(StateKeys.Nvl.Text, "累积文本");
        ctx.State.Set(StateKeys.Playback.AutoActive, true);
        ctx.State.Set(StateKeys.Nvl.AutoScoped, true);
        new NvlHandler().Handle(new NvlCommand { IsClear = true }, ctx);
        // clear 只清文本，不退出 NVL、不碰自动作用域
        ctx.State.Get<bool>(StateKeys.Nvl.Active).Should().BeTrue();
        ctx.State.Get<bool>(StateKeys.Nvl.AutoScoped).Should().BeTrue();
        ctx.State.Get<bool>(StateKeys.Playback.AutoActive).Should().BeTrue();
    }
}
