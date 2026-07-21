using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Core.Handlers;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// ResetGameStateHandler 测试：断言用户变量被清除、系统偏好保留、交互/菜单标记重置。
/// </summary>
public class ResetGameStateHandlerTests
{
    [Fact]
    public void Handle_RemovesUserVariables()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set("player_hp", 100);
        ctx.State.Set("gold", 50);
        var handler = new ResetGameStateHandler();

        handler.Handle(new ResetGameStateCommand(), ctx);

        ctx.State.ContainsKey("player_hp").Should().BeFalse();
        ctx.State.ContainsKey("gold").Should().BeFalse();
    }

    [Fact]
    public void Handle_PreservesSystemKeys()
    {
        var ctx = new FakeCommandContext();
        // __ 前缀的系统键应保留
        ctx.State.Set("__system_flag", "keep");
        ctx.State.Set("user_var", "drop");
        var handler = new ResetGameStateHandler();

        handler.Handle(new ResetGameStateCommand(), ctx);

        ctx.State.ContainsKey("__system_flag").Should().BeTrue();
        ctx.State.ContainsKey("user_var").Should().BeFalse();
    }

    [Fact]
    public void Handle_RemovesLocalVariables()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set("_local_temp", 123);
        var handler = new ResetGameStateHandler();

        handler.Handle(new ResetGameStateCommand(), ctx);

        ctx.State.ContainsKey("_local_temp").Should().BeFalse();
    }

    [Fact]
    public void Handle_ResetsSkipAndAutoFlags()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Playback.SkipActive, true);
        ctx.State.Set(StateKeys.Playback.AutoActive, true);
        var handler = new ResetGameStateHandler();

        handler.Handle(new ResetGameStateCommand(), ctx);

        ctx.State.Get<bool>(StateKeys.Playback.SkipActive).Should().BeFalse();
        ctx.State.Get<bool>(StateKeys.Playback.AutoActive).Should().BeFalse();
    }

    [Fact]
    public void Handle_ClearsMenuReturnMarker()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Scene.MenuReturnTo, "town");
        var handler = new ResetGameStateHandler();

        handler.Handle(new ResetGameStateCommand(), ctx);

        ctx.State.Get<string>(StateKeys.Scene.MenuReturnTo).Should().BeNull();
    }

    [Fact]
    public void Handle_CallsResetInteractionState()
    {
        var ctx = new FakeCommandContext();
        var handler = new ResetGameStateHandler();

        handler.Handle(new ResetGameStateCommand(), ctx);

        ctx.ResetInteractionStateCalls.Should().BeGreaterThan(0);
    }
}
