using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// PlaybackHandlers 集成测试：shake / toggle_skip / toggle_auto。
/// </summary>
public class PlaybackHandlersTests
{
    [Fact]
    public void ShakeHandler_ActivatesShake()
    {
        var ctx = new FakeCommandContext();
        new ShakeHandler().Handle(new ShakeCommand { Intensity = 12, Duration = 0.3 }, ctx);
        ctx.State.Get<bool>(StateKeys.Shake.Active).Should().BeTrue();
        ctx.State.Get<double>(StateKeys.Shake.Intensity).Should().Be(12);
        ctx.State.Get<double>(StateKeys.Shake.Duration).Should().Be(0.3);
    }

    [Fact]
    public void ToggleSkipHandler_TogglesAndDisablesAuto()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Playback.AutoActive, true);
        new ToggleSkipHandler().Handle(new ToggleSkipCommand(), ctx);
        ctx.State.Get<bool>(StateKeys.Playback.SkipActive).Should().BeTrue();
        ctx.State.Get<bool>(StateKeys.Playback.AutoActive).Should().BeFalse();
        ctx.State.Get<bool>(StateKeys.Rollback.IsActive).Should().BeFalse();
    }

    [Fact]
    public void ToggleAutoHandler_TogglesAndDisablesSkip()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Playback.SkipActive, true);
        new ToggleAutoHandler().Handle(new ToggleAutoCommand(), ctx);
        ctx.State.Get<bool>(StateKeys.Playback.AutoActive).Should().BeTrue();
        ctx.State.Get<bool>(StateKeys.Playback.SkipActive).Should().BeFalse();
    }
}
