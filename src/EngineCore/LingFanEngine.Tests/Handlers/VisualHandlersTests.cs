using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// VisualHandlers 集成测试：transition / animate。
/// </summary>
public class VisualHandlersTests
{
    [Fact]
    public void TransitionHandler_SetsAllTransitionStateKeys()
    {
        var ctx = new FakeCommandContext();
        new TransitionHandler().Handle(new TransitionCommand { Type = "SlideLeftIn", Duration = 0.8 }, ctx);
        ctx.State.Get<bool>(StateKeys.Transition.Active).Should().BeTrue();
        ctx.State.Get<string>(StateKeys.Transition.Type).Should().Be("SlideLeftIn");
        ctx.State.Get<double>(StateKeys.Transition.Duration).Should().Be(0.8);
        ctx.State.Get<string>(StateKeys.Transition.Easing).Should().Be("EaseOutQuad");
    }

    [Fact]
    public void AnimateHandler_WritesAnimationKeys()
    {
        var ctx = new FakeCommandContext();
        new AnimateHandler().Handle(
            new AnimateCommand { Target = "bg", Property = "x", TargetValue = 100, Duration = 1.2, RepeatCount = 0 }, ctx);
        var prefix = StateKeys.Animation.Prefix + "bg_x";
        ctx.State.Get<double>(prefix + StateKeys.Animation.TargetSuffix).Should().Be(100);
        ctx.State.Get<double>(prefix + StateKeys.Animation.DurationSuffix).Should().Be(1.2);
        ctx.State.Get<bool>(prefix + StateKeys.Animation.ActiveSuffix).Should().BeTrue();
    }
}
